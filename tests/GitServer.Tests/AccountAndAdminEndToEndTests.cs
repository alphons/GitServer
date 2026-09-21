using System.Net;
using System.Text.Json;
using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using GitServer.Tests.TestSupport;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GitServer.Tests.TestSupport.WebSession;

namespace GitServer.Tests;

/// <summary>The signed-in user's own settings and the administrator pages, through the real HTTP pipeline.</summary>
public class AccountAndAdminEndToEndTests : IClassFixture<GitServerFactory>
{
	private readonly GitServerFactory _f;

	public AccountAndAdminEndToEndTests(GitServerFactory factory) => _f = factory;

	private static string Unique(string stem) => stem + Guid.NewGuid().ToString("N")[..6];
	private static string En(string key) =>
		JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(TestPaths.LocalizationRoot, "en", "strings.json")))![key];

	private WebSession Anonymous() => new(_f);
	private async Task<WebSession> AsAsync(AppUser user) => await new WebSession(_f).LoginAsync(user.UserName!);
	private Task<T> Db<T>(Func<AppDbContext, Task<T>> q) => _f.UseServicesAsync(sp => q(sp.GetRequiredService<AppDbContext>()));
	private Task<AppUser?> Reload(string id) => _f.UseServicesAsync(sp => sp.GetRequiredService<UserManager<AppUser>>().FindByIdAsync(id));
	private static bool IsLoginRedirect(HttpResponseMessage r) =>
		r.StatusCode == HttpStatusCode.Redirect && r.Headers.Location != null &&
		(r.Headers.Location.IsAbsoluteUri ? r.Headers.Location.AbsolutePath : r.Headers.Location.OriginalString)
			.StartsWith("/Auth/Login", StringComparison.OrdinalIgnoreCase);

	private static (string, string)[] UserForm(AppUser u, bool disabled = false, bool admin = false, string display = "") => new[]
	{
		("userId", u.Id), ("userName", u.UserName!), ("displayName", display), ("email", u.Email!),
		("isDisabled", disabled ? "true" : "false"), ("isAdmin", admin ? "true" : "false"),
	};

	// ---- Account settings ------------------------------------------------------------------------------

	[Fact]
	public async Task SettingsPage_RequiresASignedInUser() =>
		Assert.True(IsLoginRedirect(await Anonymous().GetAsync("/User/Settings")));

	[Fact]
	public async Task SavingTheProfile_PersistsIt_AndRemembersTheTimeZoneInACookie()
	{
		var user = await _f.CreateUserAsync(Unique("prof"));
		var session = await AsAsync(user);

		var response = await session.PostFormAsync("/User/Settings", "/User/Settings?handler=Profile",
			("NewDisplayName", "Pro File"), ("NewBio", "Hello there"), ("NewCountry", " NL "),
			("NewCompanyName", ""), ("NewPreferredLanguage", "nl"), ("NewTimeZoneId", "Europe/Amsterdam"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.Contains("Europe%2FAmsterdam") || c.Contains("Europe/Amsterdam"));
		var saved = (await Reload(user.Id))!;
		Assert.Equal("Pro File", saved.DisplayName);
		Assert.Equal("Hello there", saved.Bio);
		Assert.Equal("NL", saved.Country);
		Assert.Null(saved.CompanyName);
		Assert.Equal("nl", saved.PreferredLanguage);
		Assert.Equal("Europe/Amsterdam", saved.TimeZoneId);
	}

	[Fact]
	public async Task ChangingThePassword_NeedsTheCurrentOne_AndTheNewOneThenWorks()
	{
		var user = await _f.CreateUserAsync(Unique("pw"));
		var session = await AsAsync(user);

		await session.PostFormAsync("/User/Settings", "/User/Settings?handler=Password", ("CurrentPassword", "wrong"), ("NewPassword", "Better1!pass"));
		await new WebSession(_f).LoginAsync(user.UserName!);                              // old password still valid

		await session.PostFormAsync("/User/Settings", "/User/Settings?handler=Password", ("CurrentPassword", GitServerFactory.Password), ("NewPassword", "Better1!pass"));

		await new WebSession(_f).LoginAsync(user.UserName!, "Better1!pass");
		await Assert.ThrowsAsync<InvalidOperationException>(() => new WebSession(_f).LoginAsync(user.UserName!));
	}

	[Fact]
	public async Task AnEmptyOrWeakNewPassword_IsRefused()
	{
		var user = await _f.CreateUserAsync(Unique("weak"));
		var session = await AsAsync(user);

		await session.PostFormAsync("/User/Settings", "/User/Settings?handler=Password", ("CurrentPassword", GitServerFactory.Password), ("NewPassword", ""));
		await session.PostFormAsync("/User/Settings", "/User/Settings?handler=Password", ("CurrentPassword", GitServerFactory.Password), ("NewPassword", "abc"));

		await new WebSession(_f).LoginAsync(user.UserName!);
	}

	// ---- Admin pages: who may enter -----------------------------------------------------------------------

	[Theory]
	[InlineData("/Admin/Users")]
	[InlineData("/Admin/Users?handler=Search&q=x")]
	[InlineData("/Admin/BlockedEmails")]
	[InlineData("/Admin/Settings")]
	[InlineData("/Admin/GitVersion")]
	public async Task AdminPages_AreClosedToAnonymousAndOrdinaryUsers_AndOpenToAdmins(string url)
	{
		var ordinary = await _f.CreateUserAsync(Unique("plain"));
		var admin = await _f.CreateUserAsync(Unique("boss"), isAdmin: true);

		var anonymous = await Anonymous().GetAsync(url);
		var plain = await (await AsAsync(ordinary)).GetAsync(url);

		Assert.NotEqual(HttpStatusCode.OK, anonymous.StatusCode);
		Assert.NotEqual(HttpStatusCode.OK, plain.StatusCode);
		Assert.True((int)plain.StatusCode < 500, $"{(int)plain.StatusCode}");
		Assert.Equal(HttpStatusCode.OK, (await (await AsAsync(admin)).GetAsync(url)).StatusCode);
	}

	[Fact]
	public async Task AnOrdinaryUser_CannotPostToTheAdminPages()
	{
		var ordinary = await _f.CreateUserAsync(Unique("plain"));
		var victim = await _f.CreateUserAsync(Unique("victim"));
		var session = await AsAsync(ordinary);
		var pattern = $"*@{Unique("evil")}.test";
		var before = (await _f.UseServicesAsync(sp => sp.GetRequiredService<SiteSettingsService>().GetAsync())).AllowRegistration;

		await session.PostFormAsync("/User/Settings", "/Admin/Users?handler=Delete", ("userId", victim.Id));
		await session.PostFormAsync("/User/Settings", "/Admin/BlockedEmails?handler=Add", ("NewPattern", pattern));
		await session.PostFormAsync("/User/Settings", "/Admin/Settings", ("AllowRegistration", before ? "false" : "true"));

		Assert.NotNull(await Reload(victim.Id));
		Assert.Equal(0, await Db(d => d.BlockedEmailPatterns.CountAsync(p => p.Pattern == pattern)));
		Assert.Equal(before, (await _f.UseServicesAsync(sp => sp.GetRequiredService<SiteSettingsService>().GetAsync())).AllowRegistration);
	}

	// ---- Admin: users ---------------------------------------------------------------------------------------

	[Fact]
	public async Task TheUserList_CanBeSearched_ByNameDisplayNameOrEmail()
	{
		var admin = await _f.CreateUserAsync(Unique("boss"), isAdmin: true);
		var stem = Unique("findme");
		await _f.CreateUserAsync(stem);
		var other = await _f.CreateUserAsync(Unique("bystander"));
		var session = await AsAsync(admin);

		var html = await session.GetHtmlAsync("/Admin/Users?handler=Search&q=" + stem.ToUpperInvariant());

		Assert.Contains(stem, html);
		Assert.DoesNotContain(other.UserName!, html);
	}

	[Fact]
	public async Task AnAdmin_CanCreateAUser_ThatCanThenSignIn()
	{
		var admin = await _f.CreateUserAsync(Unique("boss"), isAdmin: true);
		var name = Unique("made");

		await (await AsAsync(admin)).PostFormAsync("/Admin/Users", "/Admin/Users?handler=Save",
			("userName", name), ("displayName", ""), ("email", name + "@example.com"), ("newPassword", "Secret1!pass"), ("confirmPassword", "Secret1!pass"));

		await new WebSession(_f).LoginAsync(name, "Secret1!pass");
		var created = await Db(d => d.Users.SingleAsync(u => u.UserName == name));
		Assert.Equal(name, created.DisplayName);
		Assert.False(created.IsAdmin);
	}

	[Theory]
	[InlineData("", "")]
	[InlineData("Secret1!pass", "Different1!pass")]
	public async Task CreatingAUser_NeedsAMatchingPassword(string password, string confirm)
	{
		var admin = await _f.CreateUserAsync(Unique("boss"), isAdmin: true);
		var name = Unique("nopw");

		await (await AsAsync(admin)).PostFormAsync("/Admin/Users", "/Admin/Users?handler=Save",
			("userName", name), ("displayName", ""), ("email", name + "@example.com"), ("newPassword", password), ("confirmPassword", confirm));

		Assert.Equal(0, await Db(d => d.Users.CountAsync(u => u.UserName == name)));
	}

	[Fact]
	public async Task CreatingAUser_WithATakenName_IsRefused_EvenInAnotherCase()
	{
		var admin = await _f.CreateUserAsync(Unique("boss"), isAdmin: true);
		var existing = await _f.CreateUserAsync(Unique("taken"));

		await (await AsAsync(admin)).PostFormAsync("/Admin/Users", "/Admin/Users?handler=Save",
			("userName", existing.UserName!.ToUpperInvariant()), ("displayName", ""), ("email", "x" + existing.Email), ("newPassword", "Secret1!pass"), ("confirmPassword", "Secret1!pass"));

		Assert.Equal(1, await Db(d => d.Users.CountAsync(u => u.NormalizedUserName == existing.UserName!.ToUpperInvariant())));
	}

	[Fact]
	public async Task DisablingAUser_BlocksSignIn_AndEnablingRestoresIt()
	{
		var admin = await _f.CreateUserAsync(Unique("boss"), isAdmin: true);
		var target = await _f.CreateUserAsync(Unique("target"));
		var session = await AsAsync(admin);

		await session.PostFormAsync("/Admin/Users", "/Admin/Users?handler=Save", UserForm(target, disabled: true));
		await Assert.ThrowsAsync<InvalidOperationException>(() => new WebSession(_f).LoginAsync(target.UserName!));

		await session.PostFormAsync("/Admin/Users", "/Admin/Users?handler=Save", UserForm(target, disabled: false));
		await new WebSession(_f).LoginAsync(target.UserName!);
	}

	[Fact]
	public async Task AnAdmin_CanPromoteAUser_AndResetTheirPassword()
	{
		var admin = await _f.CreateUserAsync(Unique("boss"), isAdmin: true);
		var target = await _f.CreateUserAsync(Unique("target"));

		await (await AsAsync(admin)).PostFormAsync("/Admin/Users", "/Admin/Users?handler=Save",
			UserForm(target, admin: true, display: "Renamed").Concat(new[] { ("newPassword", "Reset1!pass"), ("confirmPassword", "Reset1!pass") }).ToArray());

		var saved = (await Reload(target.Id))!;
		Assert.True(saved.IsAdmin);
		Assert.Equal("Renamed", saved.DisplayName);
		await new WebSession(_f).LoginAsync(target.UserName!, "Reset1!pass");
	}

	[Fact]
	public async Task AnAdmin_CannotDisableThemselves()
	{
		var admin = await _f.CreateUserAsync(Unique("boss"), isAdmin: true);

		await (await AsAsync(admin)).PostFormAsync("/Admin/Users", "/Admin/Users?handler=Save", UserForm(admin, disabled: true, admin: true));

		await new WebSession(_f).LoginAsync(admin.UserName!);
	}

	[Fact]
	public async Task DeletingAUser_NeverWorksOnYourself()
	{
		var admin = await _f.CreateUserAsync(Unique("boss"), isAdmin: true);

		var response = await (await AsAsync(admin)).PostFormAsync("/Admin/Users", "/Admin/Users?handler=Delete", ("userId", admin.Id));

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		Assert.False((await Reload(admin.Id))!.IsDisabled);
	}

	[Fact]
	public async Task DeletingAUser_AnonymizesThem_KeepingEverythingTheyMadeVisible()
	{
		var admin = await _f.CreateUserAsync(Unique("boss"), isAdmin: true);
		var target = await _f.CreateUserAsync(Unique("busy"));
		var friend = await _f.CreateUserAsync(Unique("friend"));
		await _f.CreateGroupAsync(Unique("team"), friend, target);
		await _f.SeedHistoryAsync(target, "mine");
		var theirs = await _f.CreateRepoAsync(friend, "theirs");
		await Db(async d =>
		{
			var stored = await d.Users.SingleAsync(u => u.Id == target.Id);
			stored.Bio = "secret bio"; stored.CompanyName = "Acme";
			var issue = new Issue { RepositoryId = theirs.Id, Title = "From target", AuthorId = target.Id };
			d.Issues.Add(issue);
			await d.SaveChangesAsync();
			d.IssueComments.Add(new IssueComment { IssueId = issue.Id, AuthorId = target.Id, Body = "a comment" });
			await d.SaveChangesAsync();
			return 0;
		});
		var oldName = target.UserName!;

		var response = await (await AsAsync(admin)).PostFormAsync("/Admin/Users", "/Admin/Users?handler=Delete", ("userId", target.Id));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var anon = (await Reload(target.Id))!;
		Assert.Matches("^anonymous-[0-9]{4,5}$", anon.UserName);
		Assert.Equal(anon.UserName, anon.DisplayName);
		Assert.DoesNotContain(oldName, anon.Email!);
		Assert.Null(anon.Bio);
		Assert.Null(anon.CompanyName);
		Assert.False(anon.IsAdmin);
		Assert.True(anon.IsDisabled);
		Assert.False(await _f.UseServicesAsync(sp => sp.GetRequiredService<UserManager<AppUser>>().HasPasswordAsync(anon)));
		await Assert.ThrowsAsync<InvalidOperationException>(() => new WebSession(_f).LoginAsync(anon.UserName!));
		await Assert.ThrowsAsync<InvalidOperationException>(() => new WebSession(_f).LoginAsync(oldName));

		// the repository moved along with the name and is still browsable, its history intact
		var page = await Anonymous().GetHtmlAsync($"/{anon.UserName}/mine");
		Assert.Contains("README.md", page);
		Assert.Equal(HttpStatusCode.NotFound, (await Anonymous().GetAsync($"/{oldName}/mine")).StatusCode);
		Assert.False(Directory.Exists(Path.Combine(_f.ReposPath, oldName)));
		Assert.True(Directory.Exists(Path.Combine(_f.ReposPath, anon.UserName!, "mine.git")));

		// their issue and comment are still there, credited to the anonymous account
		var issues = await Db(d => d.Issues.Include(i => i.Comments).Where(i => i.RepositoryId == theirs.Id).ToListAsync());
		Assert.Equal(anon.Id, Assert.Single(issues).AuthorId);
		Assert.Single(issues[0].Comments);
	}

	[Fact]
	public async Task DeletingAPendingRegistration_RemovesItForReal()
	{
		var admin = await _f.CreateUserAsync(Unique("boss"), isAdmin: true);
		var name = Unique("pending");
		var pending = await _f.UseServicesAsync(async sp =>
		{
			var users = sp.GetRequiredService<UserManager<AppUser>>();
			var u = new AppUser { UserName = name, Email = name + "@example.com", EmailConfirmed = false };
			await users.CreateAsync(u);
			return u;
		});

		await (await AsAsync(admin)).PostFormAsync("/Admin/Users", "/Admin/Users?handler=Delete", ("userId", pending.Id));

		Assert.Null(await Reload(pending.Id));
	}


	// ---- Admin: blocked emails -------------------------------------------------------------------------------

	[Fact]
	public async Task BlockedEmails_CanBeAdded_NotDuplicated_AndDeleted()
	{
		var admin = await _f.CreateUserAsync(Unique("boss"), isAdmin: true);
		var pattern = $"*@{Unique("spam")}.test";
		var session = await AsAsync(admin);

		var added = await session.PostFormAsync("/Admin/BlockedEmails", "/Admin/BlockedEmails?handler=Add", ("NewPattern", "  " + pattern + " "));
		await session.PostFormAsync("/Admin/BlockedEmails", "/Admin/BlockedEmails?handler=Add", ("NewPattern", pattern));
		await session.PostFormAsync("/Admin/BlockedEmails", "/Admin/BlockedEmails?handler=Add", ("NewPattern", "   "));

		Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
		Assert.Equal(1, await Db(d => d.BlockedEmailPatterns.CountAsync(p => p.Pattern == pattern)));
		Assert.Contains(pattern, await session.GetHtmlAsync("/Admin/BlockedEmails"));

		var id = await Db(d => d.BlockedEmailPatterns.Where(p => p.Pattern == pattern).Select(p => p.Id).SingleAsync());
		await session.PostFormAsync("/Admin/BlockedEmails", "/Admin/BlockedEmails?handler=Delete", ("id", id.ToString()));
		Assert.Equal(0, await Db(d => d.BlockedEmailPatterns.CountAsync(p => p.Pattern == pattern)));
	}

	// ---- Admin: site settings ---------------------------------------------------------------------------------

	[Fact]
	public async Task SiteSettings_AreSaved_AndClosingRegistrationShowsOnTheRegisterPage()
	{
		var admin = await _f.CreateUserAsync(Unique("boss"), isAdmin: true);
		var session = await AsAsync(admin);
		try
		{
			await session.PostFormAsync("/Admin/Settings", "/Admin/Settings", ("AllowRegistration", "false"), ("AllowUserRepoCreation", "true"));

			var settings = await _f.UseServicesAsync(sp => sp.GetRequiredService<SiteSettingsService>().GetAsync());
			Assert.False(settings.AllowRegistration);
			Assert.True(settings.AllowUserRepoCreation);
			Assert.False(settings.AllowAnonymousPush);
			Assert.Contains(En("register_disabled"), await Anonymous().GetHtmlAsync("/Auth/Register"));
		}
		finally
		{
			await session.PostFormAsync("/Admin/Settings", "/Admin/Settings", ("AllowRegistration", "true"), ("AllowUserRepoCreation", "true"), ("AllowPushToCreateRepositories", "true"));
		}
		Assert.DoesNotContain(En("register_disabled"), await Anonymous().GetHtmlAsync("/Auth/Register"));
	}

	[Fact]
	public async Task TheGitVersionPage_ShowsTheGitInUse()
	{
		var admin = await _f.CreateUserAsync(Unique("boss"), isAdmin: true);

		var html = await (await AsAsync(admin)).GetHtmlAsync("/Admin/GitVersion");

		Assert.Contains("git version", html, StringComparison.OrdinalIgnoreCase);
	}
}
