using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using GitServer.Tests.TestSupport;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GitServer.Tests;

/// <summary>API keys: created and managed from the account page's API, and accepted in the X-Api-Key header
/// on every API controller, acting as the key's owner.</summary>
public class ApiKeysEndToEndTests : IClassFixture<GitServerFactory>
{
	private const string ApiKeysPage = "/dashboard/User/ApiKeys";
	private const string KeysApi = "/api/user/api-keys";

	private readonly GitServerFactory _f;

	public ApiKeysEndToEndTests(GitServerFactory factory) => _f = factory;

	private static string Unique(string stem) => stem + Guid.NewGuid().ToString("N")[..6];
	private async Task<WebSession> AsAsync(AppUser user) => await new WebSession(_f).LoginAsync(user.UserName!);
	private Task<T> Db<T>(Func<AppDbContext, Task<T>> q) => _f.UseServicesAsync(sp => q(sp.GetRequiredService<AppDbContext>()));

	private async Task<(int Id, string Key)> CreateKeyAsync(WebSession session, string name = "ci")
	{
		var response = await session.SendJsonAsync(ApiKeysPage, HttpMethod.Post, KeysApi, new { name });
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
		return (root.GetProperty("apiKey").GetProperty("id").GetInt32(), root.GetProperty("key").GetString()!);
	}

	private HttpClient WithKey(string key)
	{
		var client = _f.NewClient();
		client.DefaultRequestHeaders.Add("X-Api-Key", key);
		return client;
	}

	private static string Repos(AppUser user) => $"/api/users/{user.UserName}/repos";

	private static async Task<string[]> RepoNamesAsync(HttpResponseMessage response) =>
		JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("repos")
			.EnumerateArray().Select(r => r.GetProperty("displayName").GetString()!).ToArray();

	[Fact]
	public async Task AKey_IsShownOnce_LooksLikeAKey_AndExpiresAfterTheDefaultLifetime()
	{
		var user = await _f.CreateUserAsync(Unique("alice"));
		var session = await AsAsync(user);

		var (id, key) = await CreateKeyAsync(session);

		Assert.StartsWith("gsk_", key);
		var stored = await Db(d => d.ApiKeys.SingleAsync(k => k.Id == id));
		Assert.NotEqual(key, stored.KeyHash);                                            // only a hash is stored
		Assert.True(stored.IsEnabled);
		Assert.InRange((stored.ExpiresAt - DateTime.UtcNow).TotalDays, 89, 91);          // 3 months by default
		var list = await session.GetHtmlAsync(KeysApi);
		Assert.DoesNotContain(key, list);
		Assert.Contains(stored.KeyPrefix, list);
	}

	[Fact]
	public async Task TheLifetimeOfNewKeys_FollowsTheAdminSetting()
	{
		var admin = await _f.CreateUserAsync(Unique("boss"), isAdmin: true);
		var session = await AsAsync(admin);

		await session.PostFormAsync("/dashboard/Admin/Settings", "/dashboard/Admin/Settings",
			("AllowRegistration", "true"), ("AllowUserRepoCreation", "true"), ("AllowPushToCreateRepositories", "true"),
			("ShowCommitAuthorAvatar", "true"), ("ApiKeyLifetimeDays", "10"));
		var (id, _) = await CreateKeyAsync(session);

		var stored = await Db(d => d.ApiKeys.SingleAsync(k => k.Id == id));
		Assert.InRange((stored.ExpiresAt - DateTime.UtcNow).TotalDays, 9, 11);

		await _f.UseServicesAsync(async sp =>
		{
			var settings = sp.GetRequiredService<SiteSettingsService>();
			var current = await settings.GetAsync();
			current.ApiKeyLifetimeDays = 90;
			await settings.SaveAsync(current);
		});
	}

	[Fact]
	public async Task AKey_ActsAsItsOwner_OnTheApiControllers()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		await _f.CreateRepoAsync(alice, "open");
		await _f.CreateRepoAsync(alice, "secret", isPrivate: true);
		var (_, key) = await CreateKeyAsync(await AsAsync(alice));

		var anonymous = await RepoNamesAsync(await _f.NewClient().GetAsync(Repos(alice)));
		var withKey = await RepoNamesAsync(await WithKey(key).GetAsync(Repos(alice)));

		Assert.Equal(new[] { "open" }, anonymous);
		Assert.Equal(new[] { "open", "secret" }, withKey.OrderBy(n => n).ToArray());     // the private repo is visible: it is alice
	}

	[Fact]
	public async Task AKey_CanBeDisabledAndEnabledAgain_AndDeleted()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var session = await AsAsync(alice);
		var (id, key) = await CreateKeyAsync(session);
		var client = WithKey(key);
		Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Repos(alice))).StatusCode);

		await session.SendJsonAsync(ApiKeysPage, HttpMethod.Put, $"{KeysApi}/{id}/enabled", new { enabled = false });
		Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Repos(alice))).StatusCode);

		await session.SendJsonAsync(ApiKeysPage, HttpMethod.Put, $"{KeysApi}/{id}/enabled", new { enabled = true });
		Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Repos(alice))).StatusCode);

		Assert.Equal(HttpStatusCode.NoContent, (await session.SendJsonAsync(ApiKeysPage, HttpMethod.Delete, $"{KeysApi}/{id}")).StatusCode);
		Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Repos(alice))).StatusCode);
	}

	[Fact]
	public async Task AnExpiredKey_IsRefused()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var (id, key) = await CreateKeyAsync(await AsAsync(alice));

		await Db(async d =>
		{
			(await d.ApiKeys.SingleAsync(k => k.Id == id)).ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
			return await d.SaveChangesAsync();
		});

		Assert.Equal(HttpStatusCode.Unauthorized, (await WithKey(key).GetAsync(Repos(alice))).StatusCode);
	}

	[Fact]
	public async Task AnUnknownKey_IsRefused_EvenNextToAValidSession()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var session = await AsAsync(alice);
		session.Client.DefaultRequestHeaders.Add("X-Api-Key", "gsk_notarealkey");

		Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync(Repos(alice))).StatusCode);   // no fallback to the cookie
	}

	[Fact]
	public async Task TheKeysOfADisabledUser_StopWorking()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var (_, key) = await CreateKeyAsync(await AsAsync(alice));

		await _f.UseServicesAsync(async sp =>
		{
			var users = sp.GetRequiredService<UserManager<AppUser>>();
			var user = (await users.FindByIdAsync(alice.Id))!;
			await users.SetLockoutEnabledAsync(user, true);
			await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
		});

		Assert.Equal(HttpStatusCode.Unauthorized, (await WithKey(key).GetAsync(Repos(alice))).StatusCode);
	}

	[Fact]
	public async Task AKey_CannotManageKeys()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var (id, key) = await CreateKeyAsync(await AsAsync(alice));
		var client = WithKey(key);

		Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(KeysApi)).StatusCode);
		Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(KeysApi, new { name = "more" })).StatusCode);
		Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"{KeysApi}/{id}")).StatusCode);
	}

	[Fact]
	public async Task OnlyTheOwnerOfAKey_CanSeeOrChangeIt()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var mallory = await _f.CreateUserAsync(Unique("mallory"));
		var (id, key) = await CreateKeyAsync(await AsAsync(alice));
		var malloryPage = await AsAsync(mallory);

		Assert.DoesNotContain(key[..8], await malloryPage.GetHtmlAsync(KeysApi));
		Assert.Equal(HttpStatusCode.NotFound, (await malloryPage.SendJsonAsync(ApiKeysPage, HttpMethod.Delete, $"{KeysApi}/{id}")).StatusCode);
		Assert.Equal(HttpStatusCode.OK, (await WithKey(key).GetAsync(Repos(alice))).StatusCode);
	}

	[Fact]
	public async Task AdminEndpoints_AcceptAnAdminsKey_WithoutAnAntiforgeryToken_ButNotAnOrdinaryUsersKey()
	{
		var admin = await _f.CreateUserAsync(Unique("boss"), isAdmin: true);
		var ordinary = await _f.CreateUserAsync(Unique("plain"));
		var (_, adminKey) = await CreateKeyAsync(await AsAsync(admin));
		var (_, plainKey) = await CreateKeyAsync(await AsAsync(ordinary));
		var stem = Unique("k").ToLowerInvariant();
		var pattern = stem + "*";

		var asAdmin = await WithKey(adminKey).PostAsJsonAsync("/api/admin/reserved-names", new { pattern });
		var asOrdinary = await WithKey(plainKey).PostAsJsonAsync("/api/admin/reserved-names", new { pattern = pattern + "x" });
		var noAuth = await _f.NewClient().PostAsJsonAsync("/api/admin/reserved-names", new { pattern = pattern + "y" });

		Assert.Equal(HttpStatusCode.OK, asAdmin.StatusCode);
		Assert.Equal(HttpStatusCode.Forbidden, asOrdinary.StatusCode);
		Assert.True(noAuth.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest, noAuth.StatusCode.ToString());
		Assert.Equal(1, await Db(d => d.ReservedNamePatterns.CountAsync(p => p.Pattern.StartsWith(stem))));
	}

	[Fact]
	public async Task CookieCalls_StillNeedTheAntiforgeryToken()
	{
		var admin = await _f.CreateUserAsync(Unique("boss"), isAdmin: true);
		var session = await AsAsync(admin);

		var response = await session.Client.PostAsJsonAsync("/api/admin/reserved-names", new { pattern = Unique("nope") });

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}
}
