using System.Net;
using System.Text.RegularExpressions;
using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using GitServer.Tests.TestSupport;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GitServer.Tests.TestSupport.GitWire;

namespace GitServer.Tests;

/// <summary>Personal access tokens: created once on the settings page, accepted instead of the password for git
/// over HTTPS, stored only as a hash, revocable, expiring, and never a way around a disabled account.</summary>
public class AccessTokensEndToEndTests : IClassFixture<GitServerFactory>
{
	private readonly GitServerFactory _f;

	public AccessTokensEndToEndTests(GitServerFactory factory) => _f = factory;

	private static string Unique(string stem) => stem + Guid.NewGuid().ToString("N")[..6];
	private Task<T> Db<T>(Func<AppDbContext, Task<T>> q) => _f.UseServicesAsync(sp => q(sp.GetRequiredService<AppDbContext>()));

	private async Task<string> CreateTokenAsync(AppUser user, string name = "laptop", string days = "90")
	{
		var session = await new WebSession(_f).LoginAsync(user.UserName!);
		var response = await session.PostFormAsync("/User/Settings", "/User/Settings?handler=CreateToken", ("TokenName", name), ("TokenValidDays", days));
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var match = Regex.Match(await response.Content.ReadAsStringAsync(), "<code id=\"new-token\">(gsp_[A-Za-z0-9_-]+)</code>");
		Assert.True(match.Success, "the new token is shown on the page");
		return match.Groups[1].Value;
	}

	private async Task<HttpStatusCode> DiscoverAsync(string user, string secret, string repoOwner, string repo, string service = "git-upload-pack")
	{
		var url = $"/git/{repoOwner}/{repo}.git/info/refs?service={service}";
		return (await _f.NewClient().SendAsync(Get(url, Basic(user, secret)))).StatusCode;
	}

	[Fact]
	public async Task ATokenCanBeUsedInsteadOfThePassword_ForAPrivateRepository()
	{
		var user = await _f.CreateUserAsync(Unique("dev"));
		await _f.CreateRepoAsync(user, "secret", isPrivate: true);

		var token = await CreateTokenAsync(user);

		Assert.Equal(HttpStatusCode.OK, await DiscoverAsync(user.UserName!, token, user.UserName!, "secret"));
		Assert.Equal(HttpStatusCode.OK, await DiscoverAsync(user.UserName!, GitServerFactory.Password, user.UserName!, "secret"));   // the password keeps working
	}

	[Fact]
	public async Task ATokenCanBeUsedToPush_ButOnlyWhereTheUserMayWrite()
	{
		var user = await _f.CreateUserAsync(Unique("pusher"));
		var other = await _f.CreateUserAsync(Unique("someone"));
		await _f.CreateRepoAsync(user, "target");
		await _f.CreateRepoAsync(other, "theirs");
		var token = await CreateTokenAsync(user);

		var own = await DiscoverAsync(user.UserName!, token, user.UserName!, "target", "git-receive-pack");
		var foreign = await DiscoverAsync(user.UserName!, token, other.UserName!, "theirs", "git-receive-pack");

		Assert.Equal(HttpStatusCode.OK, own);
		Assert.Equal(HttpStatusCode.Forbidden, foreign);
	}

	[Fact]
	public async Task OnlyAHashIsStored_NeverTheTokenItself()
	{
		var user = await _f.CreateUserAsync(Unique("hash"));

		var token = await CreateTokenAsync(user);

		var stored = await Db(d => d.AccessTokens.SingleAsync(t => t.UserId == user.Id));
		Assert.NotEqual(token, stored.TokenHash);
		Assert.DoesNotContain(token, stored.TokenHash);
		Assert.Equal(64, stored.TokenHash.Length);                                            // SHA-256 in hex
	}

	[Fact]
	public async Task TheTokenIsShownOnce_TheListOnlyHasItsName()
	{
		var user = await _f.CreateUserAsync(Unique("once"));
		var token = await CreateTokenAsync(user, "my-laptop");
		var session = await new WebSession(_f).LoginAsync(user.UserName!);

		var html = await session.GetHtmlAsync("/User/Settings");

		Assert.Contains("my-laptop", html);
		Assert.DoesNotContain(token, html);
	}

	[Fact]
	public async Task ATokenOfAnotherUser_IsRefused()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var bob = await _f.CreateUserAsync(Unique("bob"));
		await _f.CreateRepoAsync(bob, "private", isPrivate: true);
		var aliceToken = await CreateTokenAsync(alice);

		Assert.Equal(HttpStatusCode.Unauthorized, await DiscoverAsync(bob.UserName!, aliceToken, bob.UserName!, "private"));
		Assert.Equal(HttpStatusCode.Forbidden, await DiscoverAsync(alice.UserName!, aliceToken, bob.UserName!, "private"));   // a valid token, but alice may not use bobs private repo
	}

	[Fact]
	public async Task ARevokedToken_StopsWorking_AndOthersKeepWorking()
	{
		var user = await _f.CreateUserAsync(Unique("revoke"));
		await _f.CreateRepoAsync(user, "secret", isPrivate: true);
		var doomed = await CreateTokenAsync(user, "doomed");
		var kept = await CreateTokenAsync(user, "kept");
		var id = await Db(d => d.AccessTokens.Where(t => t.UserId == user.Id && t.Name == "doomed").Select(t => t.Id).SingleAsync());
		var session = await new WebSession(_f).LoginAsync(user.UserName!);

		await session.PostFormAsync("/User/Settings", $"/User/Settings?handler=RevokeToken&id={id}");

		Assert.Equal(HttpStatusCode.Unauthorized, await DiscoverAsync(user.UserName!, doomed, user.UserName!, "secret"));
		Assert.Equal(HttpStatusCode.OK, await DiscoverAsync(user.UserName!, kept, user.UserName!, "secret"));
	}

	[Fact]
	public async Task YouCannotRevokeSomeoneElsesToken()
	{
		var owner = await _f.CreateUserAsync(Unique("owner"));
		var other = await _f.CreateUserAsync(Unique("other"));
		await CreateTokenAsync(owner);
		var id = await Db(d => d.AccessTokens.Where(t => t.UserId == owner.Id).Select(t => t.Id).SingleAsync());

		await (await new WebSession(_f).LoginAsync(other.UserName!)).PostFormAsync("/User/Settings", $"/User/Settings?handler=RevokeToken&id={id}");

		Assert.Equal(1, await Db(d => d.AccessTokens.CountAsync(t => t.Id == id)));
	}

	[Fact]
	public async Task AnExpiredToken_IsRefused()
	{
		var user = await _f.CreateUserAsync(Unique("expire"));
		await _f.CreateRepoAsync(user, "secret", isPrivate: true);
		var token = await CreateTokenAsync(user, "short", "30");
		await Db(async d =>
		{
			var t = await d.AccessTokens.SingleAsync(x => x.UserId == user.Id);
			t.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
			await d.SaveChangesAsync();
			return 0;
		});

		Assert.Equal(HttpStatusCode.Unauthorized, await DiscoverAsync(user.UserName!, token, user.UserName!, "secret"));
	}

	[Fact]
	public async Task ATokenThatNeverExpires_IsPossible()
	{
		var user = await _f.CreateUserAsync(Unique("forever"));

		await CreateTokenAsync(user, "forever", days: "");

		Assert.Null((await Db(d => d.AccessTokens.SingleAsync(t => t.UserId == user.Id))).ExpiresAt);
	}

	[Fact]
	public async Task UsingATokenIsRecorded()
	{
		var user = await _f.CreateUserAsync(Unique("used"));
		await _f.CreateRepoAsync(user, "secret", isPrivate: true);
		var token = await CreateTokenAsync(user);
		Assert.Null((await Db(d => d.AccessTokens.SingleAsync(t => t.UserId == user.Id))).LastUsedAt);

		await DiscoverAsync(user.UserName!, token, user.UserName!, "secret");

		Assert.NotNull((await Db(d => d.AccessTokens.SingleAsync(t => t.UserId == user.Id))).LastUsedAt);
	}

	[Fact]
	public async Task ADisabledAccount_CannotUseItsTokens()
	{
		var user = await _f.CreateUserAsync(Unique("banned"));
		await _f.CreateRepoAsync(user, "secret", isPrivate: true);
		var token = await CreateTokenAsync(user);
		await _f.UseServicesAsync(async sp =>
		{
			var users = sp.GetRequiredService<UserManager<AppUser>>();
			var u = (await users.FindByIdAsync(user.Id))!;
			await users.SetLockoutEnabledAsync(u, true);
			await users.SetLockoutEndDateAsync(u, DateTimeOffset.MaxValue);
		});

		Assert.Equal(HttpStatusCode.Unauthorized, await DiscoverAsync(user.UserName!, token, user.UserName!, "secret"));
	}

	[Fact]
	public async Task WrongTokens_DoNotLockTheAccount_BecauseTheyCannotBeGuessed()
	{
		var user = await _f.CreateUserAsync(Unique("noisy"));
		await _f.CreateRepoAsync(user, "secret", isPrivate: true);

		for (var i = 0; i < 8; i++)
			Assert.Equal(HttpStatusCode.Unauthorized, await DiscoverAsync(user.UserName!, "gsp_" + Guid.NewGuid().ToString("N"), user.UserName!, "secret"));

		await new WebSession(_f).LoginAsync(user.UserName!);
	}

	[Fact]
	public async Task ATokenNeedsAName()
	{
		var user = await _f.CreateUserAsync(Unique("noname"));
		var session = await new WebSession(_f).LoginAsync(user.UserName!);

		await session.PostFormAsync("/User/Settings", "/User/Settings?handler=CreateToken", ("TokenName", "   "), ("TokenValidDays", "90"));

		Assert.Equal(0, await Db(d => d.AccessTokens.CountAsync(t => t.UserId == user.Id)));
	}

	[Fact]
	public async Task DeletingTheAccount_RevokesEveryToken()
	{
		var user = await _f.CreateUserAsync(Unique("leaving"));
		await _f.CreateRepoAsync(user, "secret", isPrivate: true);
		var token = await CreateTokenAsync(user);
		var session = await new WebSession(_f).LoginAsync(user.UserName!);

		await session.PostFormAsync("/User/Settings", "/User/Settings?handler=DeleteAccount", ("CurrentPassword", GitServerFactory.Password));

		Assert.Equal(0, await Db(d => d.AccessTokens.CountAsync(t => t.UserId == user.Id)));
		Assert.NotEqual(HttpStatusCode.OK, await DiscoverAsync(user.UserName!, token, user.UserName!, "secret"));
	}
}
