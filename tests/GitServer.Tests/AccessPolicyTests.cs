using GitServer.Models;
using GitServer.Services;
using GitServer.Tests.TestSupport;
using Xunit;

namespace GitServer.Tests;

/// <summary>The read/write/administer/delete rules for repositories, groups and the site admin role.</summary>
public class AccessPolicyTests : IDisposable
{
	private readonly TestWorld _w = new();

	public void Dispose() => _w.Dispose();

	// ---- Reading and writing user-owned repositories --------------------------------------------

	[Fact]
	public async Task PublicRepo_AnyoneCanRead_NobodyCanWriteWithoutAccess()
	{
		var owner = _w.AddUser("owner");
		var other = _w.AddUser("other");
		var repo = _w.AddRepo(owner, isPrivate: false);

		Assert.True(await _w.Access.CanReadAsync(repo, other.Id));
		Assert.True(await _w.Access.CanReadAsync(repo, null));
		Assert.False(await _w.Access.CanWriteAsync(repo, other.Id));
		Assert.False(await _w.Access.CanWriteAsync(repo, null));
	}

	[Fact]
	public async Task PrivateRepo_NoAccess_DeniesReadAndWrite()
	{
		var owner = _w.AddUser("owner");
		var other = _w.AddUser("other");
		var repo = _w.AddRepo(owner, isPrivate: true);

		Assert.False(await _w.Access.CanReadAsync(repo, other.Id));
		Assert.False(await _w.Access.CanReadAsync(repo, null));
		Assert.False(await _w.Access.CanWriteAsync(repo, other.Id));
	}

	[Fact]
	public async Task Owner_AlwaysHasReadAndWrite_EvenPrivate()
	{
		var owner = _w.AddUser("owner");
		var repo = _w.AddRepo(owner, isPrivate: true);

		Assert.True(await _w.Access.CanReadAsync(repo, owner.Id));
		Assert.True(await _w.Access.CanWriteAsync(repo, owner.Id));
	}

	[Fact]
	public async Task DirectReadAccess_AllowsReadNotWrite()
	{
		var owner = _w.AddUser("owner");
		var collaborator = _w.AddUser("collaborator");
		var repo = _w.AddRepo(owner, isPrivate: true);
		_w.Grant(repo, collaborator, AccessLevel.Read);

		Assert.True(await _w.Access.CanReadAsync(repo, collaborator.Id));
		Assert.False(await _w.Access.CanWriteAsync(repo, collaborator.Id));
	}

	[Fact]
	public async Task DirectWriteAccess_AllowsReadAndWrite()
	{
		var owner = _w.AddUser("owner");
		var collaborator = _w.AddUser("collaborator");
		var repo = _w.AddRepo(owner, isPrivate: true);
		_w.Grant(repo, collaborator, AccessLevel.Write);

		Assert.True(await _w.Access.CanReadAsync(repo, collaborator.Id));
		Assert.True(await _w.Access.CanWriteAsync(repo, collaborator.Id));
	}

	[Fact]
	public async Task AccessGrantedOnOneRepo_DoesNotLeakToAnotherRepo()
	{
		var owner = _w.AddUser("owner");
		var collaborator = _w.AddUser("collaborator");
		var shared = _w.AddRepo(owner, "shared", isPrivate: true);
		var secret = _w.AddRepo(owner, "secret", isPrivate: true);
		_w.Grant(shared, collaborator, AccessLevel.Write);

		Assert.True(await _w.Access.CanWriteAsync(shared, collaborator.Id));
		Assert.False(await _w.Access.CanReadAsync(secret, collaborator.Id));
		Assert.False(await _w.Access.CanWriteAsync(secret, collaborator.Id));
	}

	// ---- Access granted through a group -------------------------------------------------------

	[Fact]
	public async Task GroupWriteAccess_AllowsMemberReadAndWrite()
	{
		var owner = _w.AddUser("owner");
		var member = _w.AddUser("member");
		var repo = _w.AddRepo(owner, isPrivate: true);
		var group = _w.AddGroup("team", owner, member);
		_w.Grant(repo, group, AccessLevel.Write);

		Assert.True(await _w.Access.CanReadAsync(repo, member.Id));
		Assert.True(await _w.Access.CanWriteAsync(repo, member.Id));
	}

	[Fact]
	public async Task GroupReadAccess_AllowsMemberReadNotWrite()
	{
		var owner = _w.AddUser("owner");
		var member = _w.AddUser("member");
		var repo = _w.AddRepo(owner, isPrivate: true);
		var group = _w.AddGroup("team", owner, member);
		_w.Grant(repo, group, AccessLevel.Read);

		Assert.True(await _w.Access.CanReadAsync(repo, member.Id));
		Assert.False(await _w.Access.CanWriteAsync(repo, member.Id));
	}

	[Fact]
	public async Task NonMemberOfGroup_HasNoAccess()
	{
		var owner = _w.AddUser("owner");
		var stranger = _w.AddUser("stranger");
		var repo = _w.AddRepo(owner, isPrivate: true);
		var group = _w.AddGroup("team", owner);
		_w.Grant(repo, group, AccessLevel.Write);

		Assert.False(await _w.Access.CanReadAsync(repo, stranger.Id));
		Assert.False(await _w.Access.CanWriteAsync(repo, stranger.Id));
	}

	// ---- Repositories owned by a group ---------------------------------------------------------

	[Fact]
	public async Task GroupOwnedRepo_GroupOwnerAndMembersCanReadAndWrite_StrangersCannot()
	{
		var groupOwner = _w.AddUser("groupowner");
		var member = _w.AddUser("member");
		var stranger = _w.AddUser("stranger");
		var group = _w.AddGroup("Team", groupOwner, member);
		var repo = _w.AddGroupRepo(group, isPrivate: true);

		Assert.True(await _w.Access.CanReadAsync(repo, groupOwner.Id));
		Assert.True(await _w.Access.CanWriteAsync(repo, groupOwner.Id));
		Assert.True(await _w.Access.CanReadAsync(repo, member.Id));
		Assert.True(await _w.Access.CanWriteAsync(repo, member.Id));
		Assert.False(await _w.Access.CanReadAsync(repo, stranger.Id));
		Assert.False(await _w.Access.CanWriteAsync(repo, stranger.Id));
		Assert.False(await _w.Access.CanReadAsync(repo, null));
	}

	[Fact]
	public async Task GroupOwnedPublicRepo_StrangerCanReadButNotWrite()
	{
		var groupOwner = _w.AddUser("groupowner");
		var stranger = _w.AddUser("stranger");
		var repo = _w.AddGroupRepo(_w.AddGroup("Team", groupOwner), isPrivate: false);

		Assert.True(await _w.Access.CanReadAsync(repo, stranger.Id));
		Assert.True(await _w.Access.CanReadAsync(repo, null));
		Assert.False(await _w.Access.CanWriteAsync(repo, stranger.Id));
	}

	[Fact]
	public async Task GroupOwnedRepo_ExplicitGrantToOutsider_Works()
	{
		var groupOwner = _w.AddUser("groupowner");
		var outsider = _w.AddUser("outsider");
		var repo = _w.AddGroupRepo(_w.AddGroup("Team", groupOwner), isPrivate: true);
		_w.Grant(repo, outsider, AccessLevel.Read);

		Assert.True(await _w.Access.CanReadAsync(repo, outsider.Id));
		Assert.False(await _w.Access.CanWriteAsync(repo, outsider.Id));
	}

	[Fact]
	public async Task GroupMembership_OfAnotherGroup_GivesNothing()
	{
		var ownerA = _w.AddUser("ownera");
		var ownerB = _w.AddUser("ownerb");
		var memberOfB = _w.AddUser("memberofb");
		var groupA = _w.AddGroup("A", ownerA);
		_w.AddGroup("B", ownerB, memberOfB);
		var repo = _w.AddGroupRepo(groupA, isPrivate: true);

		Assert.False(await _w.Access.CanReadAsync(repo, memberOfB.Id));
		Assert.False(await _w.Access.CanWriteAsync(repo, memberOfB.Id));
	}

	// ---- Read-only repositories ---------------------------------------------------------------

	[Fact]
	public async Task ReadOnlyRepo_NobodyCanWrite_NotEvenTheOwner()
	{
		var owner = _w.AddUser("owner");
		var repo = _w.AddRepo(owner, isPrivate: true, readOnly: true);

		Assert.False(await _w.Access.CanWriteAsync(repo, owner.Id));
		Assert.True(await _w.Access.CanReadAsync(repo, owner.Id));
	}

	[Fact]
	public async Task ReadOnlyRepo_OverridesWriteGrants_AndGroupMembership()
	{
		var owner = _w.AddUser("owner");
		var writer = _w.AddUser("writer");
		var member = _w.AddUser("member");
		var group = _w.AddGroup("Team", owner, member);
		var userRepo = _w.AddRepo(owner, "u", isPrivate: true, readOnly: true);
		_w.Grant(userRepo, writer, AccessLevel.Write);
		var groupRepo = _w.AddGroupRepo(group, "g", isPrivate: true, readOnly: true);

		Assert.False(await _w.Access.CanWriteAsync(userRepo, writer.Id));
		Assert.False(await _w.Access.CanWriteAsync(groupRepo, owner.Id));
		Assert.False(await _w.Access.CanWriteAsync(groupRepo, member.Id));
		Assert.True(await _w.Access.CanReadAsync(groupRepo, member.Id));
	}

	[Fact]
	public async Task ReadOnlyRepo_StaysPubliclyReadable()
	{
		var owner = _w.AddUser("owner");
		var repo = _w.AddRepo(owner, isPrivate: false, readOnly: true);

		Assert.True(await _w.Access.CanReadAsync(repo, null));
	}

	// ---- Owner / administer / delete ------------------------------------------------------------

	[Fact]
	public async Task IsOwner_TrueForUserOwner_FalseForCollaboratorStrangerAndAnonymous()
	{
		var owner = _w.AddUser("owner");
		var collaborator = _w.AddUser("collaborator");
		var repo = _w.AddRepo(owner, isPrivate: true);
		_w.Grant(repo, collaborator, AccessLevel.Write);

		Assert.True(await _w.Access.IsOwnerAsync(repo, owner.Id));
		Assert.False(await _w.Access.IsOwnerAsync(repo, collaborator.Id));
		Assert.False(await _w.Access.IsOwnerAsync(repo, null));
	}

	[Fact]
	public async Task IsOwner_ForGroupRepo_IsTheGroupOwnerNotMembers()
	{
		var groupOwner = _w.AddUser("groupowner");
		var member = _w.AddUser("member");
		var repo = _w.AddGroupRepo(_w.AddGroup("Team", groupOwner, member));

		Assert.True(await _w.Access.IsOwnerAsync(repo, groupOwner.Id));
		Assert.True(await _w.Access.CanAdministerAsync(repo, groupOwner.Id));
		Assert.False(await _w.Access.IsOwnerAsync(repo, member.Id));
		Assert.False(await _w.Access.CanAdministerAsync(repo, member.Id));
	}

	[Fact]
	public async Task CanDelete_OwnerAndSiteAdmin_ButNotOthers()
	{
		var owner = _w.AddUser("owner");
		var admin = _w.AddUser("admin", isAdmin: true);
		var other = _w.AddUser("other");
		var repo = _w.AddRepo(owner);

		Assert.True(await _w.Access.CanDeleteAsync(repo, owner));
		Assert.True(await _w.Access.CanDeleteAsync(repo, admin));
		Assert.False(await _w.Access.CanDeleteAsync(repo, other));
		Assert.False(await _w.Access.CanDeleteAsync(repo, null));
	}

	[Fact]
	public async Task SiteAdmin_DoesNotGetReadOrWriteAccessToPrivateRepos()
	{
		// Admin rights are for administering the site, not a back door into private code.
		var owner = _w.AddUser("owner");
		var admin = _w.AddUser("admin", isAdmin: true);
		var repo = _w.AddRepo(owner, isPrivate: true);

		Assert.False(await _w.Access.CanReadAsync(repo, admin.Id));
		Assert.False(await _w.Access.CanWriteAsync(repo, admin.Id));
	}

	[Fact]
	public void IsSiteAdmin_OnlyForAdminUsers()
	{
		Assert.False(AccessPolicy.IsSiteAdmin(null));
		Assert.False(AccessPolicy.IsSiteAdmin(new AppUser { IsAdmin = false }));
		Assert.True(AccessPolicy.IsSiteAdmin(new AppUser { IsAdmin = true }));
	}

	// ---- Issues ------------------------------------------------------------------------------

	[Fact]
	public async Task CanManageIssue_AuthorOrWriter_NotMereReaderOrAnonymous()
	{
		var owner = _w.AddUser("owner");
		var author = _w.AddUser("author");
		var reader = _w.AddUser("reader");
		var repo = _w.AddRepo(owner, isPrivate: true);
		_w.Grant(repo, author, AccessLevel.Read);
		_w.Grant(repo, reader, AccessLevel.Read);
		var issue = new Issue { RepositoryId = repo.Id, AuthorId = author.Id, Title = "t" };

		Assert.True(await _w.Access.CanManageIssueAsync(repo, issue, author.Id));
		Assert.True(await _w.Access.CanManageIssueAsync(repo, issue, owner.Id));
		Assert.False(await _w.Access.CanManageIssueAsync(repo, issue, reader.Id));
		Assert.False(await _w.Access.CanManageIssueAsync(repo, issue, null));
	}

	[Fact]
	public async Task CanManageIssue_AuthorKeepsControlEvenWhenRepoIsReadOnly()
	{
		var owner = _w.AddUser("owner");
		var author = _w.AddUser("author");
		var repo = _w.AddRepo(owner, readOnly: true);
		var issue = new Issue { RepositoryId = repo.Id, AuthorId = author.Id, Title = "t" };

		Assert.True(await _w.Access.CanManageIssueAsync(repo, issue, author.Id));
		Assert.False(await _w.Access.CanManageIssueAsync(repo, issue, owner.Id)); // read-only: even the owner can't write
	}

	// ---- Groups ------------------------------------------------------------------------------

	[Fact]
	public void IsGroupOwner_MatchesOwnerIdOnly()
	{
		var group = new Group { OwnerId = "u1" };

		Assert.True(AccessPolicy.IsGroupOwner(group, "u1"));
		Assert.False(AccessPolicy.IsGroupOwner(group, "u2"));
		Assert.False(AccessPolicy.IsGroupOwner(group, null));
	}

	[Fact]
	public async Task GetOwnedGroup_ReturnsOnlyForOwner()
	{
		var owner = _w.AddUser("owner");
		var other = _w.AddUser("other");
		var group = _w.AddGroup("Team", owner);

		Assert.Equal(group.Id, (await _w.Access.GetOwnedGroupAsync(group.Id, owner.Id))!.Id);
		Assert.Null(await _w.Access.GetOwnedGroupAsync(group.Id, other.Id));
		Assert.Null(await _w.Access.GetOwnedGroupAsync(group.Id + 999, owner.Id));
	}

	[Fact]
	public async Task GetOwnedGroups_AreSortedByName_AndOnlyTheUsersOwn()
	{
		var owner = _w.AddUser("owner");
		var other = _w.AddUser("other");
		var member = _w.AddUser("member");
		_w.AddGroup("Zulu", owner, member);
		_w.AddGroup("Alpha", owner);
		_w.AddGroup("Foreign", other);

		var groups = await _w.Access.GetOwnedGroupsAsync(owner.Id, includeMembers: true);

		Assert.Equal(new[] { "Alpha", "Zulu" }, groups.Select(g => g.Name));
		Assert.Single(groups.Single(g => g.Name == "Zulu").Members);
	}

	[Fact]
	public async Task CanCreateRepoInGroup_OwnerAndMembersOnly()
	{
		var owner = _w.AddUser("owner");
		var member = _w.AddUser("member");
		var stranger = _w.AddUser("stranger");
		var group = _w.AddGroup("Team", owner, member);

		Assert.True(await _w.Access.CanCreateRepoInGroupAsync(group, owner.Id));
		Assert.True(await _w.Access.CanCreateRepoInGroupAsync(group, member.Id));
		Assert.False(await _w.Access.CanCreateRepoInGroupAsync(group, stranger.Id));
	}
}
