using GitServer.Models;
using GitServer.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GitServer.Tests;

/// <summary>
/// Names are case-preserving but case-insensitive-unique ("Foo" and "foo" can't coexist), enforced
/// by the database itself (NOCASE collation + unique indexes), not just by application code.
/// </summary>
public class NamingConstraintTests : IDisposable
{
	private readonly TestWorld _w = new();

	public void Dispose() => _w.Dispose();

	[Fact]
	public void TwoReposOfTheSameUser_DifferingOnlyByCase_AreRejected()
	{
		var alice = _w.AddUser("alice");
		_w.AddRepo(alice, "VMTux");

		Assert.Throws<DbUpdateException>(() => _w.AddRepo(alice, "vmtux"));
	}

	[Fact]
	public void TwoReposInTheSameGroup_DifferingOnlyByCase_AreRejected()
	{
		var group = _w.AddGroup("team", _w.AddUser("owner"));
		_w.AddGroupRepo(group, "Api");

		Assert.Throws<DbUpdateException>(() => _w.AddGroupRepo(group, "API"));
	}

	[Fact]
	public void SameRepoName_UnderDifferentOwners_IsFine_EvenWithDifferentCasing()
	{
		var alice = _w.AddUser("alice");
		var bob = _w.AddUser("bob");
		var group = _w.AddGroup("team", alice);

		_w.AddRepo(alice, "tool");
		_w.AddRepo(bob, "Tool");
		_w.AddGroupRepo(group, "TOOL");

		Assert.Equal(3, _w.Db.Repositories.Count());
	}

	[Fact]
	public void GroupNames_AreGloballyUnique_IgnoringCase()
	{
		var alice = _w.AddUser("alice");
		var bob = _w.AddUser("bob");
		_w.AddGroup("Moneywise", alice);

		Assert.Throws<DbUpdateException>(() => _w.AddGroup("moneywise", bob));
	}

	[Fact]
	public void LookingUpByName_IsCaseInsensitive_AndKeepsTheStoredCasing()
	{
		var alice = _w.AddUser("alice");
		_w.AddRepo(alice, "MixedCase");
		_w.AddGroup("TeamName", alice);

		var repo = _w.Db.Repositories.Single(r => r.Name == "mixedcase");
		var group = _w.Db.Groups.Single(g => g.Name == "TEAMNAME");

		Assert.Equal("MixedCase", repo.Name);
		Assert.Equal("TeamName", group.Name);
	}

	[Fact]
	public void RepoCanBeRecreatedAfterDelete_WithDifferentCasing()
	{
		var alice = _w.AddUser("alice");
		var repo = _w.AddRepo(alice, "Old");
		_w.Db.Repositories.Remove(repo);
		_w.Db.SaveChanges();

		var again = _w.AddRepo(alice, "old");

		Assert.Equal("old", again.Name);
	}

	[Fact]
	public void DeletingAGroup_DeletesItsRepositories()
	{
		var group = _w.AddGroup("team", _w.AddUser("owner"));
		_w.AddGroupRepo(group, "one");
		_w.AddGroupRepo(group, "two");

		_w.Db.Groups.Remove(group);
		_w.Db.SaveChanges();

		Assert.Empty(_w.Db.Repositories);
	}

	[Fact]
	public void DeletingAUser_DeletesTheirRepositories_ButNotGroupRepos()
	{
		var alice = _w.AddUser("alice");
		var bob = _w.AddUser("bob");
		var group = _w.AddGroup("team", bob);
		_w.AddRepo(alice, "mine");
		_w.AddGroupRepo(group, "shared");

		_w.Db.Users.Remove(alice);
		_w.Db.SaveChanges();

		Assert.Equal(new[] { "shared" }, _w.Db.Repositories.Select(r => r.Name).ToArray());
	}
}
