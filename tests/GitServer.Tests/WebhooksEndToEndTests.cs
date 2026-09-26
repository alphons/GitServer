using System.Net;
using System.Text.Json;
using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using GitServer.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GitServer.Tests.TestSupport.GitWire;

namespace GitServer.Tests;

/// <summary>Webhooks: managed by repository administrators, fired by pushes and issue activity, signed, retried,
/// and kept away from private addresses unless the admin allows them.</summary>
public class WebhooksEndToEndTests : IClassFixture<GitServerFactory>, IAsyncLifetime
{
	private const string ReceivePackType = "application/x-git-receive-pack-request";
	private readonly GitServerFactory _f;
	private WebhookReceiver _receiver = null!;

	public WebhooksEndToEndTests(GitServerFactory factory) => _f = factory;

	public async Task InitializeAsync()
	{
		_receiver = await WebhookReceiver.StartAsync();
		await SetPrivateNetworksAllowedAsync(true);   // the receiver runs on 127.0.0.1
	}

	public async Task DisposeAsync() => await _receiver.DisposeAsync();

	private static string Unique(string stem) => stem + Guid.NewGuid().ToString("N")[..6];
	private Task<T> Db<T>(Func<AppDbContext, Task<T>> action) => _f.UseServicesAsync(sp => action(sp.GetRequiredService<AppDbContext>()));
	private async Task<WebSession> AsAsync(AppUser user) => await new WebSession(_f).LoginAsync(user.UserName!);
	private static string Api(AppUser owner, string repo) => $"/api/repos/{owner.UserName}/{repo}/webhooks";
	private static string Page(AppUser owner, string repo) => $"/{owner.UserName}/{repo}/webhooks";

	private Task SetPrivateNetworksAllowedAsync(bool allowed) =>
		_f.UseServicesAsync(async sp =>
		{
			var settings = sp.GetRequiredService<SiteSettingsService>();
			var current = await settings.GetAsync();
			current.AllowWebhooksToPrivateNetworks = allowed;
			await settings.SaveAsync(current);
		});

	private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
		JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

	private async Task<(WebSession Session, int HookId)> AddHookAsync(AppUser owner, string repo, string path, object? events = null, string secret = "")
	{
		var session = await AsAsync(owner);
		var response = await session.SendJsonAsync(Page(owner, repo), HttpMethod.Post, Api(owner, repo),
			new { url = _receiver.BaseUrl + path, secret, events = events ?? new[] { "push" } });
		Assert.Equal(HttpStatusCode.Created, response.StatusCode);
		return (session, (await JsonAsync(response)).GetProperty("id").GetInt32());
	}

	private async Task<List<WebhookDelivery>> WaitForDeliveriesAsync(int hookId, int count, string? eventName = null)
	{
		var deadline = DateTime.UtcNow.AddSeconds(15);
		while (true)
		{
			var deliveries = await Db(d => d.WebhookDeliveries.Where(x => x.WebhookId == hookId && (eventName == null || x.Event == eventName)).OrderBy(x => x.Id).ToListAsync());
			if (deliveries.Count >= count) return deliveries;
			if (DateTime.UtcNow > deadline) throw new TimeoutException($"Expected {count} deliveries, got {deliveries.Count}.");
			await Task.Delay(50);
		}
	}

	[Fact]
	public async Task AddingAWebhook_SendsASignedPing()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		await _f.CreateRepoAsync(alice, "pinged");
		var path = "/" + Unique("ping");

		var (_, hookId) = await AddHookAsync(alice, "pinged", path, secret: "s3cret");

		var ping = (await _receiver.WaitForAsync(path, eventName: "ping")).Single();
		Assert.Equal(WebhookDispatcher.Sign("s3cret", ping.Body), ping.Headers["X-Hub-Signature-256"]);
		Assert.StartsWith("GitServer-Hookshot", ping.Headers["User-Agent"]);
		Assert.Equal("application/json; charset=utf-8", ping.Headers["Content-Type"]);
		var body = JsonDocument.Parse(ping.Body).RootElement;
		Assert.Equal(hookId, body.GetProperty("hook_id").GetInt32());
		Assert.Equal($"{alice.UserName}/pinged", body.GetProperty("repository").GetProperty("full_name").GetString());
		Assert.True((await WaitForDeliveriesAsync(hookId, 1)).Single().Succeeded);
		Assert.True(await Db(d => d.AuditEntries.AnyAsync(a => a.Action == "webhook.create" && a.ActorUserId == alice.Id)));
	}

	[Fact]
	public async Task APush_IsDelivered_WithTheRefAndItsCommits()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		await _f.CreateRepoAsync(alice, "pushed");
		var path = "/" + Unique("push");
		await AddHookAsync(alice, "pushed", path);
		using var local = new LocalGit();
		local.Commit("a.txt", "one\n", "First commit");
		var sha = local.Commit("a.txt", "two\n", "Second commit");

		var client = _f.NewClient();
		var push = await client.SendAsync(Post($"/git/{alice.UserName}/pushed.git/git-receive-pack", ReceivePackType,
			ReceivePackRequest(WebhookService.ZeroSha, sha, "refs/heads/main", local.PackFor(sha)), Basic(alice.UserName!, GitServerFactory.Password)));

		Assert.Equal(HttpStatusCode.OK, push.StatusCode);
		var delivered = (await _receiver.WaitForAsync(path, eventName: "push")).Single();
		var body = JsonDocument.Parse(delivered.Body).RootElement;
		Assert.Equal("refs/heads/main", body.GetProperty("ref").GetString());
		Assert.Equal(WebhookService.ZeroSha, body.GetProperty("before").GetString());
		Assert.Equal(sha, body.GetProperty("after").GetString());
		Assert.True(body.GetProperty("created").GetBoolean());
		Assert.Equal(new[] { "First commit", "Second commit" },
			body.GetProperty("commits").EnumerateArray().Select(c => c.GetProperty("message").GetString()));
		Assert.Equal(sha, body.GetProperty("head_commit").GetProperty("id").GetString());
		Assert.Equal(alice.UserName, body.GetProperty("pusher").GetProperty("login").GetString());
		Assert.EndsWith($"/git/{alice.UserName}/pushed.git", body.GetProperty("repository").GetProperty("clone_url").GetString());
	}

	[Fact]
	public async Task IssueActivity_IsDelivered_OnlyToHooksThatAskForIt()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		await _f.CreateRepoAsync(alice, "tracked");
		var issuesPath = "/" + Unique("issues");
		var pushOnlyPath = "/" + Unique("pushonly");
		var (session, _) = await AddHookAsync(alice, "tracked", issuesPath, new[] { "issues", "issue_comment" });
		await AddHookAsync(alice, "tracked", pushOnlyPath, new[] { "push" });

		var open = await session.PostFormAsync($"/{alice.UserName}/tracked/issues/new", $"/{alice.UserName}/tracked/issues/new",
			("Title", "Broken build"), ("Body", "It fails."));
		var issueUrl = WebSession.Location(open)!;
		await session.PostFormAsync(issueUrl, issueUrl + "?handler=Comment", ("CommentBody", "Looking into it"));
		await session.PostFormAsync(issueUrl, issueUrl + "?handler=Close");

		var issues = await _receiver.WaitForAsync(issuesPath, 2, "issues");
		var comment = (await _receiver.WaitForAsync(issuesPath, 1, "issue_comment")).Single();
		Assert.Equal(new[] { "opened", "closed" },
			issues.Select(r => JsonDocument.Parse(r.Body).RootElement.GetProperty("action").GetString()).Order().Reverse());
		var commentBody = JsonDocument.Parse(comment.Body).RootElement;
		Assert.Equal("Looking into it", commentBody.GetProperty("comment").GetProperty("body").GetString());
		Assert.Equal("Broken build", commentBody.GetProperty("issue").GetProperty("title").GetString());
		Assert.DoesNotContain(_receiver.All(pushOnlyPath), r => r.Headers["X-GitServer-Event"] != "ping");
	}

	[Fact]
	public async Task AFailingReceiver_IsRetried_AndEveryAttemptIsRecorded()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		await _f.CreateRepoAsync(alice, "flaky");
		var path = "/" + Unique("flaky");
		_receiver.StatusCode = 500;
		try
		{
			var (_, hookId) = await AddHookAsync(alice, "flaky", path);

			var deliveries = await WaitForDeliveriesAsync(hookId, 3);

			Assert.Equal(new[] { 1, 2, 3 }, deliveries.Select(d => d.Attempt));
			Assert.All(deliveries, d => Assert.Equal(500, d.StatusCode));
			Assert.Single(deliveries.Select(d => d.DeliveryId).Distinct());   // one event, three attempts
		}
		finally
		{
			_receiver.StatusCode = 200;
		}
	}

	[Fact]
	public async Task PrivateAddresses_AreRefused_UnlessTheAdminAllowsThem()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		await _f.CreateRepoAsync(alice, "guarded");
		var path = "/" + Unique("guarded");
		await SetPrivateNetworksAllowedAsync(false);
		try
		{
			var (_, hookId) = await AddHookAsync(alice, "guarded", path);

			var deliveries = await WaitForDeliveriesAsync(hookId, 3);

			Assert.All(deliveries, d => Assert.Null(d.StatusCode));
			Assert.Contains("private network address", deliveries[0].Error);
			Assert.Empty(_receiver.All(path));
		}
		finally
		{
			await SetPrivateNetworksAllowedAsync(true);
		}
	}

	[Fact]
	public async Task OnlyRepositoryAdministrators_SeeAndManageWebhooks()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var bob = await _f.CreateUserAsync(Unique("bob"));
		await _f.CreateRepoAsync(alice, "mine");
		var (_, hookId) = await AddHookAsync(alice, "mine", "/" + Unique("mine"));
		var bobSession = await AsAsync(bob);

		var list = await bobSession.GetAsync(Api(alice, "mine"));
		var delete = await bobSession.SendJsonAsync("/dashboard/Repo/New", HttpMethod.Post, $"{Api(alice, "mine")}/{hookId}/delete");
		var page = await bobSession.GetAsync(Page(alice, "mine"));

		Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
		Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
		Assert.NotEqual(HttpStatusCode.OK, page.StatusCode);
		Assert.True(await Db(d => d.Webhooks.AnyAsync(w => w.Id == hookId)));
	}

	[Theory]
	[InlineData("not a url", "push")]
	[InlineData("ftp://example.com/hook", "push")]
	[InlineData("https://user:pw@example.com/hook", "push")]
	[InlineData("https://example.com/hook", "deploy")]
	public async Task InvalidWebhooks_AreRefused(string url, string eventName)
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		await _f.CreateRepoAsync(alice, "strict");
		var session = await AsAsync(alice);

		var response = await session.SendJsonAsync(Page(alice, "strict"), HttpMethod.Post, Api(alice, "strict"), new { url, events = new[] { eventName } });

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		Assert.False(await Db(d => d.Webhooks.AnyAsync(w => w.Repository.Name == "strict" && w.Repository.OwnerId == alice.Id)));
	}

	[Fact]
	public async Task TheWebhooksPage_AddsEditsAndDeletesAHook_AndKeepsTheSecretUnlessChanged()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		await _f.CreateRepoAsync(alice, "paged");
		var session = await AsAsync(alice);
		var url = _receiver.BaseUrl + "/" + Unique("paged");

		var create = await session.PostFormAsync(Page(alice, "paged"), Page(alice, "paged") + "?handler=Create",
			("Url", url), ("Secret", "keep-me"), ("EventPush", "true"), ("EventIssues", "true"), ("Active", "true"));
		Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
		var hook = await Db(d => d.Webhooks.SingleAsync(w => w.Url == url));
		Assert.Equal(WebhookEvents.Push | WebhookEvents.Issues, hook.Events);
		Assert.Contains(url, await session.GetHtmlAsync(Page(alice, "paged")));

		var update = await session.PostFormAsync(Page(alice, "paged"), Page(alice, "paged") + $"?handler=Update&id={hook.Id}",
			("Url", url), ("Secret", ""), ("EventIssueComment", "true"), ("Active", "false"));
		Assert.Equal(HttpStatusCode.Redirect, update.StatusCode);
		var updated = await Db(d => d.Webhooks.SingleAsync(w => w.Id == hook.Id));
		Assert.Equal("keep-me", updated.Secret);
		Assert.Equal(WebhookEvents.IssueComment, updated.Events);
		Assert.False(updated.IsActive);

		var delete = await session.PostFormAsync(Page(alice, "paged"), Page(alice, "paged") + $"?handler=Delete&id={hook.Id}");
		Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);
		Assert.False(await Db(d => d.Webhooks.AnyAsync(w => w.Id == hook.Id)));
	}
}

public class WebhookUnitTests
{
	[Theory]
	[InlineData("127.0.0.1", true)]
	[InlineData("10.1.2.3", true)]
	[InlineData("172.16.0.1", true)]
	[InlineData("172.32.0.1", false)]
	[InlineData("192.168.74.200", true)]
	[InlineData("169.254.169.254", true)]
	[InlineData("100.64.0.1", true)]
	[InlineData("0.0.0.0", true)]
	[InlineData("224.0.0.1", true)]
	[InlineData("8.8.8.8", false)]
	[InlineData("::1", true)]
	[InlineData("fe80::1", true)]
	[InlineData("fd00::1", true)]
	[InlineData("::ffff:192.168.1.1", true)]
	[InlineData("2606:4700:4700::1111", false)]
	public void IsPrivate_ClassifiesAddresses(string address, bool expected) =>
		Assert.Equal(expected, WebhookNetworkGuard.IsPrivate(IPAddress.Parse(address)));

	[Fact]
	public void Sign_IsTheHexHmacSha256OfTheBody() =>
		// Known vector: HMAC-SHA256("key", "The quick brown fox jumps over the lazy dog")
		Assert.Equal("sha256=f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8",
			WebhookDispatcher.Sign("key", "The quick brown fox jumps over the lazy dog"));

	[Theory]
	[InlineData("10,60", new[] { 10.0, 60.0 })]
	[InlineData(" 0.5 , x, 2 ", new[] { 0.5, 2.0 })]
	[InlineData("", new double[0])]
	public void RetryDelays_AreParsedLeniently(string value, double[] seconds) =>
		Assert.Equal(seconds, WebhookDispatcher.ParseDelays(value).Select(t => t.TotalSeconds));
}
