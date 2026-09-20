using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GitServer.Tests.TestSupport;

/// <summary>
/// A small, real world for tests: an in-memory Sqlite database created from the actual EF model
/// (so the NOCASE collations and filtered unique indexes are enforced), a temp repositories
/// folder, and the real <see cref="AccessPolicy"/> / <see cref="RepositoryService"/> on top of it.
/// Helpers keep the arrange step of a test to one line per user/group/repository.
/// </summary>
public sealed class TestWorld : IDisposable
{
	private readonly SqliteTestDbContext _fixture = new();
	private int _clock;

	public AppDbContext Db => _fixture.Db;
	public string ReposPath { get; } = Path.Combine(Path.GetTempPath(), $"gitserver-world-{Guid.NewGuid():N}");
	public GitServerOptions Options { get; }
	public TestGitExecutablePathProvider GitPath { get; } = new();
	public GitProcessService Git { get; }
	public AccessPolicy Access { get; }
	public RepositoryService Repos { get; }

	public TestWorld()
	{
		Directory.CreateDirectory(ReposPath);
		Options = new GitServerOptions { RepositoriesPath = ReposPath, GitPathPrefix = "/git" };
		Git = new GitProcessService(GitPath, NullLogger<GitProcessService>.Instance);
		Access = new AccessPolicy(Db);
		Repos = new RepositoryService(Db, Git, Microsoft.Extensions.Options.Options.Create(Options));
	}

	public AppUser AddUser(string userName, bool isAdmin = false, bool isDisabled = false)
	{
		var user = new AppUser
		{
			UserName = userName,
			NormalizedUserName = userName.ToUpperInvariant(),
			Email = $"{userName}@example.com",
			NormalizedEmail = $"{userName}@example.com".ToUpperInvariant(),
			IsAdmin = isAdmin,
			LockoutEnd = isDisabled ? DateTimeOffset.UtcNow.AddYears(100) : null,
		};
		Db.Users.Add(user);
		Db.SaveChanges();
		return user;
	}

	public Group AddGroup(string name, AppUser owner, params AppUser[] members)
	{
		var group = new Group { Name = name, OwnerId = owner.Id, Owner = owner };
		Db.Groups.Add(group);
		Db.SaveChanges();
		foreach (var member in members)
			Db.GroupMembers.Add(new GroupMember { GroupId = group.Id, UserId = member.Id });
		Db.SaveChanges();
		return group;
	}

	/// <summary>A repository owned by a user. Each call gets a later UpdatedAt so ordering is deterministic.</summary>
	public Repository AddRepo(AppUser owner, string name = "repo", bool isPrivate = false, bool readOnly = false, string? description = null)
	{
		var repo = new Repository
		{
			Name = name, Description = description, OwnerId = owner.Id, Owner = owner,
			IsPrivate = isPrivate, IsReadOnly = readOnly, UpdatedAt = NextTimestamp(),
		};
		Db.Repositories.Add(repo);
		Db.SaveChanges();
		return repo;
	}

	/// <summary>A repository owned by a group (no user owner).</summary>
	public Repository AddGroupRepo(Group group, string name = "repo", bool isPrivate = false, bool readOnly = false, string? description = null)
	{
		var repo = new Repository
		{
			Name = name, Description = description, GroupOwnerId = group.Id, GroupOwner = group,
			IsPrivate = isPrivate, IsReadOnly = readOnly, UpdatedAt = NextTimestamp(),
		};
		Db.Repositories.Add(repo);
		Db.SaveChanges();
		return repo;
	}

	public void Grant(Repository repo, AppUser user, AccessLevel level)
	{
		Db.RepositoryAccesses.Add(new RepositoryAccess { RepositoryId = repo.Id, UserId = user.Id, Level = level });
		Db.SaveChanges();
	}

	public void Grant(Repository repo, Group group, AccessLevel level)
	{
		Db.RepositoryAccesses.Add(new RepositoryAccess { RepositoryId = repo.Id, GroupId = group.Id, Level = level });
		Db.SaveChanges();
	}

	private DateTime NextTimestamp() => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(++_clock);

	public void Dispose()
	{
		_fixture.Dispose();
		if (!Directory.Exists(ReposPath)) return;

		foreach (var file in Directory.GetFiles(ReposPath, "*", SearchOption.AllDirectories))
			File.SetAttributes(file, FileAttributes.Normal);
		Directory.Delete(ReposPath, recursive: true);
	}
}
