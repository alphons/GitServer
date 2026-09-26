using System.Net;
using System.Text.Json;
using GitServer.Data;
using GitServer.Models;
using GitServer.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GitServer.Tests;

/// <summary>Forking: a full bare copy under the forker's name or a group they own, private exactly when the source is,
/// surviving the deletion of its source as an orphan.</summary>
public class ForkEndToEndTests : IClassFixture<GitServerFactory>
{
	private readonly GitServerFactory _f;

	public ForkEndToEndTests(GitServerFactory factory) => _f = factory;

	private static string Unique(string stem) => stem + Guid.NewGuid().ToString("N")[..6];
	private static string En(string key) =>
		JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(TestPaths.LocalizationRoot, "en", "strings.json")))![key];

	private Task<T> Db<T>(Func<AppDbContext, Task<T>> action) => _f.UseServicesAsync(sp => action(sp.GetRequiredService<AppDbContext>()));
	private async Task<WebSession> AsAsync(AppUser user) => await new WebSession(_f).LoginAsync(user.UserName!);
	private string Folder(string owner, string repo) => Path.Combine(_f.ReposPath, owner, repo + ".git");

	private static Task<HttpResponseMessage> ForkPageAsync(WebSession session, string owner, string repo, params (string, string)[] fields) =>
		session.PostFormAsync($"/{owner}/{repo}/fork", $"/{owner}/{repo}/fork", fields);

	private static Task<HttpResponseMessage> ForkApiAsync(WebSession session, string owner, string repo, object? body = null) =>
		session.SendJsonAsync($"/{owner}/{repo}/fork", HttpMethod.Post, $"/api/repos/{owner}/{repo}/fork", body ?? new { });

	private Task<Repository?> FindAsync(string owner, string repo) =>
		Db(d => d.Repositories.Include(r => r.Owner).Include(r => r.GroupOwner)
			.SingleOrDefaultAsync(r => r.Name == repo && (r.Owner!.UserName == owner || r.GroupOwner!.Name == owner)));

	private async Task GrantReadAsync(Repository repo, AppUser user) =>
		await Db(async d =>
		{
			d.RepositoryAccesses.Add(new RepositoryAccess { RepositoryId = repo.Id, UserId = user.Id, Level = AccessLevel.Read });
			return await d.SaveChangesAsync();
		});

	[Fact]
	public async Task ForkingFromThePage_CopiesAllBranchesAndTags_UnderTheForkersName()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var bob = await _f.CreateUserAsync(Unique("bob"));
		var seeded = await _f.SeedHistoryAsync(alice, "project");
		await Db(async d => { (await d.Repositories.SingleAsync(r => r.OwnerId == alice.Id)).Description = "the original"; return await d.SaveChangesAsync(); });

		var response = await ForkPageAsync(await AsAsync(bob), alice.UserName!, "project", ("Name", "project"));

		Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
		Assert.Equal($"/{bob.UserName}/project", WebSession.Location(response));
		var source = (await FindAsync(alice.UserName!, "project"))!;
		var fork = (await FindAsync(bob.UserName!, "project"))!;
		Assert.True(fork.IsFork);
		Assert.Equal(source.Id, fork.ForkedFromId);
		Assert.Equal("the original", fork.Description);
		Assert.False(fork.IsPrivate);

		var folder = Folder(bob.UserName!, "project");
		Assert.Equal(seeded.LatestSha, LocalGit.ServerRef(folder, "refs/heads/main"));
		Assert.Equal(seeded.FeatureSha, LocalGit.ServerRef(folder, "refs/heads/feature"));
		Assert.Equal(seeded.FirstSha, LocalGit.ServerRef(folder, "refs/tags/v1"));
		Assert.Empty(LocalGit.Exec(folder, null, "remote").Out);   // no trace of the source's path on disk

		var audit = await Db(d => d.AuditEntries.SingleAsync(a => a.Action == "repo.fork" && a.ActorUserId == bob.Id));
		Assert.Equal($"{bob.UserName}/project", audit.Target);
		Assert.Equal($"from {alice.UserName}/project", audit.Details);
	}

	[Fact]
	public async Task TheForkShowsItsSource_AndTheSourceCountsItsForks()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var bob = await _f.CreateUserAsync(Unique("bob"));
		await _f.SeedHistoryAsync(alice, "shown");
		var session = await AsAsync(bob);
		await ForkPageAsync(session, alice.UserName!, "shown", ("Name", "shown"));

		var forkPage = await session.GetHtmlAsync($"/{bob.UserName}/shown");
		var sourcePage = await session.GetHtmlAsync($"/{alice.UserName}/shown");

		Assert.Contains(En("fork_forked_from"), forkPage);
		Assert.Contains($"href=\"/{alice.UserName}/shown\"", forkPage);
		Assert.DoesNotContain(En("fork_forked_from"), sourcePage);
		Assert.Contains($"{En("fork_button")} <span class=\"badge badge-muted\">1</span>", sourcePage);
	}

	[Fact]
	public async Task APrivateRepository_CanBeForkedByAReader_AndTheForkIsPrivate()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var reader = await _f.CreateUserAsync(Unique("reader"));
		await _f.SeedHistoryAsync(alice, "secret", isPrivate: true);
		await GrantReadAsync((await FindAsync(alice.UserName!, "secret"))!, reader);

		var response = await ForkApiAsync(await AsAsync(reader), alice.UserName!, "secret");

		Assert.Equal(HttpStatusCode.Created, response.StatusCode);
		var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
		Assert.True(body.GetProperty("isPrivate").GetBoolean());
		Assert.Equal($"/{reader.UserName}/secret", body.GetProperty("href").GetString());
		Assert.True((await FindAsync(reader.UserName!, "secret"))!.IsPrivate);
	}

	[Fact]
	public async Task SomeoneWhoCannotReadAPrivateRepository_CannotForkIt()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var mallory = await _f.CreateUserAsync(Unique("mallory"));
		await _f.SeedHistoryAsync(alice, "hidden", isPrivate: true);
		var session = await AsAsync(mallory);

		var api = await ForkApiAsync(await AsAsync(alice), alice.UserName!, "hidden", new { name = "own-copy" });   // (alice may: sanity check)
		var page = await session.GetAsync($"/{alice.UserName}/hidden/fork");
		var apiAsMallory = await session.SendJsonAsync($"/dashboard/Repo/New", HttpMethod.Post, $"/api/repos/{alice.UserName}/hidden/fork", new { });

		Assert.Equal(HttpStatusCode.Created, api.StatusCode);
		Assert.NotEqual(HttpStatusCode.OK, page.StatusCode);
		Assert.Equal(HttpStatusCode.NotFound, apiAsMallory.StatusCode);   // indistinguishable from a repository that does not exist
		Assert.Null(await FindAsync(mallory.UserName!, "hidden"));
	}

	[Fact]
	public async Task AFork_CanGoToAnOwnedGroup_UnderAnotherName()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var bob = await _f.CreateUserAsync(Unique("bob"));
		var team = await _f.CreateGroupAsync(Unique("team"), bob);
		await _f.SeedHistoryAsync(alice, "lib");

		var response = await ForkApiAsync(await AsAsync(bob), alice.UserName!, "lib", new { name = "lib-fork", group = team.Name.ToUpperInvariant() });

		Assert.Equal(HttpStatusCode.Created, response.StatusCode);
		var fork = (await FindAsync(team.Name, "lib-fork"))!;
		Assert.Equal(team.Id, fork.GroupOwnerId);
		Assert.Null(fork.OwnerId);
		Assert.True(Directory.Exists(Folder(team.Name, "lib-fork")));
	}

	[Fact]
	public async Task ForkRequests_AreValidated()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var bob = await _f.CreateUserAsync(Unique("bob"));
		var carol = await _f.CreateUserAsync(Unique("carol"));
		var carolsTeam = await _f.CreateGroupAsync(Unique("team"), carol, bob);
		await Db(async d => { (await d.GroupMembers.SingleAsync(m => m.GroupId == carolsTeam.Id && m.UserId == bob.Id)).Role = GroupRole.Read; return await d.SaveChangesAsync(); });   // bob may only read there
		await _f.SeedHistoryAsync(alice, "tool");
		await _f.CreateRepoAsync(bob, "tool");
		var session = await AsAsync(bob);

		var taken = await ForkApiAsync(session, alice.UserName!, "tool");
		var invalid = await ForkApiAsync(session, alice.UserName!, "tool", new { name = "bad name!" });
		var readOnlyGroup = await ForkApiAsync(session, alice.UserName!, "tool", new { name = "tool2", group = carolsTeam.Name });
		var takenOnPage = await ForkPageAsync(session, alice.UserName!, "tool", ("Name", "tool"));

		Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
		Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
		Assert.Equal(HttpStatusCode.NotFound, readOnlyGroup.StatusCode);
		Assert.Equal(HttpStatusCode.OK, takenOnPage.StatusCode);   // the form again, so the user can pick another name
		Assert.Contains(En("error_repo_name_taken"), await takenOnPage.Content.ReadAsStringAsync());
		Assert.False((await FindAsync(bob.UserName!, "tool"))!.IsFork);
	}

	[Fact]
	public async Task DeletingTheSource_LeavesAnOrphanedFork()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var bob = await _f.CreateUserAsync(Unique("bob"));
		var seeded = await _f.SeedHistoryAsync(alice, "doomed");
		var bobSession = await AsAsync(bob);
		await ForkPageAsync(bobSession, alice.UserName!, "doomed", ("Name", "doomed"));

		var delete = await (await AsAsync(alice)).PostFormAsync($"/{alice.UserName}/doomed/settings", $"/{alice.UserName}/doomed/settings?handler=Delete");

		Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);
		Assert.Null(await FindAsync(alice.UserName!, "doomed"));
		var fork = (await FindAsync(bob.UserName!, "doomed"))!;
		Assert.True(fork.IsFork);
		Assert.Null(fork.ForkedFromId);
		Assert.Equal(seeded.LatestSha, LocalGit.ServerRef(Folder(bob.UserName!, "doomed"), "refs/heads/main"));
		Assert.Contains(En("fork_source_deleted"), await bobSession.GetHtmlAsync($"/{bob.UserName}/doomed"));
	}

	[Fact]
	public async Task DeletingAGroup_OrphansForksOfItsRepositories()
	{
		var owner = await _f.CreateUserAsync(Unique("owner"));
		var bob = await _f.CreateUserAsync(Unique("bob"));
		var team = await _f.CreateGroupAsync(Unique("team"), owner);
		await _f.SeedHistoryAsync(owner, "shared", group: team);
		await ForkApiAsync(await AsAsync(bob), team.Name, "shared");

		var delete = await (await AsAsync(owner)).PostFormAsync($"/dashboard/User/GroupDetail/{team.Id}", $"/dashboard/User/GroupDetail/{team.Id}?handler=Delete");

		Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);
		var fork = (await FindAsync(bob.UserName!, "shared"))!;
		Assert.True(fork.IsFork);
		Assert.Null(fork.ForkedFromId);
	}

	[Fact]
	public async Task AForkOfAPrivateRepository_CannotBeMadePublic()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var reader = await _f.CreateUserAsync(Unique("reader"));
		await _f.SeedHistoryAsync(alice, "closed", isPrivate: true);
		await GrantReadAsync((await FindAsync(alice.UserName!, "closed"))!, reader);
		var session = await AsAsync(reader);
		await ForkApiAsync(session, alice.UserName!, "closed");

		var save = await session.PostFormAsync($"/{reader.UserName}/closed/settings", $"/{reader.UserName}/closed/settings?handler=Update",
			("Description", "leak"), ("IsPrivate", "false"), ("DefaultBranch", "main"));

		Assert.Equal(HttpStatusCode.OK, save.StatusCode);
		Assert.Contains(En("settings_fork_must_stay_private"), await save.Content.ReadAsStringAsync());
		var fork = (await FindAsync(reader.UserName!, "closed"))!;
		Assert.True(fork.IsPrivate);
		Assert.NotEqual("leak", fork.Description);
	}

}
