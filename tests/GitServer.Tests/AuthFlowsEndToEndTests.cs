using System.Net;
using System.Text.Json;
using GitServer.Data;
using GitServer.Models;
using GitServer.Tests.TestSupport;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GitServer.Tests.TestSupport.WebSession;

namespace GitServer.Tests;

/// <summary>Everything around getting into an account: e-mail-first registration, completing it from the mailed
/// link, forgotten passwords, signing in and out, and the language switch — through the real forms, with mails
/// captured instead of sent.</summary>
public class AuthFlowsEndToEndTests : IClassFixture<GitServerFactory>
{
	private readonly GitServerFactory _f;

	public AuthFlowsEndToEndTests(GitServerFactory factory) => _f = factory;

	private static string Unique(string stem) => stem + Guid.NewGuid().ToString("N")[..6];
	private static string En(string key) =>
		JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(TestPaths.LocalizationRoot, "en", "strings.json")))![key];

	private WebSession NewSession() => new(_f);
	private Task<T> Db<T>(Func<AppDbContext, Task<T>> action) => _f.UseServicesAsync(sp => action(sp.GetRequiredService<AppDbContext>()));
	private Task<AppUser?> FindByEmail(string email) => Db(db => db.Users.SingleOrDefaultAsync(u => u.Email == email));

	/// <summary>Registers an address and returns the path+query of the link that was mailed.</summary>
	private async Task<string> RegisterAsync(string email, WebSession? session = null)
	{
		var response = await (session ?? NewSession()).PostFormAsync("/Auth/Register", "/Auth/Register", ("Email", email));
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		return _f.Mail.SentTo(email).Last().FirstLinkPathAndQuery();
	}

	private static (string Email, string Token) ParseLink(string pathAndQuery)
	{
		var query = System.Web.HttpUtility.ParseQueryString(new Uri("http://x" + pathAndQuery).Query);
		return (query["email"]!, query["token"]!);
	}

	private Task<HttpResponseMessage> CompleteAsync(WebSession s, string link, string username, string password = GitServerFactory.Password,
		string? confirm = null, string displayName = "")
	{
		var (email, token) = ParseLink(link);
		// The antiforgery token comes from the login page: an invalid link renders no form at all.
		return s.PostFormAsync("/Auth/Login", "/Auth/CompleteRegistration", ("Email", email), ("Token", token), ("Username", username),
			("DisplayName", displayName), ("Password", password), ("ConfirmPassword", confirm ?? password));
	}

	// ---- Registration ---------------------------------------------------------------------------

	[Fact]
	public async Task Register_SendsAConfirmationMail_ThatOpensTheCompletionForm()
	{
		var email = Unique("new") + "@example.com";

		var link = await RegisterAsync(email);

		var mail = Assert.Single(_f.Mail.SentTo(email));
		Assert.Equal(En("register_email_subject"), mail.Subject);
		Assert.StartsWith("/Auth/CompleteRegistration?", link);
		var page = await NewSession().GetHtmlAsync(link);
		Assert.Contains(En("complete_registration_title"), page);
		Assert.DoesNotContain(En("error_invalid_or_expired_link"), page);
	}

	[Fact]
	public async Task Register_ShowsThatAMailWasSent()
	{
		var response = await NewSession().PostFormAsync("/Auth/Register", "/Auth/Register", ("Email", Unique("x") + "@example.com"));

		Assert.Contains(En("register_email_sent"), await response.Content.ReadAsStringAsync());
	}

	[Fact]
	public async Task CompletingTheRegistration_CreatesAConfirmedAccount_SignsIn_AndAllowsLoginLater()
	{
		var email = Unique("new") + "@example.com";
		var username = Unique("newbie");
		var link = await RegisterAsync(email);
		var session = NewSession();

		var done = await CompleteAsync(session, link, username, displayName: "New Person");

		Assert.Equal(HttpStatusCode.Redirect, done.StatusCode);
		var user = await FindByEmail(email);
		Assert.Equal(username, user!.UserName);
		Assert.True(user.EmailConfirmed);
		Assert.Equal("New Person", user.DisplayName);
		Assert.Equal(HttpStatusCode.OK, (await session.GetAsync("/User/Settings")).StatusCode);      // already signed in
		await NewSession().LoginAsync(username);                                                       // and can sign in again
	}

	[Fact]
	public async Task ADisplayNameLeftBlank_DefaultsToTheUsername()
	{
		var email = Unique("new") + "@example.com";
		var username = Unique("plain");

		await CompleteAsync(NewSession(), await RegisterAsync(email), username);

		Assert.Equal(username, (await FindByEmail(email))!.DisplayName);
	}

	[Fact]
	public async Task RegisteringAnAlreadyRegisteredAddress_IsRefused_AndSendsNoMail()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));

		var response = await NewSession().PostFormAsync("/Auth/Register", "/Auth/Register", ("Email", alice.Email!));

		Assert.Contains(En("error_email_already_registered"), await response.Content.ReadAsStringAsync());
		Assert.Empty(_f.Mail.SentTo(alice.Email!));
	}

	[Fact]
	public async Task RegisteringTwiceBeforeConfirming_ResendsAMail_WithoutCreatingASecondAccount()
	{
		var email = Unique("twice") + "@example.com";

		await RegisterAsync(email);
		await RegisterAsync(email);

		Assert.Equal(2, _f.Mail.SentTo(email).Count());
		Assert.Equal(1, await Db(db => db.Users.CountAsync(u => u.Email == email)));
	}

	[Fact]
	public async Task ABlockedAddress_IsRefused_AndSendsNoMail()
	{
		var domain = Unique("spam") + ".test";
		await Db(async db => { db.BlockedEmailPatterns.Add(new BlockedEmailPattern { Pattern = "*@" + domain }); await db.SaveChangesAsync(); return 0; });
		var email = "someone@" + domain.ToUpperInvariant();

		var response = await NewSession().PostFormAsync("/Auth/Register", "/Auth/Register", ("Email", email));

		Assert.Contains(En("error_email_blocked"), await response.Content.ReadAsStringAsync());
		Assert.Empty(_f.Mail.SentTo(email));
		Assert.Null(await FindByEmail(email));
	}

	[Fact]
	public async Task WhenAdminsDisableRegistration_TheFormSaysSo_AndNothingIsCreated()
	{
		var email = Unique("late") + "@example.com";
		await SetRegistrationAsync(false);
		try
		{
			var page = await NewSession().GetHtmlAsync("/Auth/Register");
			var post = await NewSession().PostFormAsync("/Auth/Login", "/Auth/Register", ("Email", email));   // no form is rendered while disabled

			Assert.Contains(En("register_disabled"), page);
			Assert.Contains(En("register_disabled"), await post.Content.ReadAsStringAsync());
			Assert.Empty(_f.Mail.SentTo(email));
			Assert.Null(await FindByEmail(email));
		}
		finally { await SetRegistrationAsync(true); }
	}

	private Task SetRegistrationAsync(bool allowed) => _f.UseServicesAsync(async sp =>
	{
		var settings = sp.GetRequiredService<GitServer.Services.SiteSettingsService>();
		var s = await settings.GetAsync(); s.AllowRegistration = allowed; await settings.SaveAsync(s);
	});

	// ---- Completing it: the ways it can go wrong ----------------------------------------------------

	[Fact]
	public async Task ATamperedToken_IsRejected_AndNoAccountIsActivated()
	{
		var email = Unique("bad") + "@example.com";
		var link = await RegisterAsync(email);
		var (_, token) = ParseLink(link);
		var session = NewSession();

		var response = await session.PostFormAsync(link, "/Auth/CompleteRegistration", ("Email", email), ("Token", token[..^4] + "AAAA"),
			("Username", Unique("x")), ("Password", GitServerFactory.Password), ("ConfirmPassword", GitServerFactory.Password));

		Assert.Contains(En("error_invalid_or_expired_link"), await response.Content.ReadAsStringAsync());
		Assert.False((await FindByEmail(email))!.EmailConfirmed);
	}

	[Fact]
	public async Task MismatchedPasswords_AreRejected_AndTheLinkStillWorksAfterwards()
	{
		var email = Unique("typo") + "@example.com";
		var link = await RegisterAsync(email);

		var first = await CompleteAsync(NewSession(), link, Unique("typo"), "Passw0rd!", confirm: "Passw0rd?");
		var retry = await CompleteAsync(NewSession(), link, Unique("typo"));

		Assert.Contains(En("error_passwords_do_not_match"), await first.Content.ReadAsStringAsync());
		Assert.Equal(HttpStatusCode.Redirect, retry.StatusCode);
	}

	[Fact]
	public async Task AUsernameThatIsTaken_IsRejected_AndTheSameLinkCanBeRetriedWithAnotherName()
	{
		var taken = await _f.CreateUserAsync(Unique("taken"));
		var email = Unique("retry") + "@example.com";
		var link = await RegisterAsync(email);

		var refused = await CompleteAsync(NewSession(), link, taken.UserName!.ToUpperInvariant());
		var retry = await CompleteAsync(NewSession(), link, Unique("free"));

		Assert.Contains(En("error_username_taken"), await refused.Content.ReadAsStringAsync());
		Assert.Equal(HttpStatusCode.Redirect, retry.StatusCode);
		Assert.True((await FindByEmail(email))!.EmailConfirmed);
	}

	[Fact]
	public async Task AWeakPassword_IsRejected_AndTheSameLinkCanBeRetried()
	{
		var email = Unique("weak") + "@example.com";
		var link = await RegisterAsync(email);
		var username = Unique("weak");

		var weak = await CompleteAsync(NewSession(), link, username, "abc");
		var retry = await CompleteAsync(NewSession(), link, username);

		Assert.Equal(HttpStatusCode.OK, weak.StatusCode);
		Assert.Equal(HttpStatusCode.Redirect, retry.StatusCode);
		await NewSession().LoginAsync(username);
	}

	[Fact]
	public async Task ALinkCanOnlyBeUsedOnce()
	{
		var email = Unique("once") + "@example.com";
		var link = await RegisterAsync(email);
		await CompleteAsync(NewSession(), link, Unique("once"));

		var second = await CompleteAsync(NewSession(), link, Unique("again"));
		var page = await NewSession().GetHtmlAsync(link);

		Assert.Contains(En("error_invalid_or_expired_link"), await second.Content.ReadAsStringAsync());
		Assert.Contains(En("error_invalid_or_expired_link"), page);
	}

	// ---- Forgotten passwords ------------------------------------------------------------------------

	[Fact]
	public async Task ForgotPassword_MailsAResetLink_ThatSetsANewPassword()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var page = await NewSession().PostFormAsync("/Auth/ForgotPassword", "/Auth/ForgotPassword", ("Email", alice.Email!));
		var mail = Assert.Single(_f.Mail.SentTo(alice.Email!));
		var link = mail.FirstLinkPathAndQuery();
		var (email, token) = ParseLink(link);

		var reset = await NewSession().PostFormAsync(link, "/Auth/ResetPassword", ("Email", email), ("Token", token),
			("Password", "Brand-new-1"), ("ConfirmPassword", "Brand-new-1"));

		Assert.Contains(En("reset_password_email_sent"), await page.Content.ReadAsStringAsync());
		Assert.Equal(En("reset_password_email_subject"), mail.Subject);
		Assert.Contains(En("reset_password_success"), await reset.Content.ReadAsStringAsync());
		await NewSession().LoginAsync(alice.UserName!, "Brand-new-1");
		await Assert.ThrowsAsync<InvalidOperationException>(() => NewSession().LoginAsync(alice.UserName!, GitServerFactory.Password));
	}

	[Fact]
	public async Task ForgotPassword_GivesTheSameAnswer_ForUnknownAndUnconfirmedAddresses_AndMailsNobody()
	{
		var unknown = Unique("ghost") + "@example.com";
		var pending = Unique("pending") + "@example.com";
		await RegisterAsync(pending);
		var mailsBefore = _f.Mail.Sent.Count;

		var forUnknown = await NewSession().PostFormAsync("/Auth/ForgotPassword", "/Auth/ForgotPassword", ("Email", unknown));
		var forPending = await NewSession().PostFormAsync("/Auth/ForgotPassword", "/Auth/ForgotPassword", ("Email", pending));

		Assert.Contains(En("reset_password_email_sent"), await forUnknown.Content.ReadAsStringAsync());
		Assert.Contains(En("reset_password_email_sent"), await forPending.Content.ReadAsStringAsync());   // no account enumeration
		Assert.Equal(mailsBefore, _f.Mail.Sent.Count);
	}

	[Fact]
	public async Task AResetLink_WorksOnlyOnce_AndRejectsBadTokensAndMismatchedPasswords()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		await NewSession().PostFormAsync("/Auth/ForgotPassword", "/Auth/ForgotPassword", ("Email", alice.Email!));
		var link = _f.Mail.SentTo(alice.Email!).Single().FirstLinkPathAndQuery();
		var (email, token) = ParseLink(link);
		Task<HttpResponseMessage> Post(string t, string pw, string confirm) =>
			NewSession().PostFormAsync(link, "/Auth/ResetPassword", ("Email", email), ("Token", t), ("Password", pw), ("ConfirmPassword", confirm));

		var mismatch = await Post(token, "Brand-new-1", "Brand-new-2");
		var badToken = await Post("not-a-token", "Brand-new-1", "Brand-new-1");
		var ok = await Post(token, "Brand-new-1", "Brand-new-1");
		var reuse = await Post(token, "Another-2", "Another-2");

		Assert.Contains(En("error_passwords_do_not_match"), await mismatch.Content.ReadAsStringAsync());
		Assert.DoesNotContain(En("reset_password_success"), await badToken.Content.ReadAsStringAsync());
		Assert.Contains(En("reset_password_success"), await ok.Content.ReadAsStringAsync());
		Assert.DoesNotContain(En("reset_password_success"), await reuse.Content.ReadAsStringAsync());
		await NewSession().LoginAsync(alice.UserName!, "Brand-new-1");
	}

	// ---- Signing in and out ---------------------------------------------------------------------------

	[Fact]
	public async Task Login_AcceptsUsernameOrEmail_AnyCase_AndRecordsTheLastLogin()
	{
		var alice = await _f.CreateUserAsync(Unique("Alice"));

		await NewSession().LoginAsync(alice.UserName!.ToUpperInvariant());
		await NewSession().LoginAsync(alice.Email!);

		Assert.NotNull((await Db(db => db.Users.SingleAsync(u => u.Id == alice.Id))).LastLoginAt);
	}

	[Fact]
	public async Task Login_IsRefused_ForUnknownUsers_WrongPasswords_AndDisabledAccounts()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var bob = await _f.CreateUserAsync(Unique("bob"));
		await _f.UseServicesAsync(async sp =>
		{
			var users = sp.GetRequiredService<UserManager<AppUser>>();
			var tracked = (await users.FindByIdAsync(bob.Id))!;
			await users.SetLockoutEnabledAsync(tracked, true);
			await users.SetLockoutEndDateAsync(tracked, DateTimeOffset.MaxValue);
		});

		async Task<string> Try(string user, string pw) =>
			await (await NewSession().PostFormAsync("/Auth/Login", "/Auth/Login", ("Username", user), ("Password", pw))).Content.ReadAsStringAsync();

		Assert.Contains(En("error_invalid_credentials"), await Try("nobody-at-all", GitServerFactory.Password));
		Assert.Contains(En("error_invalid_credentials"), await Try(alice.UserName!, "wrong-password"));
		Assert.Contains(En("error_invalid_credentials"), await Try(bob.UserName!, GitServerFactory.Password));
	}

	[Fact]
	public async Task Login_ReturnsToTheRequestedLocalPage()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));

		var response = await NewSession().PostFormAsync("/Auth/Login", "/Auth/Login?returnUrl=%2Fexplore",
			("Username", alice.UserName!), ("Password", GitServerFactory.Password));

		Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
		Assert.Equal("/explore", Location(response));
	}

	[Theory]
	[InlineData("https://evil.example/phish")]
	[InlineData("//evil.example/phish")]
	[InlineData("/\\evil.example")]
	public async Task Login_NeverRedirectsToAnotherSite(string returnUrl)
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));

		var response = await NewSession().PostFormAsync("/Auth/Login", "/Auth/Login?returnUrl=" + Uri.EscapeDataString(returnUrl),
			("Username", alice.UserName!), ("Password", GitServerFactory.Password));

		Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
		Assert.Equal("/", Location(response));
	}

	[Fact]
	public async Task Login_AppliesThePreferredLanguageAndTimeZoneOfTheAccount()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		await Db(async db =>
		{
			var user = await db.Users.SingleAsync(u => u.Id == alice.Id);
			user.PreferredLanguage = "nl"; user.TimeZoneId = "Europe/Amsterdam";
			await db.SaveChangesAsync(); return 0;
		});

		var response = await NewSession().PostFormAsync("/Auth/Login", "/Auth/Login", ("Username", alice.UserName!), ("Password", GitServerFactory.Password));

		var cookies = string.Join(";", response.Headers.GetValues("Set-Cookie"));
		Assert.Contains("lang=nl", cookies);
		Assert.Contains("Europe%2FAmsterdam", cookies);
	}

	[Fact]
	public async Task Logout_EndsTheSession()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var session = await new WebSession(_f).LoginAsync(alice.UserName!);
		Assert.Equal(HttpStatusCode.OK, (await session.GetAsync("/User/Settings")).StatusCode);

		var logout = await session.PostFormAsync("/User/Settings", "/Auth/Logout");
		var after = await session.GetAsync("/User/Settings");

		Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
		Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
		Assert.StartsWith("/Auth/Login", after.Headers.Location!.AbsolutePath);
	}

	[Fact]
	public async Task TheSignedInPagesRequireALogin()
	{
		foreach (var path in new[] { "/User/Settings", "/User/Groups", "/Repo/New" })
		{
			var response = await NewSession().GetAsync(path);

			Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
			Assert.StartsWith("/Auth/Login", response.Headers.Location!.AbsolutePath);
		}
	}

	// ---- Language switch ------------------------------------------------------------------------------

	[Fact]
	public async Task SetLanguage_StoresTheChoiceInACookie_AndReturnsToTheLocalPage()
	{
		var response = await _f.NewClient().GetAsync("/set-language?lang=nl&returnUrl=%2Fexplore");

		Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
		Assert.Equal("/explore", response.Headers.Location!.OriginalString);
		Assert.Contains("lang=nl", string.Join(";", response.Headers.GetValues("Set-Cookie")));
	}

	[Theory]
	[InlineData("../../etc")]
	[InlineData("a;b")]
	[InlineData("waaaaaaaaaaaay-too-long")]
	public async Task SetLanguage_IgnoresValuesThatAreNotALanguageCode(string lang)
	{
		var response = await _f.NewClient().GetAsync("/set-language?lang=" + Uri.EscapeDataString(lang));

		Assert.False(response.Headers.Contains("Set-Cookie"));
		Assert.Equal("/", response.Headers.Location!.OriginalString);
	}

	[Theory]
	[InlineData("https://evil.example/phish")]
	[InlineData("//evil.example")]
	public async Task SetLanguage_NeverRedirectsToAnotherSite(string returnUrl)
	{
		var response = await _f.NewClient().GetAsync("/set-language?lang=nl&returnUrl=" + Uri.EscapeDataString(returnUrl));

		Assert.Equal("/", response.Headers.Location!.OriginalString);
	}
}

/// <summary>The first completed registration on a fresh installation becomes the administrator; later ones don't.
/// Needs its own empty database, hence its own factory.</summary>
public class FirstRegistrationTests : IClassFixture<GitServerFactory>
{
	private readonly GitServerFactory _f;

	public FirstRegistrationTests(GitServerFactory factory) => _f = factory;

	private async Task RegisterAndCompleteAsync(string email, string username)
	{
		var session = new WebSession(_f);
		await session.PostFormAsync("/Auth/Register", "/Auth/Register", ("Email", email));
		var link = _f.Mail.SentTo(email).Single().FirstLinkPathAndQuery();
		var query = System.Web.HttpUtility.ParseQueryString(new Uri("http://x" + link).Query);
		var response = await session.PostFormAsync(link, "/Auth/CompleteRegistration", ("Email", query["email"]!), ("Token", query["token"]!),
			("Username", username), ("DisplayName", ""), ("Password", GitServerFactory.Password), ("ConfirmPassword", GitServerFactory.Password));
		Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
	}

	[Fact]
	public async Task TheFirstAccountBecomesAdmin_TheSecondDoesNot()
	{
		await RegisterAndCompleteAsync("first@example.com", "firstuser");
		await RegisterAndCompleteAsync("second@example.com", "seconduser");

		var users = await _f.UseServicesAsync(sp => sp.GetRequiredService<AppDbContext>().Users.AsNoTracking().ToListAsync());
		Assert.True(users.Single(u => u.UserName == "firstuser").IsAdmin);
		Assert.False(users.Single(u => u.UserName == "seconduser").IsAdmin);
	}
}
