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

/// <summary>The terms page, agreeing to it while registering, the per-language home page, and users ending their own account.</summary>
public class TermsAndOwnAccountEndToEndTests : IClassFixture<GitServerFactory>
{
	private readonly GitServerFactory _f;

	public TermsAndOwnAccountEndToEndTests(GitServerFactory factory) => _f = factory;

	private static string Unique(string stem) => stem + Guid.NewGuid().ToString("N")[..6];
	private static Dictionary<string, string> Strings(string language) =>
		JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(TestPaths.LocalizationRoot, language, "strings.json")))!;
	public static IEnumerable<object[]> Languages() =>
		Directory.GetDirectories(TestPaths.LocalizationRoot).Select(d => new object[] { Path.GetFileName(d) });
	private static string Decode(string html) => WebUtility.HtmlDecode(html);

	private WebSession Anonymous() => new(_f);
	private async Task<WebSession> AsAsync(AppUser user) => await new WebSession(_f).LoginAsync(user.UserName!);
	private Task<T> Db<T>(Func<AppDbContext, Task<T>> q) => _f.UseServicesAsync(sp => q(sp.GetRequiredService<AppDbContext>()));
	private static bool IsLoginRedirect(HttpResponseMessage r) =>
		r.StatusCode == HttpStatusCode.Redirect && r.Headers.Location != null &&
		(r.Headers.Location.IsAbsoluteUri ? r.Headers.Location.AbsolutePath : r.Headers.Location.OriginalString)
			.StartsWith("/Auth/Login", StringComparison.OrdinalIgnoreCase);

	// ---- The terms page ----------------------------------------------------------------------------------

	[Theory]
	[MemberData(nameof(Languages))]
	public async Task TheTermsPage_IsOpenToEveryone_InEveryLanguage(string language)
	{
		var strings = Strings(language);

		var html = Decode(await Anonymous().GetHtmlAsync("/Terms", language));

		Assert.Contains(strings["terms_title"], html);
		Assert.Contains(strings["terms_s1_text"], html);
		Assert.Contains(strings["terms_s3_text"], html);            // deleting an account anonymizes it
	}

	[Fact]
	public async Task TheTermsPage_ShowsTheConfiguredContactAddress()
	{
		var html = await Anonymous().GetHtmlAsync("/Terms");

		Assert.Contains("href=\"mailto:privacy@example.test\"", html);
		Assert.Contains(Terms.CurrentVersion, html);
	}

	[Fact]
	public async Task EveryPage_LinksToTheTerms_InTheFooter()
	{
		var user = await _f.CreateUserAsync(Unique("reader"));

		foreach (var path in new[] { "/", "/Auth/Login", "/Terms" })
			Assert.Contains("href=\"/Terms\"", await Anonymous().GetHtmlAsync(path));
		Assert.Contains("href=\"/Terms\"", await (await AsAsync(user)).GetHtmlAsync("/"));
	}

	// ---- Agreeing while registering -----------------------------------------------------------------------

	[Fact]
	public async Task TheRegistrationForm_AsksToAgree_WithALinkToTheTerms()
	{
		var html = await Anonymous().GetHtmlAsync("/Auth/Register");

		Assert.Contains("name=\"AcceptTerms\"", html);
		Assert.Contains("href=\"/Terms\"", html);
	}

	[Fact]
	public async Task Registering_WithoutAgreeing_IsRefused_AndSendsNothing()
	{
		var email = Unique("noterms") + "@example.com";

		var response = await Anonymous().PostFormAsync("/Auth/Register", "/Auth/Register", ("Email", email));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Contains(Strings("en")["error_terms_required"], await response.Content.ReadAsStringAsync());
		Assert.Empty(_f.Mail.SentTo(email));
		Assert.Equal(0, await Db(d => d.Users.CountAsync(u => u.Email == email)));
	}

	[Fact]
	public async Task Registering_RecordsWhenAndWhichTermsWereAccepted()
	{
		var email = Unique("terms") + "@example.com";
		var before = DateTime.UtcNow.AddMinutes(-1);

		await Anonymous().PostFormAsync("/Auth/Register", "/Auth/Register", ("Email", email), ("AcceptTerms", "true"));

		var user = await Db(d => d.Users.SingleAsync(u => u.Email == email));
		Assert.Equal(Terms.CurrentVersion, user.TermsVersion);
		Assert.True(user.TermsAcceptedAt > before);
		Assert.Single(_f.Mail.SentTo(email));
	}

	// ---- The home page --------------------------------------------------------------------------------------

	[Theory]
	[MemberData(nameof(Languages))]
	public async Task TheHomePage_ExplainsWhatThisIs_ToVisitors_InTheirLanguage(string language)
	{
		var strings = Strings(language);

		var html = Decode(await Anonymous().GetHtmlAsync("/", language));

		Assert.Contains(strings["home_intro_title"], html);
		Assert.Contains(strings["home_intro_text"], html);
		Assert.Contains("href=\"/Auth/Register\"", html);
	}

	[Fact]
	public async Task TheIntroText_IsNotShown_ToSignedInUsers()
	{
		var user = await _f.CreateUserAsync(Unique("member"));

		var html = await (await AsAsync(user)).GetHtmlAsync("/");

		Assert.DoesNotContain(Strings("en")["home_intro_text"], html);
	}

	// ---- Ending your own account -----------------------------------------------------------------------------

	[Fact]
	public async Task DeletingYourOwnAccount_NeedsYourPassword()
	{
		var user = await _f.CreateUserAsync(Unique("stay"));
		var session = await AsAsync(user);

		var response = await session.PostFormAsync("/User/Settings", "/User/Settings?handler=DeleteAccount", ("CurrentPassword", "not-my-password"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Contains(Strings("en")["error_current_password_wrong"], await response.Content.ReadAsStringAsync());
		Assert.Equal(user.UserName, (await _f.UseServicesAsync(sp => sp.GetRequiredService<UserManager<AppUser>>().FindByIdAsync(user.Id)))!.UserName);
		await new WebSession(_f).LoginAsync(user.UserName!);
	}

	[Fact]
	public async Task DeletingYourOwnAccount_AnonymizesIt_SignsYouOut_AndKeepsWhatYouMade()
	{
		var user = await _f.CreateUserAsync(Unique("leaver"));
		await _f.SeedHistoryAsync(user, "kept");
		var oldName = user.UserName!;
		var session = await AsAsync(user);

		var response = await session.PostFormAsync("/User/Settings", "/User/Settings?handler=DeleteAccount", ("CurrentPassword", GitServerFactory.Password));

		Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
		Assert.True(IsLoginRedirect(await session.GetAsync("/User/Settings")));                   // the session is gone
		var anon = (await _f.UseServicesAsync(sp => sp.GetRequiredService<UserManager<AppUser>>().FindByIdAsync(user.Id)))!;
		Assert.Matches("^anonymous-[0-9]{4,5}$", anon.UserName);
		await Assert.ThrowsAsync<InvalidOperationException>(() => new WebSession(_f).LoginAsync(oldName));
		Assert.Contains("README.md", await Anonymous().GetHtmlAsync($"/{anon.UserName}/kept"));
	}
}

/// <summary>Kept apart because it needs a site whose only administrator is the user under test.</summary>
public class LastAdministratorTests : IClassFixture<GitServerFactory>
{
	private readonly GitServerFactory _f;

	public LastAdministratorTests(GitServerFactory factory) => _f = factory;

	[Fact]
	public async Task TheLastAdministrator_CannotDeleteTheirOwnAccount_UntilThereIsAnotherOne()
	{
		var only = await _f.CreateUserAsync("onlyadmin", isAdmin: true);
		var session = await new WebSession(_f).LoginAsync(only.UserName!);

		var refused = await session.PostFormAsync("/User/Settings", "/User/Settings?handler=DeleteAccount", ("CurrentPassword", GitServerFactory.Password));

		Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
		Assert.Contains(WebUtility.HtmlDecode("You are the last administrator"), WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync()));
		Assert.NotNull(await _f.UseServicesAsync(sp => sp.GetRequiredService<UserManager<AppUser>>().FindByNameAsync("onlyadmin")));

		await _f.CreateUserAsync("secondadmin", isAdmin: true);
		var allowed = await session.PostFormAsync("/User/Settings", "/User/Settings?handler=DeleteAccount", ("CurrentPassword", GitServerFactory.Password));

		Assert.Equal(HttpStatusCode.Redirect, allowed.StatusCode);
		Assert.Null(await _f.UseServicesAsync(sp => sp.GetRequiredService<UserManager<AppUser>>().FindByNameAsync("onlyadmin")));
	}
}
