using GitServer.Models;
using GitServer.Tests.TestSupport;
using Xunit;

namespace GitServer.Tests;

public class RepositoryServiceTests : IDisposable
{
	private readonly TestWorld _w = new();

	public void Dispose() => _w.Dispose();

	// ---- Case-insensitive lookup, canonical casing ----------------------------------------------

	[Theory]
	[InlineData("Alice", "MyRepo")]
	[InlineData("alice", "myrepo")]
	[InlineData("ALICE", "MYREPO")]
	[InlineData("aLiCe", "mYrEpO")]
	public async Task GetAsync_FindsUserRepo_RegardlessOfCasing_AndReturnsCanonicalNames(string owner, string repo)
	{
		var alice = _w.AddUser("Alice");
		var created = _w.AddRepo(alice, "MyRepo");

		var found = await _w.Repos.GetAsync(owner, repo);

		Assert.NotNull(found);
		Assert.Equal(created.Id, found!.Id);
		Assert.Equal("MyRepo", found.Name);
		Assert.Equal("Alice", found.OwnerName);
	}

	[Theory]
	[InlineData("Moneywise")]
	[InlineData("moneywise")]
	[InlineData("MONEYWISE")]
	public async Task GetAsync_FindsGroupRepo_RegardlessOfCasing_AndReturnsCanonicalNames(string groupName)
	{
		var owner = _w.AddUser("owner");
		var group = _w.AddGroup("Moneywise", owner);
		var created = _w.AddGroupRepo(group, "SqlBackup");

		var found = await _w.Repos.GetAsync(groupName, "sqlbackup");

		Assert.NotNull(found);
		Assert.Equal(created.Id, found!.Id);
		Assert.Equal("Moneywise", found.OwnerName);
		Assert.Equal("SqlBackup", found.Name);
		Assert.Equal(group.Id, found.GroupOwnerId);
		Assert.Null(found.OwnerId);
	}

	[Fact]
	public async Task GetAsync_UnknownOwnerOrRepo_ReturnsNull()
	{
		var alice = _w.AddUser("alice");
		_w.AddRepo(alice, "repo");

		Assert.Null(await _w.Repos.GetAsync("nobody", "repo"));
		Assert.Null(await _w.Repos.GetAsync("alice", "other"));
	}

	[Fact]
	public async Task GetAsync_SameRepoNameUnderDifferentOwners_ResolvesTheRightOne()
	{
		var alice = _w.AddUser("alice");
		var bob = _w.AddUser("bob");
		var group = _w.AddGroup("team", alice);
		var a = _w.AddRepo(alice, "shared");
		var b = _w.AddRepo(bob, "shared");
		var g = _w.AddGroupRepo(group, "shared");

		Assert.Equal(a.Id, (await _w.Repos.GetAsync("alice", "shared"))!.Id);
		Assert.Equal(b.Id, (await _w.Repos.GetAsync("BOB", "shared"))!.Id);
		Assert.Equal(g.Id, (await _w.Repos.GetAsync("Team", "shared"))!.Id);
	}

	[Fact]
	public void GetRepoPath_IsOwnerFolderThenRepoDotGit()
	{
		Assert.Equal(Path.Combine(_w.ReposPath, "Alice", "MyRepo.git"), _w.Repos.GetRepoPath("Alice", "MyRepo"));
	}

	[Fact]
	public void OwnerName_PrefersUserThenGroupThenEmpty()
	{
		var user = new AppUser { UserName = "alice" };
		var group = new Group { Name = "team" };

		Assert.Equal("alice", new Repository { Owner = user }.OwnerName);
		Assert.Equal("team", new Repository { GroupOwner = group }.OwnerName);
		Assert.Equal("", new Repository().OwnerName);
	}

	// ---- Creating and deleting (real git, real folders) ------------------------------------------

	[Fact]
	public async Task CreateAsync_AddsRow_AndInitialisesBareRepoOnDisk_UnderCanonicalNames()
	{
		var alice = _w.AddUser("Alice");

		var repo = await _w.Repos.CreateAsync(alice.Id, alice.UserName!, "MyRepo", "desc", isPrivate: true);

		Assert.True(repo.Id > 0);
		Assert.Equal(alice.Id, repo.OwnerId);
		Assert.Null(repo.GroupOwnerId);
		Assert.True(repo.IsPrivate);
		Assert.Equal("desc", repo.Description);
		Assert.True(File.Exists(Path.Combine(_w.ReposPath, "Alice", "MyRepo.git", "HEAD")));
		Assert.True(await _w.Git.IsEmpty(_w.Repos.GetRepoPath("Alice", "MyRepo")));
	}

	[Fact]
	public async Task CreateForGroupAsync_SetsGroupOwner_AndCreatesFolderUnderTheGroupName()
	{
		var owner = _w.AddUser("owner");
		var group = _w.AddGroup("Moneywise", owner);

		var repo = await _w.Repos.CreateForGroupAsync(group.Id, group.Name, "Api", null, isPrivate: false);

		Assert.Equal(group.Id, repo.GroupOwnerId);
		Assert.Null(repo.OwnerId);
		Assert.False(repo.IsReadOnly);
		Assert.True(File.Exists(Path.Combine(_w.ReposPath, "Moneywise", "Api.git", "HEAD")));
	}

	[Fact]
	public async Task DeleteAsync_RemovesRowAndFolder_EvenWithReadOnlyFiles()
	{
		var alice = _w.AddUser("alice");
		var repo = await _w.Repos.CreateAsync(alice.Id, "alice", "doomed", null, isPrivate: false);
		var folder = _w.Repos.GetRepoPath("alice", "doomed");
		foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
			File.SetAttributes(file, FileAttributes.ReadOnly);

		await _w.Repos.DeleteAsync(repo, "alice");

		Assert.False(Directory.Exists(folder));
		Assert.Null(await _w.Repos.GetAsync("alice", "doomed"));
	}

	[Fact]
	public async Task DeleteAsync_WhenFolderIsAlreadyGone_StillRemovesTheRow()
	{
		var alice = _w.AddUser("alice");
		var repo = _w.AddRepo(alice, "ghost");

		await _w.Repos.DeleteAsync(repo, "alice");

		Assert.Null(await _w.Repos.GetAsync("alice", "ghost"));
	}

	[Fact]
	public async Task DeletingAGroupRepo_LeavesTheGroupAndItsOtherRepos()
	{
		var owner = _w.AddUser("owner");
		var group = _w.AddGroup("team", owner);
		var keep = _w.AddGroupRepo(group, "keep");
		var drop = _w.AddGroupRepo(group, "drop");

		await _w.Repos.DeleteAsync(drop, "team");

		Assert.NotNull(await _w.Repos.GetAsync("team", "keep"));
		Assert.Equal(1, await _w.Repos.GetGroupRepoCountAsync(group.Id));
		Assert.Equal(keep.Id, (await _w.Repos.GetGroupReposAsync(group.Id)).Single().Id);
	}

	// ---- Listing: public / user / group / accessible-group repos ---------------------------------

	[Fact]
	public async Task GetPublicRepos_OnlyPublic_NewestFirst_IncludingGroupRepos()
	{
		var alice = _w.AddUser("alice");
		var group = _w.AddGroup("team", alice);
		_w.AddRepo(alice, "old-public");
		_w.AddRepo(alice, "hidden", isPrivate: true);
		_w.AddGroupRepo(group, "group-public");
		_w.AddGroupRepo(group, "group-hidden", isPrivate: true);

		var repos = await _w.Repos.GetPublicReposAsync();

		Assert.Equal(new[] { "group-public", "old-public" }, repos.Select(r => r.Name));
		Assert.Equal("team", repos[0].OwnerName);
		Assert.Equal("alice", repos[1].OwnerName);
	}

	[Fact]
	public async Task GetPublicRepos_Paginates()
	{
		var alice = _w.AddUser("alice");
		for (var i = 1; i <= 5; i++) _w.AddRepo(alice, $"r{i}");

		var page1 = await _w.Repos.GetPublicReposAsync(skip: 0, take: 2);
		var page2 = await _w.Repos.GetPublicReposAsync(skip: 2, take: 2);
		var page3 = await _w.Repos.GetPublicReposAsync(skip: 4, take: 2);

		Assert.Equal(new[] { "r5", "r4" }, page1.Select(r => r.Name));
		Assert.Equal(new[] { "r3", "r2" }, page2.Select(r => r.Name));
		Assert.Equal(new[] { "r1" }, page3.Select(r => r.Name));
	}

	[Fact]
	public async Task GetUserRepos_ExcludesPrivateUnlessAsked_AndNeverIncludesGroupRepos()
	{
		var alice = _w.AddUser("alice");
		var group = _w.AddGroup("team", alice);
		_w.AddRepo(alice, "pub");
		_w.AddRepo(alice, "priv", isPrivate: true);
		_w.AddGroupRepo(group, "group-repo");

		var publicOnly = await _w.Repos.GetUserReposAsync(alice.Id, includePrivate: false);
		var everything = await _w.Repos.GetUserReposAsync(alice.Id, includePrivate: true);

		Assert.Equal(new[] { "pub" }, publicOnly.Select(r => r.Name));
		Assert.Equal(new[] { "priv", "pub" }, everything.Select(r => r.Name));
	}

	[Fact]
	public async Task GetUserRepos_FiltersByNameOrDescription_CaseInsensitively()
	{
		var alice = _w.AddUser("alice");
		_w.AddRepo(alice, "WebShop");
		_w.AddRepo(alice, "tools", description: "A tiny WEB helper");
		_w.AddRepo(alice, "unrelated");

		var hits = await _w.Repos.GetUserReposAsync(alice.Id, includePrivate: true, query: "web");
		var count = await _w.Repos.GetUserRepoCountAsync(alice.Id, includePrivate: true, query: "web");

		Assert.Equal(new[] { "tools", "WebShop" }, hits.Select(r => r.Name));
		Assert.Equal(2, count);
	}

	[Fact]
	public async Task GetUserRepos_Paginates_AndCountIsTheTotalNotThePageSize()
	{
		var alice = _w.AddUser("alice");
		for (var i = 1; i <= 25; i++) _w.AddRepo(alice, $"r{i:00}");

		var page2 = await _w.Repos.GetUserReposAsync(alice.Id, includePrivate: true, skip: 10, take: 10);

		Assert.Equal(10, page2.Count);
		Assert.Equal("r15", page2[0].Name);
		Assert.Equal(25, await _w.Repos.GetUserRepoCountAsync(alice.Id, includePrivate: true));
	}

	[Fact]
	public async Task GetUserRepoCount_RespectsIncludePrivate_AndOtherUsersAreIgnored()
	{
		var alice = _w.AddUser("alice");
		var bob = _w.AddUser("bob");
		_w.AddRepo(alice, "a1");
		_w.AddRepo(alice, "a2", isPrivate: true);
		_w.AddRepo(bob, "b1");

		Assert.Equal(1, await _w.Repos.GetUserRepoCountAsync(alice.Id, includePrivate: false));
		Assert.Equal(2, await _w.Repos.GetUserRepoCountAsync(alice.Id, includePrivate: true));
	}

	[Fact]
	public async Task GetGroupRepos_PaginatesAndCounts_OnlyThatGroup()
	{
		var owner = _w.AddUser("owner");
		var g1 = _w.AddGroup("g1", owner);
		var g2 = _w.AddGroup("g2", owner);
		for (var i = 1; i <= 5; i++) _w.AddGroupRepo(g1, $"a{i}");
		_w.AddGroupRepo(g2, "other");

		var page = await _w.Repos.GetGroupReposAsync(g1.Id, skip: 3, take: 3);

		Assert.Equal(5, await _w.Repos.GetGroupRepoCountAsync(g1.Id));
		Assert.Equal(new[] { "a2", "a1" }, page.Select(r => r.Name));
	}

	[Fact]
	public async Task GetAccessibleGroupRepos_OwnedAndMemberGroups_NotOthers_SortedByGroupThenNewest()
	{
		var me = _w.AddUser("me");
		var boss = _w.AddUser("boss");
		var other = _w.AddUser("other");
		var owned = _w.AddGroup("Beta", me);
		var memberOf = _w.AddGroup("Alpha", boss, me);
		var foreign = _w.AddGroup("Gamma", other);
		_w.AddGroupRepo(owned, "b-old");
		_w.AddGroupRepo(memberOf, "a-only", isPrivate: true);
		_w.AddGroupRepo(owned, "b-new");
		_w.AddGroupRepo(foreign, "secret", isPrivate: true);

		var repos = await _w.Repos.GetAccessibleGroupReposAsync(me.Id);

		Assert.Equal(new[] { "a-only", "b-new", "b-old" }, repos.Select(r => r.Name));
		Assert.Equal(3, await _w.Repos.GetAccessibleGroupRepoCountAsync(me.Id));
	}

	[Fact]
	public async Task GetAccessibleGroupRepos_FilterAndPagination_Combine()
	{
		var me = _w.AddUser("me");
		var group = _w.AddGroup("Team", me);
		for (var i = 1; i <= 6; i++) _w.AddGroupRepo(group, $"api-{i}");
		_w.AddGroupRepo(group, "docs");

		var page = await _w.Repos.GetAccessibleGroupReposAsync(me.Id, "API", skip: 4, take: 4);

		Assert.Equal(new[] { "api-2", "api-1" }, page.Select(r => r.Name));
		Assert.Equal(6, await _w.Repos.GetAccessibleGroupRepoCountAsync(me.Id, "api"));
	}

	[Fact]
	public async Task GetAccessibleGroupRepos_UserInNoGroups_GetsNothing()
	{
		var me = _w.AddUser("me");
		_w.AddGroupRepo(_w.AddGroup("Team", _w.AddUser("boss")), "x");

		Assert.Empty(await _w.Repos.GetAccessibleGroupReposAsync(me.Id));
		Assert.Equal(0, await _w.Repos.GetAccessibleGroupRepoCountAsync(me.Id));
	}

	[Fact]
	public async Task SearchAsync_FindsPublicReposByNameDescriptionOrOwner_NeverPrivate()
	{
		var alice = _w.AddUser("alice");
		var group = _w.AddGroup("Moneywise", alice);
		_w.AddRepo(alice, "compiler");
		_w.AddRepo(alice, "notes", description: "about the COMPILER");
		_w.AddRepo(alice, "secret-compiler", isPrivate: true);
		_w.AddGroupRepo(group, "billing");

		Assert.Equal(new[] { "notes", "compiler" }, (await _w.Repos.SearchAsync("compiler")).Select(r => r.Name));
		Assert.Equal(new[] { "billing" }, (await _w.Repos.SearchAsync("moneywise")).Select(r => r.Name));
		Assert.Equal(new[] { "notes", "compiler" }, (await _w.Repos.SearchAsync("ALICE")).Select(r => r.Name));
	}
}
