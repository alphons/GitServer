using System.Net;
using System.Text;
using System.Text.Json;
using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using GitServer.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GitServer.Tests;

/// <summary>Pull requests: opened from a branch of the repository or of a fork, compared, merged on the server (merge commit
/// or squash) without a work tree, refused when they conflict, and kept readable after their fork is deleted.</summary>
public class PullRequestsEndToEndTests : IClassFixture<GitServerFactory>
{
	private readonly GitServerFactory _f;

	public PullRequestsEndToEndTests(GitServerFactory factory) => _f = factory;

	private static string Unique(string stem) => stem + Guid.NewGuid().ToString("N")[..6];
	private static string En(string key) =>
		JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(TestPaths.LocalizationRoot, "en", "strings.json")))![key];
	private Task<T> Db<T>(Func<AppDbContext, Task<T>> action) => _f.UseServicesAsync(sp => action(sp.GetRequiredService<AppDbContext>()));
	private async Task<WebSession> AsAsync(AppUser user) => await new WebSession(_f).LoginAsync(user.UserName!);
	private string Folder(string owner, string repo) => Path.Combine(_f.ReposPath, owner, repo + ".git");
	private static string Api(string owner, string repo) => $"/api/repos/{owner}/{repo}/pulls";
	private static string Git(string folder, params string[] args) => Encoding.UTF8.GetString(LocalGit.Exec(folder, null, args).Out).Trim();

	/// <summary>main: a.txt. feature (from main): adds b.txt. clash (from main): changes a.txt, while main changes it differently too.</summary>
	private async Task<(string MainSha, string FeatureSha)> SeedAsync(AppUser owner, string repo)
	{
		await _f.CreateRepoAsync(owner, repo);
		using var local = new LocalGit();
		local.Commit("a.txt", "one\n", "Initial");
		local.Run("checkout", "-q", "-b", "feature");
		var feature = local.Commit("b.txt", "new file\n", "Add b");
		local.Run("checkout", "-q", "-b", "clash", "main");
		local.Commit("a.txt", "clash side\n", "Change a on clash");
		local.Run("checkout", "-q", "main");
		var main = local.Commit("a.txt", "main side\n", "Change a on main");
		local.PushTo(Folder(owner.UserName!, repo));
		return (main, feature);
	}

	private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
		JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

	private static async Task<int> OpenAsync(WebSession session, string owner, string repo, string head, string? headRepo = null, string title = "My change")
	{
		var response = await session.SendJsonAsync($"/{owner}/{repo}/pulls", HttpMethod.Post, Api(owner, repo),
			new { title, head, @base = "main", headRepo, body = "Please merge." });
		Assert.Equal(HttpStatusCode.Created, response.StatusCode);
		return (await JsonAsync(response)).GetProperty("number").GetInt32();
	}

	[Fact]
	public async Task APullRequestFromABranch_IsOpenedOnThePage_ShowsItsChanges_AndMergesWithAMergeCommit()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var (mainSha, featureSha) = await SeedAsync(alice, "proj");
		var repoId = await Db(d => d.Repositories.Where(r => r.OwnerId == alice.Id && r.Name == "proj").Select(r => r.Id).SingleAsync());
		var session = await AsAsync(alice);
		var newPage = $"/{alice.UserName}/proj/pulls/new?Source={repoId}&Head=feature&Base=main";

		var preview = await session.GetHtmlAsync(newPage);
		var open = await session.PostFormAsync(newPage, newPage, ("Title", "Add b"), ("Body", "Adds **b**."));

		Assert.Contains(En("pr_mergeable"), preview);
		Assert.Equal(HttpStatusCode.Redirect, open.StatusCode);
		var url = WebSession.Location(open)!;
		var id = int.Parse(url.Split('/')[^1]);
		Assert.Equal(featureSha, Git(Folder(alice.UserName!, "proj"), "rev-parse", $"refs/pull/{id}/head"));
		Assert.Contains("b.txt", await session.GetHtmlAsync(url + "?tab=files"));
		Assert.Contains("Add b", await session.GetHtmlAsync(url + "?tab=commits"));
		Assert.Equal("Adds **b**.", await session.GetHtmlAsync(url + "/raw"));

		var merge = await session.PostFormAsync(url, url + "?handler=Merge", ("squash", "false"));

		Assert.Equal(HttpStatusCode.Redirect, merge.StatusCode);
		var pr = await Db(d => d.PullRequests.SingleAsync(p => p.Id == id));
		Assert.Equal(PullRequestState.Merged, pr.State);
		Assert.Equal(alice.Id, pr.MergedById);
		var folder = Folder(alice.UserName!, "proj");
		Assert.Equal(pr.MergeCommitSha, Git(folder, "rev-parse", "refs/heads/main"));
		Assert.Equal($"{mainSha} {featureSha}", Git(folder, "log", "-1", "--format=%P", "main"));   // a real merge commit
		Assert.Equal("new file", Git(folder, "show", "main:b.txt"));
		Assert.Contains(En("pr_merged"), await session.GetHtmlAsync(url));
		Assert.True(await Db(d => d.AuditEntries.AnyAsync(a => a.Action == "pr.merge" && a.ActorUserId == alice.Id)));
	}

	[Fact]
	public async Task Squashing_AddsOneCommitWithOneParent()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var (mainSha, _) = await SeedAsync(alice, "squashy");
		var session = await AsAsync(alice);
		var id = await OpenAsync(session, alice.UserName!, "squashy", "feature", title: "Add b file");

		var merge = await session.SendJsonAsync($"/{alice.UserName}/squashy/pulls", HttpMethod.Post, $"{Api(alice.UserName!, "squashy")}/{id}/merge", new { method = "squash" });

		Assert.Equal(HttpStatusCode.OK, merge.StatusCode);
		Assert.Equal("merged", (await JsonAsync(merge)).GetProperty("state").GetString());
		var folder = Folder(alice.UserName!, "squashy");
		Assert.Equal(mainSha, Git(folder, "log", "-1", "--format=%P", "main"));
		Assert.Equal($"Add b file (#{id})", Git(folder, "log", "-1", "--format=%s", "main"));
		Assert.Equal("new file", Git(folder, "show", "main:b.txt"));
	}

	[Fact]
	public async Task AConflictingPullRequest_IsShownAsSuch_AndIsNotMerged()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var (mainSha, _) = await SeedAsync(alice, "clashing");
		var session = await AsAsync(alice);
		var id = await OpenAsync(session, alice.UserName!, "clashing", "clash");

		var detail = await session.GetAsync($"{Api(alice.UserName!, "clashing")}/{id}");
		var merge = await session.SendJsonAsync($"/{alice.UserName}/clashing/pulls", HttpMethod.Post, $"{Api(alice.UserName!, "clashing")}/{id}/merge");

		Assert.False((await JsonAsync(detail)).GetProperty("mergeable").GetBoolean());
		Assert.Equal(HttpStatusCode.Conflict, merge.StatusCode);
		Assert.Equal(mainSha, Git(Folder(alice.UserName!, "clashing"), "rev-parse", "refs/heads/main"));
		Assert.Contains(En("pr_conflicts"), await session.GetHtmlAsync($"/{alice.UserName}/clashing/pulls/{id}"));
	}

	[Fact]
	public async Task APullRequestFromAFork_IsMergedByTheOwner_AndSurvivesTheForksDeletion()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var bob = await _f.CreateUserAsync(Unique("bob"));
		await SeedAsync(alice, "upstream");
		var bobSession = await AsAsync(bob);
		var fork = await bobSession.SendJsonAsync($"/{alice.UserName}/upstream", HttpMethod.Post, $"/api/repos/{alice.UserName}/upstream/fork", new { });
		Assert.Equal(HttpStatusCode.Created, fork.StatusCode);

		// Bob works in his fork: a new branch with a new file.
		using (var local = new LocalGit())
		{
			local.Run("fetch", "-q", Folder(bob.UserName!, "upstream"), "main:main-from-fork");
			local.Run("checkout", "-q", "-b", "docs", "main-from-fork");
			local.Commit("docs.md", "# Docs\n", "Add docs");
			local.Run("push", "-q", Folder(bob.UserName!, "upstream"), "docs");
		}
		var id = await OpenAsync(bobSession, alice.UserName!, "upstream", "docs", headRepo: $"{bob.UserName}/upstream", title: "Docs");

		var aliceSession = await AsAsync(alice);
		var merge = await aliceSession.SendJsonAsync($"/{alice.UserName}/upstream/pulls", HttpMethod.Post, $"{Api(alice.UserName!, "upstream")}/{id}/merge");
		Assert.Equal(HttpStatusCode.OK, merge.StatusCode);
		Assert.Equal("# Docs", Git(Folder(alice.UserName!, "upstream"), "show", "main:docs.md"));

		var delete = await bobSession.PostFormAsync($"/{bob.UserName}/upstream/settings", $"/{bob.UserName}/upstream/settings?handler=Delete");
		Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);

		var pr = await Db(d => d.PullRequests.SingleAsync(p => p.Id == id));
		Assert.Null(pr.SourceRepositoryId);
		Assert.Equal($"{bob.UserName}/upstream", pr.SourceDisplayName);
		var page = await aliceSession.GetHtmlAsync($"/{alice.UserName}/upstream/pulls/{id}?tab=commits");
		Assert.Contains("Add docs", page);
	}

	[Fact]
	public async Task NewCommitsOnTheSourceBranch_ShowUpInTheOpenPullRequest()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		await SeedAsync(alice, "growing");
		var session = await AsAsync(alice);
		var id = await OpenAsync(session, alice.UserName!, "growing", "feature");

		using (var local = new LocalGit())
		{
			local.Run("fetch", "-q", Folder(alice.UserName!, "growing"), "feature:feature");
			local.Run("checkout", "-q", "feature");
			local.Commit("c.txt", "later\n", "Add c later");
			local.Run("push", "-q", Folder(alice.UserName!, "growing"), "feature");
		}

		var detail = await JsonAsync(await session.GetAsync($"{Api(alice.UserName!, "growing")}/{id}"));
		Assert.Contains(detail.GetProperty("files").EnumerateArray(), f => f.GetString() == "c.txt");
	}

	[Fact]
	public async Task OnlyWritersMerge_OnlyTheAuthorAndWritersClose()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var bob = await _f.CreateUserAsync(Unique("bob"));
		var carol = await _f.CreateUserAsync(Unique("carol"));
		await SeedAsync(alice, "guarded");
		var bobSession = await AsAsync(bob);
		var carolSession = await AsAsync(carol);
		var id = await OpenAsync(bobSession, alice.UserName!, "guarded", "feature");
		var pulls = $"/{alice.UserName}/guarded/pulls";

		var bobMerge = await bobSession.SendJsonAsync(pulls, HttpMethod.Post, $"{Api(alice.UserName!, "guarded")}/{id}/merge");
		var carolClose = await carolSession.SendJsonAsync(pulls, HttpMethod.Post, $"{Api(alice.UserName!, "guarded")}/{id}/close");
		var bobClose = await bobSession.SendJsonAsync(pulls, HttpMethod.Post, $"{Api(alice.UserName!, "guarded")}/{id}/close");
		var bobReopen = await bobSession.SendJsonAsync(pulls, HttpMethod.Post, $"{Api(alice.UserName!, "guarded")}/{id}/reopen");

		Assert.Equal(HttpStatusCode.Forbidden, bobMerge.StatusCode);
		Assert.Equal(HttpStatusCode.Forbidden, carolClose.StatusCode);
		Assert.Equal(HttpStatusCode.OK, bobClose.StatusCode);
		Assert.Equal(HttpStatusCode.OK, bobReopen.StatusCode);
		Assert.Equal(PullRequestState.Open, await Db(d => d.PullRequests.Where(p => p.Id == id).Select(p => p.State).SingleAsync()));
	}

	[Fact]
	public async Task AReadOnlyRepository_CannotBeMergedInto()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		await SeedAsync(alice, "frozen");
		var session = await AsAsync(alice);
		var id = await OpenAsync(session, alice.UserName!, "frozen", "feature");
		await Db(async d => { (await d.Repositories.SingleAsync(r => r.OwnerId == alice.Id && r.Name == "frozen")).IsReadOnly = true; return await d.SaveChangesAsync(); });

		var merge = await session.SendJsonAsync($"/{alice.UserName}/frozen/pulls", HttpMethod.Post, $"{Api(alice.UserName!, "frozen")}/{id}/merge");

		Assert.Equal(HttpStatusCode.Forbidden, merge.StatusCode);
	}

	[Theory]
	[InlineData("main", null, "pr_error_same_branch")]
	[InlineData("no-such-branch", null, "pr_error_branch")]
	[InlineData("feature", "other", "pr_error_source")]
	public async Task InvalidPullRequests_AreRefused(string head, string? headRepoSuffix, string errorKey)
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		await SeedAsync(alice, "strict");
		string? headRepo = null;
		if (headRepoSuffix != null)
		{
			await SeedAsync(alice, "unrelated");   // a repository that is not a fork of "strict"
			headRepo = $"{alice.UserName}/unrelated";
		}
		var session = await AsAsync(alice);

		var response = await session.SendJsonAsync($"/{alice.UserName}/strict/pulls", HttpMethod.Post, Api(alice.UserName!, "strict"),
			new { title = "x", head, @base = "main", headRepo });

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		Assert.Equal(En(errorKey), (await JsonAsync(response)).GetProperty("error").GetString());
	}

	[Fact]
	public async Task TheListShowsOpenAndClosedPullRequestsSeparately()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		await SeedAsync(alice, "listed");
		var session = await AsAsync(alice);
		await OpenAsync(session, alice.UserName!, "listed", "feature", title: "Still open");
		var closed = await OpenAsync(session, alice.UserName!, "listed", "clash", title: "Given up");
		await session.SendJsonAsync($"/{alice.UserName}/listed/pulls", HttpMethod.Post, $"{Api(alice.UserName!, "listed")}/{closed}/close");

		var openPage = await session.GetHtmlAsync($"/{alice.UserName}/listed/pulls");
		var closedPage = await session.GetHtmlAsync($"/{alice.UserName}/listed/pulls?closed=1");

		Assert.Contains("Still open", openPage);
		Assert.DoesNotContain("Given up", openPage);
		Assert.Contains("Given up", closedPage);
		Assert.DoesNotContain("Still open", closedPage);
	}
}
