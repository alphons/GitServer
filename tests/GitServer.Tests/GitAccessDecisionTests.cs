using GitServer.Models;
using GitServer.Services;
using GitServer.Tests.TestSupport;
using Xunit;
using static GitServer.Services.GitAccessDecision;

namespace GitServer.Tests;

/// <summary>
/// The complete clone/fetch/push decision matrix for the git smart-HTTP endpoint, expressed as
/// pure policy questions (no HTTP): who is allowed to do what to which kind of repository, and
/// whether a missing repository may be created by pushing to it.
/// </summary>
public class GitAccessDecisionTests : IDisposable
{
	private readonly TestWorld _w = new();
	private readonly AppUser _owner;
	private readonly AppUser _reader;
	private readonly AppUser _writer;
	private readonly AppUser _stranger;

	public GitAccessDecisionTests()
	{
		_owner = _w.AddUser("owner");
		_reader = _w.AddUser("reader");
		_writer = _w.AddUser("writer");
		_stranger = _w.AddUser("stranger");
	}

	public void Dispose() => _w.Dispose();

	private Repository Public(bool readOnly = false) => WithGrants(_w.AddRepo(_owner, "pub", isPrivate: false, readOnly: readOnly));
	private Repository Private(bool readOnly = false) => WithGrants(_w.AddRepo(_owner, "priv", isPrivate: true, readOnly: readOnly));

	private Repository WithGrants(Repository repo)
	{
		_w.Grant(repo, _reader, AccessLevel.Read);
		_w.Grant(repo, _writer, AccessLevel.Write);
		return repo;
	}

	private Task<GitAccessDecision> Decide(Repository repo, AppUser? user, bool push, bool anonymousPush = false) =>
		_w.Access.DecideGitAccessAsync(repo, user, push, anonymousPush);

	// ---- Public repository -------------------------------------------------------------------

	[Fact]
	public async Task PublicRepo_CloneNeedsNoCredentials()
	{
		var repo = Public();

		Assert.Equal(Allow, await Decide(repo, null, push: false));
		Assert.Equal(Allow, await Decide(repo, _stranger, push: false));
	}

	[Fact]
	public async Task PublicRepo_AnonymousPush_IsChallenged_WhenAnonymousPushIsOff()
	{
		Assert.Equal(Unauthorized, await Decide(Public(), null, push: true, anonymousPush: false));
	}

	[Fact]
	public async Task PublicRepo_AnonymousPush_IsAllowed_WhenAnonymousPushIsOn()
	{
		Assert.Equal(Allow, await Decide(Public(), null, push: true, anonymousPush: true));
	}

	[Fact]
	public async Task PublicRepo_PushBySomeoneWithoutWriteAccess_IsForbidden()
	{
		var repo = Public();

		Assert.Equal(Forbidden, await Decide(repo, _stranger, push: true));
		Assert.Equal(Forbidden, await Decide(repo, _reader, push: true));
	}

	[Fact]
	public async Task PublicRepo_PushByOwnerAndWriter_IsAllowed()
	{
		var repo = Public();

		Assert.Equal(Allow, await Decide(repo, _owner, push: true));
		Assert.Equal(Allow, await Decide(repo, _writer, push: true));
	}

	// ---- Private repository ------------------------------------------------------------------

	[Fact]
	public async Task PrivateRepo_AnonymousClone_IsChallenged()
	{
		Assert.Equal(Unauthorized, await Decide(Private(), null, push: false));
	}

	[Fact]
	public async Task PrivateRepo_CloneByStranger_IsForbidden()
	{
		Assert.Equal(Forbidden, await Decide(Private(), _stranger, push: false));
	}

	[Fact]
	public async Task PrivateRepo_CloneByOwnerReaderWriter_IsAllowed()
	{
		var repo = Private();

		Assert.Equal(Allow, await Decide(repo, _owner, push: false));
		Assert.Equal(Allow, await Decide(repo, _reader, push: false));
		Assert.Equal(Allow, await Decide(repo, _writer, push: false));
	}

	[Fact]
	public async Task PrivateRepo_AnonymousPushSetting_NeverAppliesToPrivateRepos()
	{
		Assert.Equal(Unauthorized, await Decide(Private(), null, push: true, anonymousPush: true));
	}

	[Fact]
	public async Task PrivateRepo_PushRequiresWriteAccess()
	{
		var repo = Private();

		Assert.Equal(Forbidden, await Decide(repo, _stranger, push: true));
		Assert.Equal(Forbidden, await Decide(repo, _reader, push: true));
		Assert.Equal(Allow, await Decide(repo, _writer, push: true));
		Assert.Equal(Allow, await Decide(repo, _owner, push: true));
	}

	[Fact]
	public async Task GroupOwnedRepo_MembersCanPush_StrangersCannot()
	{
		var member = _w.AddUser("member");
		var group = _w.AddGroup("Team", _owner, member);
		var repo = _w.AddGroupRepo(group, isPrivate: true);

		Assert.Equal(Allow, await Decide(repo, member, push: true));
		Assert.Equal(Allow, await Decide(repo, _owner, push: true));
		Assert.Equal(Forbidden, await Decide(repo, _stranger, push: true));
		Assert.Equal(Unauthorized, await Decide(repo, null, push: false));
	}

	// ---- Read-only repositories ----------------------------------------------------------------

	[Fact]
	public async Task ReadOnlyRepo_RejectsEveryPush_IncludingTheOwner()
	{
		var repo = Private(readOnly: true);

		Assert.Equal(Forbidden, await Decide(repo, _owner, push: true));
		Assert.Equal(Forbidden, await Decide(repo, _writer, push: true));
	}

	[Fact]
	public async Task ReadOnlyPublicRepo_AnonymousPushSetting_CannotBypassReadOnly()
	{
		// Regression: the "allow anonymous push" shortcut used to skip the write check entirely.
		Assert.Equal(Forbidden, await Decide(Public(readOnly: true), null, push: true, anonymousPush: true));
	}

	[Fact]
	public async Task ReadOnlyRepo_CanStillBeCloned()
	{
		var publicRepo = Public(readOnly: true);
		var privateRepo = Private(readOnly: true);

		Assert.Equal(Allow, await Decide(publicRepo, null, push: false));
		Assert.Equal(Allow, await Decide(privateRepo, _reader, push: false));
		Assert.Equal(Unauthorized, await Decide(privateRepo, null, push: false));
	}

	// ---- Creating a repository by pushing to a name that doesn't exist yet ------------------------

	private Task<GitAccessDecision> AutoCreate(AppUser? user, AppUser? nsUser, Group? nsGroup, bool enabled = true) =>
		_w.Access.DecideAutoCreateAsync(user, nsUser, nsGroup, enabled);

	[Fact]
	public async Task AutoCreate_WhenDisabled_LooksLikeNotFound_EvenForTheOwner()
	{
		Assert.Equal(NotFound, await AutoCreate(_owner, _owner, null, enabled: false));
	}

	[Fact]
	public async Task AutoCreate_RequiresCredentials()
	{
		Assert.Equal(Unauthorized, await AutoCreate(null, _owner, null));
	}

	[Fact]
	public async Task AutoCreate_InOwnNamespace_IsAllowed_InSomeoneElsesIsForbidden()
	{
		Assert.Equal(Allow, await AutoCreate(_owner, _owner, null));
		Assert.Equal(Forbidden, await AutoCreate(_stranger, _owner, null));
	}

	[Fact]
	public async Task AutoCreate_InGroupNamespace_OwnerAndMembersMay_StrangersMayNot()
	{
		var member = _w.AddUser("member");
		var group = _w.AddGroup("Team", _owner, member);

		Assert.Equal(Allow, await AutoCreate(_owner, null, group));
		Assert.Equal(Allow, await AutoCreate(member, null, group));
		Assert.Equal(Forbidden, await AutoCreate(_stranger, null, group));
	}

	[Fact]
	public async Task AutoCreate_WithoutAnyNamespace_IsNotFound()
	{
		Assert.Equal(NotFound, await AutoCreate(_owner, null, null));
	}
}
