using System.Net;
using System.Text.Json;
using GitServer.Models;
using GitServer.Tests.TestSupport;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GitServer.Tests.TestSupport.GitWire;

namespace GitServer.Tests;

/// <summary>Guessing passwords must stop working: after five wrong ones the account is locked for a while, on the
/// web login and on git over HTTPS alike, and a lockout can be lifted by an administrator.</summary>
public class LoginLockoutEndToEndTests : IClassFixture<GitServerFactory>
{
	private const int Attempts = 5;
	private readonly GitServerFactory _f;

	public LoginLockoutEndToEndTests(GitServerFactory factory) => _f = factory;

	private static string Unique(string stem) => stem + Guid.NewGuid().ToString("N")[..6];
	private static string En(string key) =>
		JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(TestPaths.LocalizationRoot, "en", "strings.json")))![key];

	private static Task<HttpResponseMessage> TryLogin(WebSession s, string user, string password) =>
		s.PostFormAsync("/Auth/Login", "/Auth/Login", ("Username", user), ("Password", password));

	[Fact]
	public async Task AfterTooManyWrongPasswords_TheRightOneNoLongerWorks_AndTheUserIsToldWhy()
	{
		var user = await _f.CreateUserAsync(Unique("victim"));
		var session = new WebSession(_f);

		for (var i = 0; i < Attempts; i++)
			Assert.Equal(HttpStatusCode.OK, (await TryLogin(session, user.UserName!, "wrong-password")).StatusCode);
		var locked = await TryLogin(session, user.UserName!, GitServerFactory.Password);

		Assert.Equal(HttpStatusCode.OK, locked.StatusCode);                                   // no redirect: not signed in
		Assert.Contains(En("error_account_locked"), await locked.Content.ReadAsStringAsync());
	}

	[Fact]
	public async Task WrongPasswordsBelowTheLimit_DoNotLock_AndASuccessfulLoginResetsTheCount()
	{
		var user = await _f.CreateUserAsync(Unique("careful"));

		for (var round = 0; round < 3; round++)
		{
			for (var i = 0; i < Attempts - 1; i++) await TryLogin(new WebSession(_f), user.UserName!, "wrong-password");
			await new WebSession(_f).LoginAsync(user.UserName!);
		}
	}

	[Fact]
	public async Task GitOverHttps_CountsWrongPasswordsToo()
	{
		var user = await _f.CreateUserAsync(Unique("gituser"));
		await _f.CreateRepoAsync(user, "secret", isPrivate: true);
		var url = $"/git/{user.UserName}/secret.git/info/refs?service=git-upload-pack";

		for (var i = 0; i < Attempts; i++)
			Assert.Equal(HttpStatusCode.Unauthorized, (await _f.NewClient().SendAsync(Get(url, Basic(user.UserName!, "wrong-password")))).StatusCode);
		var withRightPassword = await _f.NewClient().SendAsync(Get(url, Basic(user.UserName!, GitServerFactory.Password)));

		Assert.Equal(HttpStatusCode.Unauthorized, withRightPassword.StatusCode);
	}

	[Fact]
	public async Task TheLockoutIsSharedBetweenWebAndGit()
	{
		var user = await _f.CreateUserAsync(Unique("shared"));
		await _f.CreateRepoAsync(user, "secret", isPrivate: true);
		var url = $"/git/{user.UserName}/secret.git/info/refs?service=git-upload-pack";

		for (var i = 0; i < Attempts; i++) await _f.NewClient().SendAsync(Get(url, Basic(user.UserName!, "wrong-password")));

		await Assert.ThrowsAsync<InvalidOperationException>(() => new WebSession(_f).LoginAsync(user.UserName!));
	}

	[Fact]
	public async Task AnAdministrator_CanLiftALockout()
	{
		var admin = await _f.CreateUserAsync(Unique("boss"), isAdmin: true);
		var user = await _f.CreateUserAsync(Unique("locked"));
		for (var i = 0; i < Attempts; i++) await TryLogin(new WebSession(_f), user.UserName!, "wrong-password");
		await Assert.ThrowsAsync<InvalidOperationException>(() => new WebSession(_f).LoginAsync(user.UserName!));

		await (await new WebSession(_f).LoginAsync(admin.UserName!)).PostFormAsync("/Admin/Users", "/Admin/Users?handler=Save",
			("userId", user.Id), ("userName", user.UserName!), ("displayName", ""), ("email", user.Email!), ("isDisabled", "false"), ("isAdmin", "false"));

		await new WebSession(_f).LoginAsync(user.UserName!);
	}

	[Fact]
	public async Task ALockedAccount_IsNotShownAsDisabled_ButADisabledOneStillIs()
	{
		var user = await _f.CreateUserAsync(Unique("temp"));
		for (var i = 0; i < Attempts; i++) await TryLogin(new WebSession(_f), user.UserName!, "wrong-password");

		var locked = await _f.UseServicesAsync(sp => sp.GetRequiredService<UserManager<AppUser>>().FindByIdAsync(user.Id));

		Assert.True(locked!.IsTemporarilyLocked);
		Assert.False(locked.IsDisabled);
	}
}
