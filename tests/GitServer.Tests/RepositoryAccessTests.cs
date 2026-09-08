using GitServer.Models;
using GitServer.Services;
using GitServer.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GitServer.Tests;

public class RepositoryAccessTests : IDisposable
{
	private readonly SqliteTestDbContext _fixture = new();
	private readonly RepositoryService _repos;

	public RepositoryAccessTests()
	{
		var git = new GitProcessService(new TestGitExecutablePathProvider(), NullLogger<GitProcessService>.Instance);
		var options = Options.Create(new GitServerOptions { RepositoriesPath = Path.GetTempPath() });
		_repos = new RepositoryService(_fixture.Db, git, options);
	}

	public void Dispose() => _fixture.Dispose();

	private AppUser AddUser(string userName)
	{
		var user = new AppUser { UserName = userName, Email = $"{userName}@example.com" };
		_fixture.Db.Users.Add(user);
		_fixture.Db.SaveChanges();
		return user;
	}

	private Repository AddRepo(AppUser owner, bool isPrivate)
	{
		var repo = new Repository { Name = "repo", OwnerId = owner.Id, Owner = owner, IsPrivate = isPrivate };
		_fixture.Db.Repositories.Add(repo);
		_fixture.Db.SaveChanges();
		return repo;
	}

	[Fact]
	public async Task PublicRepo_AnyoneCanRead_NobodyCanWriteWithoutAccess()
	{
		var owner = AddUser("owner");
		var other = AddUser("other");
		var repo = AddRepo(owner, isPrivate: false);

		Assert.True(await _repos.CanReadAsync(repo, other.Id));
		Assert.True(await _repos.CanReadAsync(repo, null));
		Assert.False(await _repos.CanWriteAsync(repo, other.Id));
		Assert.False(await _repos.CanWriteAsync(repo, null));
	}

	[Fact]
	public async Task PrivateRepo_NoAccess_DeniesReadAndWrite()
	{
		var owner = AddUser("owner");
		var other = AddUser("other");
		var repo = AddRepo(owner, isPrivate: true);

		Assert.False(await _repos.CanReadAsync(repo, other.Id));
		Assert.False(await _repos.CanReadAsync(repo, null));
		Assert.False(await _repos.CanWriteAsync(repo, other.Id));
	}

	[Fact]
	public async Task Owner_AlwaysHasReadAndWrite_EvenPrivate()
	{
		var owner = AddUser("owner");
		var repo = AddRepo(owner, isPrivate: true);

		Assert.True(await _repos.CanReadAsync(repo, owner.Id));
		Assert.True(await _repos.CanWriteAsync(repo, owner.Id));
	}

	[Fact]
	public async Task DirectReadAccess_AllowsReadNotWrite()
	{
		var owner = AddUser("owner");
		var collaborator = AddUser("collaborator");
		var repo = AddRepo(owner, isPrivate: true);
		_fixture.Db.RepositoryAccesses.Add(new RepositoryAccess { RepositoryId = repo.Id, UserId = collaborator.Id, Level = AccessLevel.Read });
		_fixture.Db.SaveChanges();

		Assert.True(await _repos.CanReadAsync(repo, collaborator.Id));
		Assert.False(await _repos.CanWriteAsync(repo, collaborator.Id));
	}

	[Fact]
	public async Task DirectWriteAccess_AllowsReadAndWrite()
	{
		var owner = AddUser("owner");
		var collaborator = AddUser("collaborator");
		var repo = AddRepo(owner, isPrivate: true);
		_fixture.Db.RepositoryAccesses.Add(new RepositoryAccess { RepositoryId = repo.Id, UserId = collaborator.Id, Level = AccessLevel.Write });
		_fixture.Db.SaveChanges();

		Assert.True(await _repos.CanReadAsync(repo, collaborator.Id));
		Assert.True(await _repos.CanWriteAsync(repo, collaborator.Id));
	}

	[Fact]
	public async Task GroupWriteAccess_AllowsMemberReadAndWrite()
	{
		var owner = AddUser("owner");
		var member = AddUser("member");
		var repo = AddRepo(owner, isPrivate: true);

		var group = new Group { Name = "team", OwnerId = owner.Id, Owner = owner };
		_fixture.Db.Groups.Add(group);
		_fixture.Db.SaveChanges();

		_fixture.Db.GroupMembers.Add(new GroupMember { GroupId = group.Id, UserId = member.Id });
		_fixture.Db.RepositoryAccesses.Add(new RepositoryAccess { RepositoryId = repo.Id, GroupId = group.Id, Level = AccessLevel.Write });
		_fixture.Db.SaveChanges();

		Assert.True(await _repos.CanReadAsync(repo, member.Id));
		Assert.True(await _repos.CanWriteAsync(repo, member.Id));
	}

	[Fact]
	public async Task GroupReadAccess_AllowsMemberReadNotWrite()
	{
		var owner = AddUser("owner");
		var member = AddUser("member");
		var repo = AddRepo(owner, isPrivate: true);

		var group = new Group { Name = "team", OwnerId = owner.Id, Owner = owner };
		_fixture.Db.Groups.Add(group);
		_fixture.Db.SaveChanges();

		_fixture.Db.GroupMembers.Add(new GroupMember { GroupId = group.Id, UserId = member.Id });
		_fixture.Db.RepositoryAccesses.Add(new RepositoryAccess { RepositoryId = repo.Id, GroupId = group.Id, Level = AccessLevel.Read });
		_fixture.Db.SaveChanges();

		Assert.True(await _repos.CanReadAsync(repo, member.Id));
		Assert.False(await _repos.CanWriteAsync(repo, member.Id));
	}

	[Fact]
	public async Task NonMemberOfGroup_HasNoAccess()
	{
		var owner = AddUser("owner");
		var stranger = AddUser("stranger");
		var repo = AddRepo(owner, isPrivate: true);

		var group = new Group { Name = "team", OwnerId = owner.Id, Owner = owner };
		_fixture.Db.Groups.Add(group);
		_fixture.Db.SaveChanges();

		_fixture.Db.RepositoryAccesses.Add(new RepositoryAccess { RepositoryId = repo.Id, GroupId = group.Id, Level = AccessLevel.Write });
		_fixture.Db.SaveChanges();

		Assert.False(await _repos.CanReadAsync(repo, stranger.Id));
		Assert.False(await _repos.CanWriteAsync(repo, stranger.Id));
	}
}
