using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using GitServer.Data;
using GitServer.Models;
using GitServer.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GitServer.Tests.TestSupport.WebSession;

namespace GitServer.Tests;

/// <summary>Browsing the site as different people (anonymous, owner, group member, stranger, admin) through
/// the real pages, forms and authorization — including that the choices made in the UI really change what
/// the git endpoint allows.</summary>
public class WebPagesEndToEndTests : IClassFixture<GitServerFactory>
{
	private readonly GitServerFactory _f;

	public WebPagesEndToEndTests(GitServerFactory factory) => _f = factory;

	private static string Unique(string stem) => stem + Guid.NewGuid().ToString("N")[..6];
	private static string En(string key) => Text("en", key);
	private static string Text(string lang, string key) =>
		JsonSerializer.Deserialize<Dictionary<string, string>>(
			File.ReadAllText(Path.Combine(TestPaths.LocalizationRoot, lang, "strings.json")))![key];

	private WebSession Anonymous() => new(_f);
	private async Task<WebSession> AsAsync(AppUser user) => await new WebSession(_f).LoginAsync(user.UserName!);

	private static int RepoCards(string html) => Regex.Matches(html, "class=\"repo-card\"").Count;
	private static bool IsLoginRedirect(HttpResponseMessage r) =>
		r.StatusCode == HttpStatusCode.Redirect && r.Headers.Location != null &&
		(r.Headers.Location.IsAbsoluteUri ? r.Headers.Location.AbsolutePath : r.Headers.Location.OriginalString)
			.StartsWith("/Auth/Login", StringComparison.OrdinalIgnoreCase);

	private Task<T> Db<T>(Func<AppDbContext, Task<T>> action) =>
		_f.UseServicesAsync(sp => action(sp.GetRequiredService<AppDbContext>()));

	// ---- Explore and repository pages, anonymous ---------------------------------------------------

	[Fact]
	public async Task Explore_ListsPublicRepos_IncludingGroupOwnedOnes_AndNeverPrivateOnes()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var group = await _f.CreateGroupAsync(Unique("Team"), alice);
		await _f.CreateRepoAsync(alice, "visible-user-repo");
		await _f.CreateRepoAsync(alice, "hidden-user-repo", isPrivate: true);
		await _f.CreateGroupRepoAsync(group, "visible-group-repo");
		await _f.CreateGroupRepoAsync(group, "hidden-group-repo", isPrivate: true);

		var html = await Anonymous().GetHtmlAsync("/explore?q=" + Uri.EscapeDataString("-repo"));

		Assert.Contains($"{alice.UserName} / visible-user-repo", html);
		Assert.Contains($"{group.Name} / visible-group-repo", html);
		Assert.DoesNotContain("hidden-user-repo", html);
		Assert.DoesNotContain("hidden-group-repo", html);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task RepoPage_ResolvesAnyCasing_AndShowsTheStoredNames(bool upper)
	{
		var alice = await _f.CreateUserAsync(Unique("Alice"));
		await _f.CreateRepoAsync(alice, "MyRepo");
		var owner = upper ? alice.UserName!.ToUpperInvariant() : alice.UserName!.ToLowerInvariant();

		var html = await Anonymous().GetHtmlAsync($"/{owner}/{(upper ? "MYREPO" : "myrepo")}");

		Assert.Contains("MyRepo", html);
		Assert.Contains(alice.UserName!, html);
	}

	[Fact]
	public async Task RepoPage_LinksTheOwnerToTheRightProfile_UserOrGroup()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var group = await _f.CreateGroupAsync(Unique("Team"), alice);
		await _f.CreateRepoAsync(alice, "mine");
		await _f.CreateGroupRepoAsync(group, "ours");

		var userRepo = await Anonymous().GetHtmlAsync($"/{alice.UserName}/mine");
		var groupRepo = await Anonymous().GetHtmlAsync($"/{group.Name}/ours");

		Assert.Contains($"href=\"/User/{alice.UserName}\"", userRepo);
		Assert.DoesNotContain("href=\"/Group/", userRepo);
		Assert.Contains($"href=\"/Group/{group.Name}\"", groupRepo);
		Assert.DoesNotContain($"href=\"/User/{group.Name}\"", groupRepo);   // the original 404 bug
	}

	[Fact]
	public async Task RepoPage_ShowsWhetherItIsAUserOrGroupRepo_AndWhenItIsReadOnly()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var group = await _f.CreateGroupAsync(Unique("Team"), alice);
		await _f.CreateRepoAsync(alice, "plain");
		await _f.CreateGroupRepoAsync(group, "grouped");
		await _f.CreateRepoAsync(alice, "frozen", readOnly: true);

		var plain = await Anonymous().GetHtmlAsync($"/{alice.UserName}/plain");
		var grouped = await Anonymous().GetHtmlAsync($"/{group.Name}/grouped");
		var frozen = await Anonymous().GetHtmlAsync($"/{alice.UserName}/frozen");

		Assert.Matches("class=\"badge badge-muted\">\\s*" + En("user_label") + "\\s*</span>", plain);
		Assert.Contains("badge-group", grouped);
		Assert.DoesNotContain(En("settings_readonly_label"), plain);
		Assert.Contains(En("settings_readonly_label"), frozen);
	}

	[Fact]
	public async Task APrivateRepoPage_SendsAnonymousVisitorsToTheLogin()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		await _f.CreateRepoAsync(alice, "secret", isPrivate: true);

		var response = await Anonymous().GetAsync($"/{alice.UserName}/secret");

		Assert.True(IsLoginRedirect(response), "Location: " + Location(response));
	}

	[Fact]
	public async Task APrivateRepoPage_IsVisibleToItsOwner_NotToAStranger()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var mallory = await _f.CreateUserAsync(Unique("mallory"));
		await _f.CreateRepoAsync(alice, "secret", isPrivate: true);

		Assert.Equal(HttpStatusCode.OK, (await (await AsAsync(alice)).GetAsync($"/{alice.UserName}/secret")).StatusCode);
		Assert.True(IsLoginRedirect(await (await AsAsync(mallory)).GetAsync($"/{alice.UserName}/secret")));
	}

	// ---- Group page --------------------------------------------------------------------------------

	[Fact]
	public async Task GroupPage_IsCaseInsensitive_ShowsOwnerAndMembers_AndHidesPrivateRepos()
	{
		var owner = await _f.CreateUserAsync(Unique("owner"));
		var member = await _f.CreateUserAsync(Unique("member"));
		var group = await _f.CreateGroupAsync(Unique("Moneywise"), owner, member);
		await _f.CreateGroupRepoAsync(group, "public-one");
		await _f.CreateGroupRepoAsync(group, "private-one", isPrivate: true);

		var html = await Anonymous().GetHtmlAsync($"/Group/{group.Name.ToUpperInvariant()}");

		Assert.Contains(group.Name, html);
		Assert.Contains("public-one", html);
		Assert.DoesNotContain("private-one", html);
		Assert.Contains(owner.UserName!, html);
		Assert.Contains(member.UserName!, html);
		Assert.Contains(En("new_repo_owner_label"), html);
		Assert.Contains(En("group_repos_title") + " (1)", html);
	}

	[Fact]
	public async Task GroupPage_ShowsPrivateRepos_ToMembers()
	{
		var owner = await _f.CreateUserAsync(Unique("owner"));
		var member = await _f.CreateUserAsync(Unique("member"));
		var group = await _f.CreateGroupAsync(Unique("Team"), owner, member);
		await _f.CreateGroupRepoAsync(group, "private-one", isPrivate: true);

		var html = await (await AsAsync(member)).GetHtmlAsync($"/Group/{group.Name}");

		Assert.Contains("private-one", html);
	}

	[Fact]
	public async Task GroupPage_Paginates_TheRealTotalInTheHeading()
	{
		var owner = await _f.CreateUserAsync(Unique("owner"));
		var group = await _f.CreateGroupAsync(Unique("Big"), owner);
		await _f.AddRepoRowsAsync(null, group, "repo", 25);

		var page0 = await Anonymous().GetHtmlAsync($"/Group/{group.Name}");
		var page1 = await Anonymous().GetHtmlAsync($"/Group/{group.Name}?p=1");

		Assert.Contains(En("group_repos_title") + " (25)", page0);
		Assert.Equal(20, RepoCards(page0));
		Assert.Contains("?p=1", page0);
		Assert.Equal(5, RepoCards(page1));
		Assert.Contains("?p=0", page1);
	}

	[Fact]
	public async Task UnknownGroupAndUnknownUser_Are404()
	{
		var anonymous = Anonymous();

		Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/Group/no-such-group-anywhere")).StatusCode);
		Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/User/no-such-user-anywhere")).StatusCode);
	}

	// ---- Profile page ------------------------------------------------------------------------------

	[Fact]
	public async Task Profile_ForAVisitor_ShowsOnlyPublicRepos_WithoutTheOwnersTitles()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		await _f.CreateRepoAsync(alice, "pub");
		await _f.CreateRepoAsync(alice, "priv", isPrivate: true);

		var html = await Anonymous().GetHtmlAsync($"/User/{alice.UserName}");

		Assert.Contains("pub", html);
		Assert.DoesNotContain("priv", html.Replace("private", "").Replace("Private", ""));
		Assert.DoesNotContain(En("profile_own_repos_title"), html);      // "Your repositories" is for your own profile
		Assert.DoesNotContain(En("profile_group_repos_title"), html);
	}

	[Fact]
	public async Task Profile_ForTheOwner_ShowsPrivateRepos_GroupRepos_AndRealTotals()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var boss = await _f.CreateUserAsync(Unique("boss"));
		var group = await _f.CreateGroupAsync(Unique("Team"), boss, alice);
		await _f.AddRepoRowsAsync(alice, null, "mine", 23);
		await _f.CreateRepoAsync(alice, "my-secret", isPrivate: true);
		await _f.AddRepoRowsAsync(null, group, "shared", 22, isPrivate: true);

		var html = await (await AsAsync(alice)).GetHtmlAsync($"/User/{alice.UserName}");

		Assert.Contains(En("profile_own_repos_title") + " (24)", html);
		Assert.Contains(En("profile_group_repos_title") + " (22)", html);
		Assert.Equal(40, RepoCards(html));                                 // 20 + 20 on the first pages
	}

	[Fact]
	public async Task Profile_OwnAndGroupLists_PageIndependently()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var group = await _f.CreateGroupAsync(Unique("Team"), alice);
		await _f.AddRepoRowsAsync(alice, null, "own", 21);
		await _f.AddRepoRowsAsync(null, group, "grp", 45);
		var session = await AsAsync(alice);

		var ownPage2 = await session.GetHtmlAsync($"/User/{alice.UserName}?handler=Search&q=&p=1&gp=0");
		var groupPage3 = await session.GetHtmlAsync($"/User/{alice.UserName}?handler=Search&q=&p=0&gp=2");

		Assert.Contains("own-01", ownPage2);                              // page 2 of own = the single oldest repo
		Assert.DoesNotContain("own-21", ownPage2);
		Assert.Contains("grp-05", groupPage3);                            // page 3 of group = the last 5
		Assert.DoesNotContain("grp-06", groupPage3);
		Assert.Contains("own-21", groupPage3);                            // own list still on its first page
	}

	[Fact]
	public async Task ProfileSearch_FiltersBothTheOwnAndTheGroupRepositories()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var group = await _f.CreateGroupAsync(Unique("Team"), alice);
		await _f.CreateRepoAsync(alice, "billing-tool");
		await _f.CreateRepoAsync(alice, "notes");
		await _f.CreateGroupRepoAsync(group, "billing-api");
		await _f.CreateGroupRepoAsync(group, "website");

		var html = await (await AsAsync(alice)).GetHtmlAsync($"/User/{alice.UserName}?handler=Search&q=BILLING");

		Assert.Contains("billing-tool", html);
		Assert.Contains("billing-api", html);
		Assert.DoesNotContain("notes", html);
		Assert.DoesNotContain("website", html);
		Assert.Contains(En("profile_own_repos_title") + " (1)", html);
		Assert.Contains(En("profile_group_repos_title") + " (1)", html);
	}

	// ---- Language ----------------------------------------------------------------------------------

	[Theory]
	[InlineData("nl")]
	[InlineData("de")]
	[InlineData("ja")]
	[InlineData("ar")]
	public async Task TheLanguageCookie_ChangesThePageText_AndTheHtmlLangAttribute(string language)
	{
		var html = await Anonymous().GetHtmlAsync("/explore", language);

		Assert.Contains(Text(language, "nav_explore"), WebUtility.HtmlDecode(html));   // Razor writes non-Latin text as entities
		Assert.Contains($"<html lang=\"{language}\"", html);
	}

	[Fact]
	public async Task WithoutTheCookie_ThePageIsEnglish()
	{
		var html = await Anonymous().GetHtmlAsync("/explore");

		Assert.Contains("<html lang=\"en\"", html);
		Assert.Contains(En("nav_explore"), html);
	}

	// ---- Signing in --------------------------------------------------------------------------------

	[Fact]
	public async Task Login_WithTheRightPassword_Redirects_AndAWrongOneShowsTheError()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));

		var good = await Anonymous().PostFormAsync("/Auth/Login", "/Auth/Login", ("Username", alice.UserName!), ("Password", GitServerFactory.Password));
		var bad = await Anonymous().PostFormAsync("/Auth/Login", "/Auth/Login", ("Username", alice.UserName!), ("Password", "wrong-wrong"));

		Assert.Equal(HttpStatusCode.Redirect, good.StatusCode);
		Assert.Equal(HttpStatusCode.OK, bad.StatusCode);
		Assert.Contains(En("error_invalid_credentials"), await bad.Content.ReadAsStringAsync());
	}

	// ---- Repository settings: who may open them, and what they change ---------------------------------

	[Fact]
	public async Task Settings_AnonymousIsSentToLogin_StrangerIsRefused_OwnerSeesTheForm()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var mallory = await _f.CreateUserAsync(Unique("mallory"));
		await _f.CreateRepoAsync(alice, "repo");
		var url = $"/{alice.UserName}/repo/settings";

		Assert.True(IsLoginRedirect(await Anonymous().GetAsync(url)));
		Assert.True(IsLoginRedirect(await (await AsAsync(mallory)).GetAsync(url)));
		var html = await (await AsAsync(alice)).GetHtmlAsync(url);
		Assert.Contains(En("settings_readonly_label"), html);
		Assert.Contains(En("settings_delete_repo"), html);
	}

	[Fact]
	public async Task Settings_OfAGroupRepo_AreForTheGroupOwner_NotForMembers()
	{
		var owner = await _f.CreateUserAsync(Unique("owner"));
		var member = await _f.CreateUserAsync(Unique("member"));
		var group = await _f.CreateGroupAsync(Unique("Team"), owner, member);
		await _f.CreateGroupRepoAsync(group, "shared");
		var url = $"/{group.Name}/shared/settings";

		Assert.Equal(HttpStatusCode.OK, (await (await AsAsync(owner)).GetAsync(url)).StatusCode);
		Assert.True(IsLoginRedirect(await (await AsAsync(member)).GetAsync(url)));
	}

	[Fact]
	public async Task TickingReadOnlyInTheSettings_MakesTheGitEndpointRefusePushes()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		await _f.CreateRepoAsync(alice, "repo");
		var session = await AsAsync(alice);
		var git = _f.NewClient();
		var auth = GitWire.Basic(alice.UserName!, GitServerFactory.Password);
		var discovery = $"/git/{alice.UserName}/repo.git/info/refs?service=git-receive-pack";
		Assert.Equal(HttpStatusCode.OK, (await git.SendAsync(GitWire.Get(discovery, auth))).StatusCode);

		var save = await session.PostFormAsync($"/{alice.UserName}/repo/settings", $"/{alice.UserName}/repo/settings?handler=Update",
			("Description", "frozen now"), ("IsReadOnly", "true"), ("DefaultBranch", "main"));

		Assert.Equal(HttpStatusCode.OK, save.StatusCode);
		Assert.Equal(HttpStatusCode.Forbidden, (await git.SendAsync(GitWire.Get(discovery, auth))).StatusCode);
		Assert.True((await Db(db => db.Repositories.SingleAsync(r => r.OwnerId == alice.Id))).IsReadOnly);

		// ...and unticking it again makes it writable.
		await session.PostFormAsync($"/{alice.UserName}/repo/settings", $"/{alice.UserName}/repo/settings?handler=Update",
			("Description", ""), ("DefaultBranch", "main"));
		Assert.Equal(HttpStatusCode.OK, (await git.SendAsync(GitWire.Get(discovery, auth))).StatusCode);
	}

	[Fact]
	public async Task MakingARepoPrivate_HidesItFromAnonymousClones()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		await _f.CreateRepoAsync(alice, "repo");
		var git = _f.NewClient();
		var fetch = $"/git/{alice.UserName}/repo.git/info/refs?service=git-upload-pack";
		Assert.Equal(HttpStatusCode.OK, (await git.SendAsync(GitWire.Get(fetch))).StatusCode);

		await (await AsAsync(alice)).PostFormAsync($"/{alice.UserName}/repo/settings", $"/{alice.UserName}/repo/settings?handler=Update",
			("IsPrivate", "true"), ("DefaultBranch", "main"));

		Assert.Equal(HttpStatusCode.Unauthorized, (await git.SendAsync(GitWire.Get(fetch))).StatusCode);
	}

	[Fact]
	public async Task DeletingARepo_RemovesTheRowAndTheFolder_AndItsPageIs404()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		await _f.CreateRepoAsync(alice, "doomed");
		var folder = Path.Combine(_f.ReposPath, alice.UserName!, "doomed.git");
		Assert.True(Directory.Exists(folder));
		var session = await AsAsync(alice);

		var response = await session.PostFormAsync($"/{alice.UserName}/doomed/settings", $"/{alice.UserName}/doomed/settings?handler=Delete");

		Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
		Assert.False(Directory.Exists(folder));
		Assert.Equal(HttpStatusCode.NotFound, (await session.GetAsync($"/{alice.UserName}/doomed")).StatusCode);
		Assert.Equal(0, await Db(db => db.Repositories.CountAsync(r => r.OwnerId == alice.Id)));
	}

	[Fact]
	public async Task ADeleteRequest_FromAStranger_ChangesNothing()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var mallory = await _f.CreateUserAsync(Unique("mallory"));
		await _f.CreateRepoAsync(alice, "keep");
		var session = await AsAsync(mallory);

		var response = await session.PostFormAsync("/Auth/Login", $"/{alice.UserName}/keep/settings?handler=Delete");

		Assert.True(IsLoginRedirect(response));
		Assert.True(Directory.Exists(Path.Combine(_f.ReposPath, alice.UserName!, "keep.git")));
	}

	// ---- Collaborators -------------------------------------------------------------------------------

	[Fact]
	public async Task AddingACollaborator_GivesThemAccess_ToThePrivateRepo_OverGit()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var bob = await _f.CreateUserAsync(Unique("bob"));
		await _f.CreateRepoAsync(alice, "secret", isPrivate: true);
		var git = _f.NewClient();
		var bobAuth = GitWire.Basic(bob.UserName!, GitServerFactory.Password);
		var url = $"/git/{alice.UserName}/secret.git/info/refs?service=git-upload-pack";
		Assert.Equal(HttpStatusCode.Forbidden, (await git.SendAsync(GitWire.Get(url, bobAuth))).StatusCode);

		var page = $"/{alice.UserName}/secret/collaborators";
		var add = await (await AsAsync(alice)).PostFormAsync(page, page + "?handler=AddCollaborator",
			("CollaboratorName", bob.UserName!), ("CollaboratorLevel", "Read"));

		Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);
		Assert.Equal(HttpStatusCode.OK, (await git.SendAsync(GitWire.Get(url, bobAuth))).StatusCode);
	}

	[Fact]
	public async Task Collaborators_CanOnlyBeManagedByTheOwner()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var bob = await _f.CreateUserAsync(Unique("bob"));
		var repo = await _f.CreateRepoAsync(alice, "secret", isPrivate: true);
		await Db(async db =>
		{
			db.RepositoryAccesses.Add(new RepositoryAccess { RepositoryId = repo.Id, UserId = bob.Id, Level = AccessLevel.Write });
			await db.SaveChangesAsync();
			return 0;
		});

		var response = await (await AsAsync(bob)).GetAsync($"/{alice.UserName}/secret/collaborators");

		Assert.True(IsLoginRedirect(response));                            // a writer is not an owner
	}

	// ---- Creating repositories -----------------------------------------------------------------------

	[Fact]
	public async Task NewRepo_CreatesInYourOwnNamespace_AndRedirectsToIt()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));

		var response = await (await AsAsync(alice)).PostFormAsync("/Repo/New", "/Repo/New", ("Name", "brand-new"), ("Description", "hello"));

		Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
		Assert.Equal($"/{alice.UserName}/brand-new", Location(response));
		Assert.True(File.Exists(Path.Combine(_f.ReposPath, alice.UserName!, "brand-new.git", "HEAD")));
	}

	[Fact]
	public async Task NewRepo_CanTargetAGroupYouOwn_ButNotSomeoneElsesGroup()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var bob = await _f.CreateUserAsync(Unique("bob"));
		var mine = await _f.CreateGroupAsync(Unique("Mine"), alice);
		var theirs = await _f.CreateGroupAsync(Unique("Theirs"), bob);
		var session = await AsAsync(alice);

		var ok = await session.PostFormAsync("/Repo/New", "/Repo/New", ("Name", "in-my-group"), ("GroupOwnerId", mine.Id.ToString()));
		var refused = await session.PostFormAsync("/Repo/New", "/Repo/New", ("Name", "in-their-group"), ("GroupOwnerId", theirs.Id.ToString()));

		Assert.Equal($"/{mine.Name}/in-my-group", Location(ok));
		Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
		Assert.Contains(En("error_group_not_found"), await refused.Content.ReadAsStringAsync());
		Assert.Equal(0, await Db(db => db.Repositories.CountAsync(r => r.Name == "in-their-group")));
	}

	[Fact]
	public async Task NewRepo_RejectsANameThatDiffersOnlyInCase_AndInvalidNames()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		await _f.CreateRepoAsync(alice, "Taken");
		var session = await AsAsync(alice);

		var duplicate = await session.PostFormAsync("/Repo/New", "/Repo/New", ("Name", "taken"));
		var invalid = await session.PostFormAsync("/Repo/New", "/Repo/New", ("Name", "no spaces/allowed"));

		Assert.Contains(En("error_repo_name_taken"), await duplicate.Content.ReadAsStringAsync());
		Assert.Contains(En("error_invalid_repo_name"), await invalid.Content.ReadAsStringAsync());
		Assert.Equal(1, await Db(db => db.Repositories.CountAsync(r => r.OwnerId == alice.Id)));
	}

	// ---- Groups --------------------------------------------------------------------------------------

	[Fact]
	public async Task CreatingAGroup_Works_AndTheNameFollowsTheUsernameRules()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var bob = await _f.CreateUserAsync(Unique("bob"));
		var session = await AsAsync(alice);
		var name = Unique("Crew");

		var created = await session.PostFormAsync("/User/Groups", "/User/Groups?handler=Create", ("NewGroupName", name));
		var sameNameOtherCase = await (await AsAsync(bob)).PostFormAsync("/User/Groups", "/User/Groups?handler=Create", ("NewGroupName", name.ToUpperInvariant()));
		var clashesWithUser = await session.PostFormAsync("/User/Groups", "/User/Groups?handler=Create", ("NewGroupName", bob.UserName!.ToUpperInvariant()));
		var badChars = await session.PostFormAsync("/User/Groups", "/User/Groups?handler=Create", ("NewGroupName", "no spaces or/slashes"));

		Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
		Assert.Contains(En("error_group_name_taken"), await sameNameOtherCase.Content.ReadAsStringAsync());
		Assert.Contains(En("error_group_name_taken"), await clashesWithUser.Content.ReadAsStringAsync());
		Assert.Contains(En("error_invalid_group_name"), await badChars.Content.ReadAsStringAsync());
		Assert.Equal(1, await Db(db => db.Groups.CountAsync(g => g.Name == name)));
	}

	[Fact]
	public async Task GroupDetail_IsOnlyForTheOwner_AndManagesMembersAndDeletion()
	{
		var owner = await _f.CreateUserAsync(Unique("owner"));
		var member = await _f.CreateUserAsync(Unique("member"));
		var stranger = await _f.CreateUserAsync(Unique("stranger"));
		var group = await _f.CreateGroupAsync(Unique("Team"), owner);
		await _f.CreateGroupRepoAsync(group, "inside");
		var page = $"/User/GroupDetail/{group.Id}";
		var session = await AsAsync(owner);

		Assert.Equal(HttpStatusCode.NotFound, (await (await AsAsync(stranger)).GetAsync(page)).StatusCode);
		Assert.Equal(HttpStatusCode.OK, (await session.GetAsync(page)).StatusCode);

		await session.PostFormAsync(page, page + "?handler=AddMember", ("MemberName", member.UserName!));
		Assert.Equal(1, await Db(db => db.GroupMembers.CountAsync(m => m.GroupId == group.Id)));

		var membership = await Db(db => db.GroupMembers.SingleAsync(m => m.GroupId == group.Id));
		await session.PostFormAsync(page, page + $"?handler=RemoveMember&memberId={membership.Id}");
		Assert.Equal(0, await Db(db => db.GroupMembers.CountAsync(m => m.GroupId == group.Id)));

		var delete = await session.PostFormAsync(page, page + "?handler=Delete");
		Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);
		Assert.Equal(0, await Db(db => db.Groups.CountAsync(g => g.Id == group.Id)));
		Assert.Equal(0, await Db(db => db.Repositories.CountAsync(r => r.GroupOwnerId == group.Id)));   // cascade
	}

	[Fact]
	public async Task GroupDetail_ListsRepos_WithPaging_AndTheRealTotal()
	{
		var owner = await _f.CreateUserAsync(Unique("owner"));
		var group = await _f.CreateGroupAsync(Unique("Big"), owner);
		await _f.AddRepoRowsAsync(null, group, "repo", 23);
		var session = await AsAsync(owner);

		var first = await session.GetHtmlAsync($"/User/GroupDetail/{group.Id}");
		var second = await session.GetHtmlAsync($"/User/GroupDetail/{group.Id}?handler=Repos&rp=1");

		Assert.Contains(" (23)", first);
		Assert.Equal(20, RepoCards(first));
		Assert.Equal(3, RepoCards(second));
		Assert.DoesNotContain(En("index_empty_create"), first);              // "Create the first one!" only when empty
	}

	// ---- Site administration -------------------------------------------------------------------------

	[Theory]
	[InlineData("/Admin/Users")]
	[InlineData("/Admin/Settings")]
	[InlineData("/Admin/BlockedEmails")]
	[InlineData("/Admin/GitVersion")]
	public async Task AdminPages_AreOnlyForAdmins(string path)
	{
		var admin = await _f.CreateUserAsync(Unique("admin"), isAdmin: true);
		var user = await _f.CreateUserAsync(Unique("plain"));

		Assert.NotEqual(HttpStatusCode.OK, (await Anonymous().GetAsync(path)).StatusCode);
		Assert.NotEqual(HttpStatusCode.OK, (await (await AsAsync(user)).GetAsync(path)).StatusCode);
		Assert.Equal(HttpStatusCode.OK, (await (await AsAsync(admin)).GetAsync(path)).StatusCode);
	}

	[Fact]
	public async Task TheAdminNavigationLink_IsOnlyShownToAdmins()
	{
		var admin = await _f.CreateUserAsync(Unique("admin"), isAdmin: true);
		var user = await _f.CreateUserAsync(Unique("plain"));

		Assert.Contains("href=\"/Admin/Users\"", await (await AsAsync(admin)).GetHtmlAsync("/explore"));
		Assert.DoesNotContain("href=\"/Admin/Users\"", await (await AsAsync(user)).GetHtmlAsync("/explore"));
		Assert.DoesNotContain("href=\"/Admin/Users\"", await Anonymous().GetHtmlAsync("/explore"));
	}

	[Fact]
	public async Task NewRepoCreation_CanBeSwitchedOffByTheAdmin()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var session = await AsAsync(alice);
		await _f.UseServicesAsync(async sp =>
		{
			var settings = sp.GetRequiredService<GitServer.Services.SiteSettingsService>();
			var s = await settings.GetAsync(); s.AllowUserRepoCreation = false; await settings.SaveAsync(s);
		});
		try
		{
			var response = await session.PostFormAsync("/Repo/New", "/Repo/New", ("Name", "blocked"));

			Assert.Equal(HttpStatusCode.OK, response.StatusCode);
			Assert.Contains(En("new_repo_creation_disabled"), await response.Content.ReadAsStringAsync());
			Assert.Equal(0, await Db(db => db.Repositories.CountAsync(r => r.OwnerId == alice.Id)));
		}
		finally
		{
			await _f.UseServicesAsync(async sp =>
			{
				var settings = sp.GetRequiredService<GitServer.Services.SiteSettingsService>();
				var s = await settings.GetAsync(); s.AllowUserRepoCreation = true; await settings.SaveAsync(s);
			});
		}
	}
}
