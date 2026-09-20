using System.Net;
using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using GitServer.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GitServer.Tests.TestSupport.GitWire;

namespace GitServer.Tests;

/// <summary>
/// Real git, real HTTP pipeline. A local repository makes a commit and produces the same packfile a
/// `git push` would send; the app receives it through its middleware + GitController and the real
/// `git receive-pack`, and the result is read back with `git upload-pack` (a clone) and inspected.
/// </summary>
public class GitSmartHttpEndToEndTests : IClassFixture<GitServerFactory>, IDisposable
{
	private const string UploadPackType = "application/x-git-upload-pack-request";
	private const string ReceivePackType = "application/x-git-receive-pack-request";

	private readonly GitServerFactory _f;
	private readonly HttpClient _client;
	private readonly List<IDisposable> _cleanup = new();

	public GitSmartHttpEndToEndTests(GitServerFactory factory)
	{
		_f = factory;
		_client = factory.NewClient();
	}

	public void Dispose() => _cleanup.ForEach(c => c.Dispose());

	private static string Unique(string stem) => stem + Guid.NewGuid().ToString("N")[..6];

	private LocalGit NewLocal()
	{
		var local = new LocalGit();
		_cleanup.Add(local);
		return local;
	}

	private string ServerRepoDir(string owner, string repo) => Path.Combine(_f.ReposPath, owner, repo + ".git");

	private async Task<HttpResponseMessage> PushAsync(string owner, string repo, LocalGit local, string sha, string? auth, string oldSha = ZeroSha)
	{
		var body = ReceivePackRequest(oldSha, sha, "refs/heads/main", local.PackFor(sha));
		return await _client.SendAsync(Post($"/git/{owner}/{repo}.git/git-receive-pack", ReceivePackType, body, auth));
	}

	private async Task<HttpResponseMessage> UploadPackAsync(string owner, string repo, string sha, string? auth) =>
		await _client.SendAsync(Post($"/git/{owner}/{repo}.git/git-upload-pack", UploadPackType, UploadPackWantRequest(sha), auth));

	// ---- Discovery ----------------------------------------------------------------------------

	[Fact]
	public async Task Discovery_ForUploadPack_OnAPublicRepo_NeedsNoCredentials()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		await _f.CreateRepoAsync(alice, "pub");

		var response = await _client.SendAsync(Get($"/git/{alice.UserName}/pub.git/info/refs?service=git-upload-pack"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("application/x-git-upload-pack-advertisement", response.Content.Headers.ContentType?.MediaType);
		Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
		Assert.StartsWith("001e# service=git-upload-pack\n0000", AsText(await response.Content.ReadAsByteArrayAsync()));
	}

	[Fact]
	public async Task Discovery_ForReceivePack_RequiresCredentials()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		await _f.CreateRepoAsync(alice, "pub");
		var url = $"/git/{alice.UserName}/pub.git/info/refs?service=git-receive-pack";

		var anonymous = await _client.SendAsync(Get(url));
		var owner = await _client.SendAsync(Get(url, Basic(alice.UserName!, GitServerFactory.Password)));

		Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
		Assert.Contains("Basic realm=\"GitServer\"", anonymous.Headers.WwwAuthenticate.ToString());
		Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
		Assert.Equal("application/x-git-receive-pack-advertisement", owner.Content.Headers.ContentType?.MediaType);
	}

	[Fact]
	public async Task FetchingAMissingRepository_Is404()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));

		var response = await _client.SendAsync(Get($"/git/{alice.UserName}/ghost.git/info/refs?service=git-upload-pack"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task TheBareGitUrl_RedirectsToTheWebPage()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		await _f.CreateRepoAsync(alice, "pub");

		var response = await _client.SendAsync(Get($"/git/{alice.UserName}/pub.git"));

		Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
		Assert.Equal($"/{alice.UserName}/pub", response.Headers.Location?.OriginalString);
	}

	// ---- Push, then clone, through the real git backend ----------------------------------------------

	[Fact]
	public async Task Push_CreatesTheRepository_And_ACloneGetsTheCommitBack()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var auth = Basic(alice.UserName!, GitServerFactory.Password);
		var local = NewLocal();
		var sha = local.Commit("hello.txt", "hello from the client\n", "first commit");

		var push = await PushAsync(alice.UserName!, "roundtrip", local, sha, auth);
		var pushText = AsText(await push.Content.ReadAsByteArrayAsync());

		Assert.Equal(HttpStatusCode.OK, push.StatusCode);
		Assert.Equal("application/x-git-receive-pack-result", push.Content.Headers.ContentType?.MediaType);
		Assert.Contains("unpack ok", pushText);
		Assert.Contains("ok refs/heads/main", pushText);

		// The repository now exists in the database (private by default) and on disk with the commit.
		var repo = await _f.UseServicesAsync(sp => sp.GetRequiredService<RepositoryService>().GetAsync(alice.UserName!, "roundtrip"));
		Assert.NotNull(repo);
		Assert.True(repo!.IsPrivate);
		Assert.Equal(sha, LocalGit.ServerRef(ServerRepoDir(alice.UserName!, "roundtrip"), "refs/heads/main"));

		// A clone (upload-pack) returns a pack containing that commit and its file.
		var clone = await UploadPackAsync(alice.UserName!, "roundtrip", sha, auth);
		var cloneBytes = await clone.Content.ReadAsByteArrayAsync();
		Assert.Equal(HttpStatusCode.OK, clone.StatusCode);
		Assert.Equal("application/x-git-upload-pack-result", clone.Content.Headers.ContentType?.MediaType);
		Assert.StartsWith("0008NAK\nPACK", AsText(cloneBytes));

		var fresh = NewLocal();
		fresh.IndexPack(cloneBytes[8..]);
		Assert.True(fresh.HasCommit(sha));
		Assert.Equal("hello from the client", fresh.Run("show", $"{sha}:hello.txt"));
	}

	[Fact]
	public async Task APublicRepo_CanBeClonedWithoutCredentials_APrivateOneCannot()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var auth = Basic(alice.UserName!, GitServerFactory.Password);
		var local = NewLocal();
		var sha = local.Commit("a.txt", "a", "c1");
		await _f.CreateRepoAsync(alice, "open", isPrivate: false);
		await _f.CreateRepoAsync(alice, "closed", isPrivate: true);
		await PushAsync(alice.UserName!, "open", local, sha, auth);
		await PushAsync(alice.UserName!, "closed", local, sha, auth);

		var open = await UploadPackAsync(alice.UserName!, "open", sha, auth: null);
		var closed = await UploadPackAsync(alice.UserName!, "closed", sha, auth: null);

		Assert.Equal(HttpStatusCode.OK, open.StatusCode);
		Assert.StartsWith("0008NAK\nPACK", AsText(await open.Content.ReadAsByteArrayAsync()));
		Assert.Equal(HttpStatusCode.Unauthorized, closed.StatusCode);
	}

	[Fact]
	public async Task WrongPassword_And_AStranger_AreRefused_TheOwnerIsNot()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var mallory = await _f.CreateUserAsync(Unique("mallory"));
		await _f.CreateRepoAsync(alice, "secret", isPrivate: true);
		var url = $"/git/{alice.UserName}/secret.git/info/refs?service=git-upload-pack";

		var wrongPassword = await _client.SendAsync(Get(url, Basic(alice.UserName!, "nope-nope")));
		var stranger = await _client.SendAsync(Get(url, Basic(mallory.UserName!, GitServerFactory.Password)));
		var owner = await _client.SendAsync(Get(url, Basic(alice.UserName!, GitServerFactory.Password)));

		Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
		Assert.Equal(HttpStatusCode.Forbidden, stranger.StatusCode);
		Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
	}

	[Fact]
	public async Task ACollaborator_CanPushOnlyWithWriteAccess()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var reader = await _f.CreateUserAsync(Unique("reader"));
		var writer = await _f.CreateUserAsync(Unique("writer"));
		var repo = await _f.CreateRepoAsync(alice, "team", isPrivate: true);
		await _f.UseServicesAsync(async sp =>
		{
			var db = sp.GetRequiredService<AppDbContext>();
			db.RepositoryAccesses.Add(new RepositoryAccess { RepositoryId = repo.Id, UserId = reader.Id, Level = AccessLevel.Read });
			db.RepositoryAccesses.Add(new RepositoryAccess { RepositoryId = repo.Id, UserId = writer.Id, Level = AccessLevel.Write });
			await db.SaveChangesAsync();
		});
		var local = NewLocal();
		var sha = local.Commit("f.txt", "x", "c");

		var byReader = await PushAsync(alice.UserName!, "team", local, sha, Basic(reader.UserName!, GitServerFactory.Password));
		Assert.Equal(HttpStatusCode.Forbidden, byReader.StatusCode);
		Assert.Null(LocalGit.ServerRef(ServerRepoDir(alice.UserName!, "team"), "refs/heads/main"));

		var byWriter = await PushAsync(alice.UserName!, "team", local, sha, Basic(writer.UserName!, GitServerFactory.Password));
		Assert.Equal(HttpStatusCode.OK, byWriter.StatusCode);
		Assert.Equal(sha, LocalGit.ServerRef(ServerRepoDir(alice.UserName!, "team"), "refs/heads/main"));
	}

	// ---- Read-only repositories ----------------------------------------------------------------

	[Fact]
	public async Task ReadOnlyRepo_RefusesEveryPush_AndTheServerRepoStaysUntouched()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var auth = Basic(alice.UserName!, GitServerFactory.Password);
		var local = NewLocal();
		var first = local.Commit("a.txt", "one", "first");
		var repo = await _f.CreateRepoAsync(alice, "frozen");

		Assert.Equal(HttpStatusCode.OK, (await PushAsync(alice.UserName!, "frozen", local, first, auth)).StatusCode);

		await _f.UseServicesAsync(async sp =>
		{
			var db = sp.GetRequiredService<AppDbContext>();
			(await db.Repositories.SingleAsync(r => r.Id == repo.Id)).IsReadOnly = true;
			await db.SaveChangesAsync();
		});
		var second = local.Commit("a.txt", "two", "second");

		var refused = await PushAsync(alice.UserName!, "frozen", local, second, auth, oldSha: first);
		var discovery = await _client.SendAsync(Get($"/git/{alice.UserName}/frozen.git/info/refs?service=git-receive-pack", auth));
		var clone = await UploadPackAsync(alice.UserName!, "frozen", first, auth);

		Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
		Assert.Equal(HttpStatusCode.Forbidden, discovery.StatusCode);
		Assert.Equal(first, LocalGit.ServerRef(ServerRepoDir(alice.UserName!, "frozen"), "refs/heads/main"));
		Assert.Equal(HttpStatusCode.OK, clone.StatusCode);   // reading is still fine
	}

	// ---- Case-insensitive URLs ----------------------------------------------------------------------

	[Fact]
	public async Task AnyCasingInTheUrl_ReachesTheSameRepo_WithoutCreatingADuplicate()
	{
		var owner = await _f.CreateUserAsync(Unique("Alice"));
		var auth = Basic(owner.UserName!, GitServerFactory.Password);
		await _f.CreateRepoAsync(owner, "MyRepo");
		var local = NewLocal();
		var sha = local.Commit("a.txt", "a", "c");

		var push = await PushAsync(owner.UserName!.ToUpperInvariant(), "MYREPO", local, sha, auth);
		var clone = await UploadPackAsync(owner.UserName!.ToLowerInvariant(), "myrepo", sha, auth);

		Assert.Equal(HttpStatusCode.OK, push.StatusCode);
		Assert.Equal(HttpStatusCode.OK, clone.StatusCode);
		Assert.Equal(sha, LocalGit.ServerRef(ServerRepoDir(owner.UserName!, "MyRepo"), "refs/heads/main"));

		var (repoRows, ownerDirs) = await _f.UseServicesAsync(async sp =>
		{
			var db = sp.GetRequiredService<AppDbContext>();
			return (await db.Repositories.CountAsync(r => r.OwnerId == owner.Id),
				Directory.GetDirectories(Path.Combine(_f.ReposPath, owner.UserName!)).Select(Path.GetFileName).ToArray());
		});
		Assert.Equal(1, repoRows);
		Assert.Equal(new[] { "MyRepo.git" }, ownerDirs);
	}

	[Fact]
	public async Task TheCasingOfTheFirstPush_BecomesTheRepositoryName()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var local = NewLocal();
		var sha = local.Commit("a.txt", "a", "c");

		await PushAsync(alice.UserName!, "FreshNameWithCaps", local, sha, Basic(alice.UserName!, GitServerFactory.Password));

		var repo = await _f.UseServicesAsync(sp => sp.GetRequiredService<RepositoryService>().GetAsync(alice.UserName!, "freshnamewithcaps"));
		Assert.Equal("FreshNameWithCaps", repo!.Name);
		Assert.True(Directory.Exists(ServerRepoDir(alice.UserName!, "FreshNameWithCaps")));
	}

	// ---- Groups --------------------------------------------------------------------------------

	[Fact]
	public async Task AGroupMember_CanPushANewRepoIntoTheGroupNamespace_AStrangerCannot()
	{
		var owner = await _f.CreateUserAsync(Unique("owner"));
		var member = await _f.CreateUserAsync(Unique("member"));
		var stranger = await _f.CreateUserAsync(Unique("stranger"));
		var groupName = Unique("Team");
		var group = await _f.CreateGroupAsync(groupName, owner, member);
		var local = NewLocal();
		var sha = local.Commit("a.txt", "a", "c");

		var byStranger = await PushAsync(groupName.ToLowerInvariant(), "api", local, sha, Basic(stranger.UserName!, GitServerFactory.Password));
		var byMember = await PushAsync(groupName.ToLowerInvariant(), "api", local, sha, Basic(member.UserName!, GitServerFactory.Password));

		Assert.Equal(HttpStatusCode.Forbidden, byStranger.StatusCode);
		Assert.Equal(HttpStatusCode.OK, byMember.StatusCode);

		var repo = await _f.UseServicesAsync(sp => sp.GetRequiredService<RepositoryService>().GetAsync(groupName.ToUpperInvariant(), "API"));
		Assert.Equal(group.Id, repo!.GroupOwnerId);
		Assert.Null(repo.OwnerId);
		Assert.Equal(groupName, repo.OwnerName);                                      // canonical casing
		Assert.Equal(sha, LocalGit.ServerRef(ServerRepoDir(groupName, "api"), "refs/heads/main"));
	}

	[Fact]
	public async Task AGroupRepo_IsReadableByMembers_AndHiddenFromStrangers_WhenPrivate()
	{
		var owner = await _f.CreateUserAsync(Unique("owner"));
		var member = await _f.CreateUserAsync(Unique("member"));
		var stranger = await _f.CreateUserAsync(Unique("stranger"));
		var groupName = Unique("Team");
		var group = await _f.CreateGroupAsync(groupName, owner, member);
		await _f.CreateGroupRepoAsync(group, "secret", isPrivate: true);
		var url = $"/git/{groupName}/secret.git/info/refs?service=git-upload-pack";

		Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(Get(url, Basic(member.UserName!, GitServerFactory.Password)))).StatusCode);
		Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(Get(url, Basic(stranger.UserName!, GitServerFactory.Password)))).StatusCode);
		Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(Get(url))).StatusCode);
	}

	// ---- Admin switches ------------------------------------------------------------------------

	[Fact]
	public async Task AutoCreateOnPush_CanBeSwitchedOff_InTheSiteSettings()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var local = NewLocal();
		var sha = local.Commit("a.txt", "a", "c");
		await SetSiteSettingAsync(s => s.AllowPushToCreateRepositories = false);
		try
		{
			var response = await PushAsync(alice.UserName!, "nope", local, sha, Basic(alice.UserName!, GitServerFactory.Password));

			Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
			Assert.False(Directory.Exists(ServerRepoDir(alice.UserName!, "nope")));
		}
		finally
		{
			await SetSiteSettingAsync(s => s.AllowPushToCreateRepositories = true);
		}
	}

	private Task SetSiteSettingAsync(Action<SiteSettings> change) =>
		_f.UseServicesAsync(async sp =>
		{
			var settings = sp.GetRequiredService<SiteSettingsService>();
			var current = await settings.GetAsync();
			change(current);
			await settings.SaveAsync(current);
		});
}
