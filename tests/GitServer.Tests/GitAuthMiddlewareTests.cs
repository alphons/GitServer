using GitServer.Models;
using GitServer.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Xunit;
using static GitServer.Tests.TestSupport.MiddlewareHarness;

namespace GitServer.Tests;

/// <summary>The git smart-HTTP gatekeeper end to end (minus the HTTP socket): URL parsing, owner and
/// repository resolution, HTTP Basic authentication, the access decision, and creating repositories
/// by pushing to them.</summary>
public class GitAuthMiddlewareTests : IDisposable
{
	private readonly MiddlewareHarness _h = new();

	public void Dispose() => _h.Dispose();

	private static string? Challenge(HttpContext c) => c.Response.Headers.WWWAuthenticate.ToString();

	// ---- Which requests it handles ------------------------------------------------------------

	[Theory]
	[InlineData("/")]
	[InlineData("/dashboard/explore")]
	[InlineData("/dashboard/User/alice")]
	[InlineData("/alice/repo")]                       // the web page, not the git URL
	[InlineData("/git/alice/repo.git")]               // bare URL is redirected by the controller
	[InlineData("/alice/repo.git/info/refs")]         // missing the /git prefix
	public async Task NonGitRequests_PassThroughUntouched(string path)
	{
		var context = await _h.SendAsync("GET", path);

		Assert.True(_h.NextWasCalled);
		Assert.Equal(200, context.Response.StatusCode);
		Assert.False(context.Items.ContainsKey("GitRepo"));
	}

	[Fact]
	public async Task WithoutAPathPrefix_GitUrlsLiveAtTheRoot()
	{
		_h.World.Options.GitPathPrefix = "";
		var alice = await _h.AddUserAsync("alice");
		_h.World.AddRepo(alice, "repo");

		var context = await _h.SendAsync("GET", "/alice/repo.git/info/refs", "?service=git-upload-pack");

		Assert.True(_h.NextWasCalled);
		Assert.Equal("repo", context.Items["GitRepoName"]);
	}

	// ---- Resolving owner and repository, with any casing -----------------------------------------

	[Fact]
	public async Task UnknownOwner_Is404()
	{
		var context = await _h.CloneAsync("nobody", "repo");

		Assert.False(_h.NextWasCalled);
		Assert.Equal(404, context.Response.StatusCode);
	}

	[Fact]
	public async Task UnknownRepository_OnAFetch_Is404()
	{
		await _h.AddUserAsync("alice");

		Assert.Equal(404, (await _h.CloneAsync("alice", "missing")).Response.StatusCode);
	}

	[Fact]
	public async Task PublicRepo_CanBeClonedAnonymously_AndTheRequestCarriesCanonicalNames()
	{
		var alice = await _h.AddUserAsync("alice");
		var repo = _h.World.AddRepo(alice, "repo");

		var context = await _h.CloneAsync("alice", "repo");

		Assert.True(_h.NextWasCalled);
		Assert.Equal(200, context.Response.StatusCode);
		Assert.Equal(repo.Id, ((Repository)context.Items["GitRepo"]!).Id);
		Assert.Equal("alice", context.Items["GitOwnerName"]);
		Assert.Equal("repo", context.Items["GitRepoName"]);
		Assert.Null(context.Items["GitUser"]);
	}

	[Theory]
	[InlineData("alice", "myrepo")]
	[InlineData("ALICE", "MYREPO")]
	[InlineData("Alice", "MyRepo")]
	public async Task AnyCasingInTheUrl_ResolvesToTheStoredCasing(string owner, string repo)
	{
		var alice = await _h.AddUserAsync("Alice");
		_h.World.AddRepo(alice, "MyRepo");

		var context = await _h.CloneAsync(owner, repo);

		Assert.True(_h.NextWasCalled);
		Assert.Equal("Alice", context.Items["GitOwnerName"]);
		Assert.Equal("MyRepo", context.Items["GitRepoName"]);
	}

	[Fact]
	public async Task GroupNamespace_ResolvesCaseInsensitively_AndHasNoUserOwner()
	{
		var alice = await _h.AddUserAsync("alice");
		var group = _h.World.AddGroup("Moneywise", alice);
		_h.World.AddGroupRepo(group, "SqlBackup");

		var context = await _h.CloneAsync("MONEYWISE", "sqlbackup");

		Assert.True(_h.NextWasCalled);
		Assert.Equal("Moneywise", context.Items["GitOwnerName"]);
		Assert.Equal("SqlBackup", context.Items["GitRepoName"]);
		Assert.Null(context.Items["GitOwner"]);
	}

	[Fact]
	public async Task ARepoNameThatExistsUnderAnotherOwner_IsNotFoundUnderThisOne()
	{
		var alice = await _h.AddUserAsync("alice");
		var bob = await _h.AddUserAsync("bob");
		_h.World.AddRepo(bob, "only-bobs");

		Assert.Equal(404, (await _h.CloneAsync("alice", "only-bobs")).Response.StatusCode);
		_ = alice;
	}

	// ---- Authentication ------------------------------------------------------------------------

	[Fact]
	public async Task PrivateRepo_WithoutCredentials_IsChallengedWithBasicAuth()
	{
		var alice = await _h.AddUserAsync("alice");
		_h.World.AddRepo(alice, "secret", isPrivate: true);

		var context = await _h.CloneAsync("alice", "secret");

		Assert.False(_h.NextWasCalled);
		Assert.Equal(401, context.Response.StatusCode);
		Assert.Equal("Basic realm=\"GitServer\"", Challenge(context));
	}

	[Fact]
	public async Task PrivateRepo_OwnerWithCorrectPassword_IsLetIn_AndIdentified()
	{
		var alice = await _h.AddUserAsync("alice");
		_h.World.AddRepo(alice, "secret", isPrivate: true);

		var context = await _h.CloneAsync("alice", "secret", BasicHeader("alice"));

		Assert.True(_h.NextWasCalled);
		Assert.Equal(alice.Id, ((AppUser)context.Items["GitUser"]!).Id);
	}

	[Fact]
	public async Task UsernameInTheBasicHeader_IsCaseInsensitive_AndTheEmailAddressWorksToo()
	{
		var alice = await _h.AddUserAsync("alice");
		_h.World.AddRepo(alice, "secret", isPrivate: true);

		Assert.True((await _h.CloneAsync("alice", "secret", BasicHeader("ALICE"))).Response.StatusCode == 200);
		Assert.True((await _h.CloneAsync("alice", "secret", BasicHeader("alice@example.com"))).Response.StatusCode == 200);
	}

	[Fact]
	public async Task WrongPassword_IsChallengedAgain()
	{
		var alice = await _h.AddUserAsync("alice");
		_h.World.AddRepo(alice, "secret", isPrivate: true);

		var context = await _h.CloneAsync("alice", "secret", BasicHeader("alice", "wrong-password"));

		Assert.Equal(401, context.Response.StatusCode);
		Assert.False(_h.NextWasCalled);
	}

	[Fact]
	public async Task TooManyWrongPasswords_LockTheAccount_EvenForTheRightPassword()
	{
		var alice = await _h.AddUserAsync("alice");
		_h.World.AddRepo(alice, "secret", isPrivate: true);

		for (var i = 0; i < 3; i++) await _h.CloneAsync("alice", "secret", BasicHeader("alice", "wrong-password"));
		var context = await _h.CloneAsync("alice", "secret", BasicHeader("alice"));

		Assert.Equal(401, context.Response.StatusCode);
		Assert.False(_h.NextWasCalled);
	}

	[Fact]
	public async Task ASuccessfulLogin_ResetsTheCountOfWrongPasswords()
	{
		var alice = await _h.AddUserAsync("alice");
		_h.World.AddRepo(alice, "secret", isPrivate: true);

		for (var round = 0; round < 3; round++)
		{
			await _h.CloneAsync("alice", "secret", BasicHeader("alice", "wrong-password"));
			await _h.CloneAsync("alice", "secret", BasicHeader("alice", "wrong-password"));
			await _h.CloneAsync("alice", "secret", BasicHeader("alice"));
			Assert.True(_h.NextWasCalled, $"round {round}");
		}
	}

	[Fact]
	public async Task ADisabledAccount_CannotAuthenticate_EvenWithTheRightPassword()
	{
		var alice = await _h.AddUserAsync("alice");
		_h.World.AddRepo(alice, "secret", isPrivate: true);
		await _h.DisableAsync(alice);

		Assert.Equal(401, (await _h.CloneAsync("alice", "secret", BasicHeader("alice"))).Response.StatusCode);
	}

	[Theory]
	[InlineData("Basic !!!not-base64!!!")]
	[InlineData("Basic ")]
	[InlineData("Bearer some-token")]
	[InlineData("Basic bm9jb2xvbg==")]            // "nocolon"
	public async Task MalformedOrUnsupportedAuthorizationHeaders_AreTreatedAsAnonymous(string header)
	{
		var alice = await _h.AddUserAsync("alice");
		_h.World.AddRepo(alice, "secret", isPrivate: true);

		var context = await _h.CloneAsync("alice", "secret", header);

		Assert.Equal(401, context.Response.StatusCode);
	}

	[Fact]
	public async Task AuthenticatedButUnauthorised_Is403_NotAnotherChallenge()
	{
		var alice = await _h.AddUserAsync("alice");
		await _h.AddUserAsync("mallory");
		_h.World.AddRepo(alice, "secret", isPrivate: true);

		var context = await _h.CloneAsync("alice", "secret", BasicHeader("mallory"));

		Assert.Equal(403, context.Response.StatusCode);
		Assert.Empty(Challenge(context)!);
	}

	[Fact]
	public async Task ACollaboratorWithReadAccess_CanCloneAPrivateRepo()
	{
		var alice = await _h.AddUserAsync("alice");
		var reader = await _h.AddUserAsync("reader");
		var repo = _h.World.AddRepo(alice, "secret", isPrivate: true);
		_h.World.Grant(repo, reader, AccessLevel.Read);

		Assert.True((await _h.CloneAsync("alice", "secret", BasicHeader("reader"))).Response.StatusCode == 200);
	}

	// ---- Pushing -------------------------------------------------------------------------------

	[Fact]
	public async Task Push_WithoutCredentials_IsChallenged_EvenOnAPublicRepo()
	{
		var alice = await _h.AddUserAsync("alice");
		_h.World.AddRepo(alice, "repo");

		Assert.Equal(401, (await _h.PushAsync("alice", "repo")).Response.StatusCode);
	}

	[Fact]
	public async Task Push_ByTheOwner_IsAllowed()
	{
		var alice = await _h.AddUserAsync("alice");
		_h.World.AddRepo(alice, "repo");

		var context = await _h.PushAsync("alice", "repo", BasicHeader("alice"));

		Assert.True(_h.NextWasCalled);
		Assert.Equal(alice.Id, ((AppUser)context.Items["GitUser"]!).Id);
	}

	[Fact]
	public async Task Push_ByAReader_IsForbidden_ByAWriter_Allowed()
	{
		var alice = await _h.AddUserAsync("alice");
		var reader = await _h.AddUserAsync("reader");
		var writer = await _h.AddUserAsync("writer");
		var repo = _h.World.AddRepo(alice, "repo");
		_h.World.Grant(repo, reader, AccessLevel.Read);
		_h.World.Grant(repo, writer, AccessLevel.Write);

		Assert.Equal(403, (await _h.PushAsync("alice", "repo", BasicHeader("reader"))).Response.StatusCode);
		Assert.Equal(200, (await _h.PushAsync("alice", "repo", BasicHeader("writer"))).Response.StatusCode);
	}

	[Fact]
	public async Task PushDiscovery_ViaTheServiceQuery_IsAlreadyTreatedAsAPush()
	{
		// git first asks GET info/refs?service=git-receive-pack; write access is checked right there.
		var alice = await _h.AddUserAsync("alice");
		var reader = await _h.AddUserAsync("reader");
		var repo = _h.World.AddRepo(alice, "repo");
		_h.World.Grant(repo, reader, AccessLevel.Read);

		var context = await _h.SendAsync("GET", "/git/alice/repo.git/info/refs", "?service=git-receive-pack", BasicHeader("reader"));

		Assert.Equal(403, context.Response.StatusCode);
	}

	[Fact]
	public async Task AnonymousPush_OnAPublicRepo_OnlyWhenTheAdminAllowsIt()
	{
		var alice = await _h.AddUserAsync("alice");
		_h.World.AddRepo(alice, "repo");

		Assert.Equal(401, (await _h.PushAsync("alice", "repo")).Response.StatusCode);

		await _h.ConfigureAsync(s => s.AllowAnonymousPush = true);

		var context = await _h.PushAsync("alice", "repo");
		Assert.True(_h.NextWasCalled);
		Assert.Equal(200, context.Response.StatusCode);
	}

	[Fact]
	public async Task AnonymousPush_IsNeverAllowedOnAPrivateRepo()
	{
		await _h.ConfigureAsync(s => s.AllowAnonymousPush = true);
		var alice = await _h.AddUserAsync("alice");
		_h.World.AddRepo(alice, "secret", isPrivate: true);

		Assert.Equal(401, (await _h.PushAsync("alice", "secret")).Response.StatusCode);
	}

	// ---- Read-only repositories ----------------------------------------------------------------

	[Fact]
	public async Task ReadOnlyRepo_RejectsPush_EvenFromTheOwner_ButStillServesClones()
	{
		var alice = await _h.AddUserAsync("alice");
		_h.World.AddRepo(alice, "frozen", readOnly: true);

		var push = await _h.PushAsync("alice", "frozen", BasicHeader("alice"));
		var clone = await _h.CloneAsync("alice", "frozen");

		Assert.Equal(403, push.Response.StatusCode);
		Assert.Equal(200, clone.Response.StatusCode);
		Assert.True(_h.NextWasCalled);
	}

	[Fact]
	public async Task ReadOnlyRepo_AnonymousPushSetting_DoesNotOpenABackDoor()
	{
		await _h.ConfigureAsync(s => s.AllowAnonymousPush = true);
		var alice = await _h.AddUserAsync("alice");
		_h.World.AddRepo(alice, "frozen", readOnly: true);

		var context = await _h.PushAsync("alice", "frozen");

		Assert.False(_h.NextWasCalled);
		Assert.Equal(403, context.Response.StatusCode);
	}

	[Fact]
	public async Task ReadOnlyRepo_DiscoveryForPush_IsAlsoRefused()
	{
		var alice = await _h.AddUserAsync("alice");
		_h.World.AddRepo(alice, "frozen", readOnly: true);

		var context = await _h.SendAsync("GET", "/git/alice/frozen.git/info/refs", "?service=git-receive-pack", BasicHeader("alice"));

		Assert.Equal(403, context.Response.StatusCode);
	}

	// ---- Creating a repository by pushing to it ----------------------------------------------------

	[Fact]
	public async Task PushingToANewName_CreatesTheRepo_OnDisk_AndInTheDatabase_PrivateByDefault()
	{
		var alice = await _h.AddUserAsync("alice");

		var context = await _h.PushAsync("alice", "brand-new", BasicHeader("alice"));

		Assert.True(_h.NextWasCalled);
		var repo = await _h.World.Repos.GetAsync("alice", "brand-new");
		Assert.NotNull(repo);
		Assert.Equal(alice.Id, repo!.OwnerId);
		Assert.True(repo.IsPrivate);
		Assert.True(File.Exists(Path.Combine(_h.World.ReposPath, "alice", "brand-new.git", "HEAD")));
		Assert.Equal(repo.Id, ((Repository)context.Items["GitRepo"]!).Id);
	}

	[Fact]
	public async Task AutoCreatedRepos_FollowTheConfiguredDefaultVisibility()
	{
		_h.World.Options.DefaultPrivateOnAutoCreate = false;
		await _h.AddUserAsync("alice");

		await _h.PushAsync("alice", "open", BasicHeader("alice"));

		Assert.False((await _h.World.Repos.GetAsync("alice", "open"))!.IsPrivate);
	}

	[Fact]
	public async Task TheCasingUsedOnTheFirstPush_BecomesThePermanentName()
	{
		await _h.AddUserAsync("alice");

		await _h.PushAsync("ALICE", "MyNewRepo", BasicHeader("alice"));

		var repo = await _h.World.Repos.GetAsync("alice", "mynewrepo");
		Assert.Equal("MyNewRepo", repo!.Name);
		Assert.Equal("alice", repo.OwnerName);            // owner folder uses the account's stored name
		Assert.True(Directory.Exists(Path.Combine(_h.World.ReposPath, "alice", "MyNewRepo.git")));
	}

	[Fact]
	public async Task PushingWithDifferentCasing_ToAnExistingRepo_NeverCreatesADuplicate()
	{
		var alice = await _h.AddUserAsync("alice");
		_h.World.AddRepo(alice, "MyRepo");

		var context = await _h.PushAsync("alice", "MYREPO", BasicHeader("alice"));

		Assert.True(_h.NextWasCalled);
		Assert.Equal("MyRepo", context.Items["GitRepoName"]);
		Assert.Equal(1, _h.World.Db.Repositories.Count());
	}

	[Fact]
	public async Task AutoCreate_CanBeSwitchedOffByTheAdmin()
	{
		await _h.ConfigureAsync(s => s.AllowPushToCreateRepositories = false);
		await _h.AddUserAsync("alice");

		var context = await _h.PushAsync("alice", "nope", BasicHeader("alice"));

		Assert.False(_h.NextWasCalled);
		Assert.Equal(404, context.Response.StatusCode);
		Assert.Empty(_h.World.Db.Repositories);
	}

	[Fact]
	public async Task AutoCreate_RequiresCredentials_AndOnlyInYourOwnNamespace()
	{
		await _h.AddUserAsync("alice");
		await _h.AddUserAsync("mallory");

		Assert.Equal(401, (await _h.PushAsync("alice", "x")).Response.StatusCode);
		Assert.Equal(403, (await _h.PushAsync("alice", "x", BasicHeader("mallory"))).Response.StatusCode);
		Assert.Empty(_h.World.Db.Repositories);
	}

	[Fact]
	public async Task AutoCreate_InAGroup_IsOpenToOwnerAndMembers_NotToStrangers()
	{
		var owner = await _h.AddUserAsync("owner");
		var member = await _h.AddUserAsync("member");
		await _h.AddUserAsync("stranger");
		var group = _h.World.AddGroup("Moneywise", owner, member);

		Assert.Equal(403, (await _h.PushAsync("Moneywise", "a", BasicHeader("stranger"))).Response.StatusCode);
		Assert.Equal(200, (await _h.PushAsync("moneywise", "b", BasicHeader("member"))).Response.StatusCode);
		Assert.Equal(200, (await _h.PushAsync("MONEYWISE", "c", BasicHeader("owner"))).Response.StatusCode);

		var created = _h.World.Db.Repositories.OrderBy(r => r.Name).ToList();
		Assert.Equal(new[] { "b", "c" }, created.Select(r => r.Name));
		Assert.All(created, r => { Assert.Equal(group.Id, r.GroupOwnerId); Assert.Null(r.OwnerId); });
		Assert.True(File.Exists(Path.Combine(_h.World.ReposPath, "Moneywise", "b.git", "HEAD")));
	}

	[Fact]
	public async Task AFetch_OfAMissingRepo_NeverCreatesIt()
	{
		await _h.AddUserAsync("alice");

		await _h.CloneAsync("alice", "ghost", BasicHeader("alice"));

		Assert.Empty(_h.World.Db.Repositories);
	}
}
