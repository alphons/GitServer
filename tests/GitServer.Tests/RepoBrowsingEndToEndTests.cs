using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GitServer.Data;
using GitServer.Models;
using GitServer.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GitServer.Tests.TestSupport.WebSession;

namespace GitServer.Tests;

/// <summary>Browsing a repository that really contains history: file tree, files, raw downloads, commit list and
/// detail, branches, tags, archives, empty and broken repositories — and that every one of those pages honours
/// the repository's privacy.</summary>
public class RepoBrowsingEndToEndTests : IClassFixture<GitServerFactory>
{
	private readonly GitServerFactory _f;

	public RepoBrowsingEndToEndTests(GitServerFactory factory) => _f = factory;

	private static string Unique(string stem) => stem + Guid.NewGuid().ToString("N")[..6];
	private static string En(string key) =>
		JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(TestPaths.LocalizationRoot, "en", "strings.json")))![key];

	private WebSession Anonymous() => new(_f);
	private async Task<WebSession> AsAsync(AppUser user) => await new WebSession(_f).LoginAsync(user.UserName!);
	private static bool IsLoginRedirect(HttpResponseMessage r) =>
		r.StatusCode == HttpStatusCode.Redirect && r.Headers.Location != null &&
		(r.Headers.Location.IsAbsoluteUri ? r.Headers.Location.AbsolutePath : r.Headers.Location.OriginalString)
			.StartsWith("/Auth/Login", StringComparison.OrdinalIgnoreCase);

	private async Task<(AppUser Owner, GitServerFactory.SeededRepo Seed, string Base)> SeedAsync(bool isPrivate = false, int extraCommits = 0)
	{
		var owner = await _f.CreateUserAsync(Unique("owner"));
		var seed = await _f.SeedHistoryAsync(owner, "proj", isPrivate, extraCommits);
		return (owner, seed, $"/{owner.UserName}/proj");
	}

	// ---- The repository page --------------------------------------------------------------------------

	[Fact]
	public async Task TheRootListsFilesAndFolders_TheCloneUrl_AndTheBranches()
	{
		var (owner, _, url) = await SeedAsync();

		var html = await Anonymous().GetHtmlAsync(url);

		Assert.Contains("README.md", html);
		Assert.Contains("logo.png", html);
		Assert.Contains("href=\"" + url + "?branch=main&amp;path=src\"", html);        // a folder links to itself
		Assert.Contains($"/git/{owner.UserName}/proj.git", html);                       // clone URL with the git prefix
		Assert.Contains("<option value=\"feature\"", html);
		Assert.Contains("<option value=\"main\" selected", html);
		Assert.Contains($"/raw/main/README.md", html);                                   // the readme is rendered from the raw endpoint
	}

	[Fact]
	public async Task AFolderShowsItsFiles_AndABranchShowsItsOwnTree()
	{
		var (_, _, url) = await SeedAsync();
		var session = Anonymous();

		var src = await session.GetHtmlAsync(url + "?branch=main&path=src");
		var featureRoot = await session.GetHtmlAsync(url + "?branch=feature");
		var featureSrc = await session.GetHtmlAsync(url + "?branch=feature&path=src");

		Assert.Contains("app.txt", src);
		Assert.DoesNotContain("feature.txt", src);
		Assert.DoesNotContain("logo.png", featureRoot);                                  // the branch predates the logo
		Assert.Contains("feature.txt", featureSrc);
		Assert.Contains("app.txt", featureSrc);
	}

	[Fact]
	public async Task AnEmptyRepository_ExplainsHowToPush_InsteadOfShowingATree()
	{
		var owner = await _f.CreateUserAsync(Unique("owner"));
		await _f.CreateRepoAsync(owner, "fresh");

		var html = await Anonymous().GetHtmlAsync($"/{owner.UserName}/fresh");

		Assert.Contains(En("repo_empty_title"), html);
		Assert.Contains($"/git/{owner.UserName}/fresh.git", html);
	}

	[Fact]
	public async Task TheBreadcrumbs_UseTheStoredNames_EvenWhenTheUrlUsesAnotherCasing()
	{
		var owner = await _f.CreateUserAsync(Unique("Owner"));
		await _f.SeedHistoryAsync(owner, "MixedCase");

		var html = await Anonymous().GetHtmlAsync($"/{owner.UserName!.ToUpperInvariant()}/MIXEDCASE/commits");

		Assert.Contains("<strong>MixedCase</strong>", html);
		Assert.Contains($">{owner.UserName}</a>", html);
	}

	// ---- Files -----------------------------------------------------------------------------------------

	[Fact]
	public async Task ATextFile_ShowsItsContent_IncludingFilesInFolders()
	{
		var (_, _, url) = await SeedAsync();

		var html = await Anonymous().GetHtmlAsync(url + "/blob/main/src/app.txt");

		Assert.Contains("line one", html);
		Assert.Contains("line two", html);
	}

	[Fact]
	public async Task ABinaryFile_IsNotDumpedIntoThePage()
	{
		var (_, _, url) = await SeedAsync();

		var html = await Anonymous().GetHtmlAsync(url + "/blob/main/logo.png");

		Assert.DoesNotContain("PNG", html.Replace("logo.png", ""));
		Assert.Contains("logo.png", html);
	}

	[Fact]
	public async Task Raw_ServesTextAsPlainText_AndImagesWithTheirMediaType()
	{
		var (_, _, url) = await SeedAsync();
		var session = Anonymous();

		var text = await session.GetAsync(url + "/raw/main/src/app.txt");
		var image = await session.GetAsync(url + "/raw/main/logo.png");

		Assert.Equal(HttpStatusCode.OK, text.StatusCode);
		Assert.StartsWith("text/plain", text.Content.Headers.ContentType!.MediaType);
		Assert.Equal("line one\nline two\n", (await text.Content.ReadAsStringAsync()).Replace("\r\n", "\n"));
		Assert.Equal("image/png", image.Content.Headers.ContentType!.MediaType);
		Assert.Equal("PNG", Encoding.Latin1.GetString((await image.Content.ReadAsByteArrayAsync())[..4]));
	}

	[Theory]
	[InlineData("/blob/main/does/not/exist.txt")]
	[InlineData("/raw/main/does/not/exist.txt")]
	[InlineData("/blob/no-such-branch/README.md")]
	[InlineData("/raw/no-such-branch/README.md")]
	public async Task AMissingFileOrBranch_IsNeverAServerError(string path)
	{
		var (_, _, url) = await SeedAsync();

		var response = await Anonymous().GetAsync(url + path);

		Assert.True((int)response.StatusCode < 500, $"{path} -> {(int)response.StatusCode}");
	}

	// ---- History ---------------------------------------------------------------------------------------

	[Fact]
	public async Task Commits_ListsTheHistory_NewestFirst_WithTheTotal()
	{
		var (_, _, url) = await SeedAsync(extraCommits: 2);

		var html = await Anonymous().GetHtmlAsync(url + "/commits");

		Assert.Contains("5 " + En("commits_on"), html);
		Assert.True(html.IndexOf("Log entry 02", StringComparison.Ordinal) < html.IndexOf("Add readme", StringComparison.Ordinal));
		Assert.Contains("Add logo", html);
	}

	[Fact]
	public async Task Commits_PaginateBy25()
	{
		var (_, seed, url) = await SeedAsync(extraCommits: 27);               // 30 commits on main
		var session = Anonymous();

		var first = await session.GetHtmlAsync(url + "/commits");
		var second = await session.GetHtmlAsync(url + "/commits?page=1");

		Assert.Contains("30 " + En("commits_on"), first);
		Assert.Equal(25, Regex.Matches(first, "class=\"commit-item\"").Count);
		Assert.Equal(5, Regex.Matches(second, "class=\"commit-item\"").Count);
		Assert.Contains("Add readme", second);
		Assert.DoesNotContain("Add readme", first);
		Assert.Contains(">v1</a>", second);                                     // the tag sits on the first commit
	}

	[Fact]
	public async Task Commits_CanBeShownPerBranch()
	{
		var (_, _, url) = await SeedAsync();

		var feature = await Anonymous().GetHtmlAsync(url + "/commits?branch=feature");

		Assert.Contains("Feature work", feature);
		Assert.DoesNotContain("Add logo", feature);
	}

	[Fact]
	public async Task ACommit_ShowsItsMessage_TheChangedFilesAndTheDiff()
	{
		var (_, seed, url) = await SeedAsync();

		var html = await Anonymous().GetHtmlAsync($"{url}/commit/{seed.SecondSha}");

		Assert.Contains("Add app source", html);
		Assert.Contains("1 " + En("commit_files_changed"), html);
		Assert.Contains("src/app.txt", html);
		Assert.Contains("line one", html);
	}

	[Fact]
	public async Task ATaggedCommit_ShowsItsTag()
	{
		var (_, seed, url) = await SeedAsync();

		Assert.Contains(">v1</a>", await Anonymous().GetHtmlAsync($"{url}/commit/{seed.FirstSha}"));
	}

	[Fact]
	public async Task AnUnknownCommit_IsNeverAServerError()
	{
		var (_, _, url) = await SeedAsync();

		var response = await Anonymous().GetAsync(url + "/commit/0123456789abcdef0123456789abcdef01234567");

		Assert.True((int)response.StatusCode < 500, "status " + (int)response.StatusCode);
	}

	[Fact]
	public async Task Branches_ListsEveryBranch_AndTags_ListsEveryTag()
	{
		var (_, _, url) = await SeedAsync();
		var session = Anonymous();

		var branches = await session.GetHtmlAsync(url + "/branches");
		var tags = await session.GetHtmlAsync(url + "/tags");

		Assert.Contains("main", branches);
		Assert.Contains("feature", branches);
		Assert.Contains("release/1.0", branches);
		Assert.Contains("v1", tags);
	}

	[Fact]
	public async Task BranchesAndTags_OfAnEmptyRepository_SayThereAreNone()
	{
		var owner = await _f.CreateUserAsync(Unique("owner"));
		await _f.CreateRepoAsync(owner, "fresh");
		var session = Anonymous();

		Assert.Contains(En("branches_empty"), await session.GetHtmlAsync($"/{owner.UserName}/fresh/branches"));
		Assert.Contains(En("tags_empty"), await session.GetHtmlAsync($"/{owner.UserName}/fresh/tags"));
	}

	// ---- Archives ---------------------------------------------------------------------------------------

	private static List<string> ZipEntries(byte[] zip)
	{
		using var archive = new ZipArchive(new MemoryStream(zip));
		return archive.Entries.Select(e => e.FullName).ToList();
	}

	[Fact]
	public async Task Archive_ReturnsAZipOfTheBranch()
	{
		var (_, _, url) = await SeedAsync();

		var response = await Anonymous().GetAsync(url + "/archive/main");
		var entries = ZipEntries(await response.Content.ReadAsByteArrayAsync());

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("application/zip", response.Content.Headers.ContentType!.MediaType);
		Assert.Contains("attachment; filename=\"proj-main.zip\"", response.Content.Headers.ContentDisposition!.ToString().Replace("filename=proj-main.zip", "filename=\"proj-main.zip\""));
		Assert.Contains("README.md", entries);
		Assert.Contains("src/app.txt", entries);
		Assert.DoesNotContain("src/feature.txt", entries);
	}

	[Fact]
	public async Task TheArchiveLink_OnThePage_WorksForBranchNamesContainingASlash()
	{
		var (_, _, url) = await SeedAsync();
		var session = Anonymous();
		var page = await session.GetHtmlAsync(url + "?branch=release/1.0");
		var link = Regex.Match(page, "href=\"([^\"]*/archive/[^\"]+)\"").Groups[1].Value;

		var response = await session.GetAsync(link);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Contains("src/app.txt", ZipEntries(await response.Content.ReadAsByteArrayAsync()));
	}

	// ---- Repositories whose data has gone missing --------------------------------------------------------

	[Fact]
	public async Task WhenTheFolderIsGone_ThePagesRedirectToTheDataMissingPage_WhichOnlyTheOwnerCanCleanUp()
	{
		var (owner, _, url) = await SeedAsync();
		var stranger = await _f.CreateUserAsync(Unique("stranger"));
		var folder = Path.Combine(_f.ReposPath, owner.UserName!, "proj.git");
		foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
		Directory.Delete(folder, recursive: true);

		var view = await Anonymous().GetAsync(url);
		var page = await Anonymous().GetHtmlAsync(url + "/data-missing");
		var strangerPage = await (await AsAsync(stranger)).GetHtmlAsync(url + "/data-missing");
		var ownerPage = await (await AsAsync(owner)).GetHtmlAsync(url + "/data-missing");

		Assert.Equal(HttpStatusCode.Redirect, view.StatusCode);
		Assert.EndsWith("/data-missing", view.Headers.Location!.OriginalString);
		Assert.Contains(En("repo_data_missing_title"), page);
		Assert.DoesNotContain(En("repo_data_missing_delete"), strangerPage);
		Assert.Contains(En("repo_data_missing_delete"), ownerPage);
	}

	[Fact]
	public async Task TheOwner_CanRemoveTheRecordOfARepoWhoseDataIsGone_ButAStrangerCannot()
	{
		var owner = await _f.CreateUserAsync(Unique("owner"));
		var stranger = await _f.CreateUserAsync(Unique("stranger"));
		await _f.CreateRepoAsync(owner, "broken");
		Directory.Delete(Path.Combine(_f.ReposPath, owner.UserName!, "broken.git"), recursive: true);
		var page = $"/{owner.UserName}/broken/data-missing";

		var refused = await (await AsAsync(stranger)).PostFormAsync("/User/Settings", page + "?handler=Delete");
		Assert.True(IsLoginRedirect(refused));
		Assert.Equal(1, await _f.UseServicesAsync(sp => sp.GetRequiredService<AppDbContext>().Repositories.CountAsync(r => r.OwnerId == owner.Id)));

		var done = await (await AsAsync(owner)).PostFormAsync(page, page + "?handler=Delete");
		Assert.Equal(HttpStatusCode.Redirect, done.StatusCode);
		Assert.Equal(0, await _f.UseServicesAsync(sp => sp.GetRequiredService<AppDbContext>().Repositories.CountAsync(r => r.OwnerId == owner.Id)));
	}

	[Fact]
	public async Task TheGitEndpoint_Answers404_ForARepoWhoseDataIsGone()
	{
		var owner = await _f.CreateUserAsync(Unique("owner"));
		await _f.CreateRepoAsync(owner, "broken");
		Directory.Delete(Path.Combine(_f.ReposPath, owner.UserName!, "broken.git"), recursive: true);

		var response = await _f.NewClient().SendAsync(GitWire.Get($"/git/{owner.UserName}/broken.git/info/refs?service=git-upload-pack"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	// ---- Privacy across every browse page ----------------------------------------------------------------

	[Fact]
	public async Task EveryBrowsePage_OfAPrivateRepo_IsHiddenFromStrangersAndAnonymous_AndOpenToTheOwner()
	{
		var (owner, seed, url) = await SeedAsync(isPrivate: true);
		var stranger = await _f.CreateUserAsync(Unique("stranger"));
		var paths = new[]
		{
			url, url + "?branch=main&path=src", url + "/blob/main/src/app.txt", url + "/raw/main/src/app.txt",
			url + "/commits", $"{url}/commit/{seed.FirstSha}", url + "/branches", url + "/tags", url + "/archive/main",
		};
		var ownerSession = await AsAsync(owner);
		var strangerSession = await AsAsync(stranger);

		foreach (var path in paths)
		{
			Assert.Equal(HttpStatusCode.OK, (await ownerSession.GetAsync(path)).StatusCode);
			Assert.True(IsLoginRedirect(await strangerSession.GetAsync(path)), "stranger: " + path);
			Assert.True(IsLoginRedirect(await Anonymous().GetAsync(path)), "anonymous: " + path);
		}
	}

	[Fact]
	public async Task EveryBrowsePage_OfAPublicRepo_IsOpenToAnonymousVisitors()
	{
		var (_, seed, url) = await SeedAsync();
		var paths = new[] { url, url + "/blob/main/README.md", url + "/raw/main/README.md", url + "/commits", $"{url}/commit/{seed.FirstSha}", url + "/branches", url + "/tags", url + "/archive/main" };

		foreach (var path in paths)
			Assert.Equal(HttpStatusCode.OK, (await Anonymous().GetAsync(path)).StatusCode);
	}

	[Fact]
	public async Task GroupMembers_CanBrowseAPrivateGroupRepo_StrangersCannot()
	{
		var owner = await _f.CreateUserAsync(Unique("owner"));
		var member = await _f.CreateUserAsync(Unique("member"));
		var stranger = await _f.CreateUserAsync(Unique("stranger"));
		var group = await _f.CreateGroupAsync(Unique("Team"), owner, member);
		await _f.SeedHistoryAsync(owner, "proj", isPrivate: true, group: group);
		var url = $"/{group.Name}/proj";

		Assert.Equal(HttpStatusCode.OK, (await (await AsAsync(member)).GetAsync(url + "/commits")).StatusCode);
		Assert.True(IsLoginRedirect(await (await AsAsync(stranger)).GetAsync(url + "/commits")));
		var html = await (await AsAsync(member)).GetHtmlAsync(url);
		Assert.Contains($"href=\"/Group/{group.Name}\"", html);
		Assert.Contains($"/git/{group.Name}/proj.git", html);
	}
}
