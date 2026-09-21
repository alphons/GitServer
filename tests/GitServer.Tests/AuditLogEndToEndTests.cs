using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GitServer.Data;
using GitServer.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GitServer.Tests;

/// <summary>The audit log: who did what is recorded, and only admins can read it.</summary>
public class AuditLogEndToEndTests : IClassFixture<GitServerFactory>
{
	private const string AuditApi = "/api/admin/audit";

	private readonly GitServerFactory factory;

	public AuditLogEndToEndTests(GitServerFactory factory) => this.factory = factory;

	private static string Unique(string stem) => stem + Guid.NewGuid().ToString("N")[..6];
	private async Task<WebSession> AsAsync(GitServer.Models.AppUser user) => await new WebSession(factory).LoginAsync(user.UserName!);
	private Task<T> Db<T>(Func<AppDbContext, Task<T>> q) => factory.UseServicesAsync(sp => q(sp.GetRequiredService<AppDbContext>()));

	[Fact]
	public async Task AdminActions_AreRecorded_WithWhoAndHow()
	{
		var admin = await factory.CreateUserAsync(Unique("boss"), isAdmin: true);
		var session = await AsAsync(admin);
		var pattern = Unique("aud").ToLowerInvariant() + "*";

		await session.SendJsonAsync("/dashboard/Admin/ReservedNames", HttpMethod.Post, "/api/admin/reserved-names", new { pattern });

		var entry = await Db(d => d.AuditEntries.SingleAsync(a => a.Action == "reserved-name.add" && a.Target == pattern));
		Assert.Equal(admin.UserName, entry.ActorName);
		Assert.Equal(admin.Id, entry.ActorUserId);
		Assert.Equal("web", entry.Via);
	}

	[Fact]
	public async Task ApiKeyLifecycle_IsRecorded_IncludingFirstUseAsTheKeysOwner()
	{
		var alice = await factory.CreateUserAsync(Unique("alice"));
		var session = await AsAsync(alice);
		var created = await session.SendJsonAsync("/dashboard/User/ApiKeys", HttpMethod.Post, "/api/user/api-keys", new { name = "audited", readOnly = true });
		var key = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("key").GetString()!;

		var client = factory.NewClient();
		client.DefaultRequestHeaders.Add("X-Api-Key", key);
		await client.GetAsync($"/api/users/{alice.UserName}/repos");
		await client.GetAsync($"/api/users/{alice.UserName}/repos");

		var entries = await Db(d => d.AuditEntries.Where(a => a.ActorUserId == alice.Id).OrderBy(a => a.Id).ToListAsync());
		Assert.Contains(entries, a => a.Action == "apikey.create" && a.Target == "audited" && a.Details == "read-only");
		Assert.Single(entries, a => a.Action == "apikey.used");                     // once, however often the key is used
		Assert.Equal("api-key", entries.Single(a => a.Action == "apikey.used").Via);
	}

	[Fact]
	public async Task TheAuditLog_CanBeReadAndFiltered_ByAdminsOnly()
	{
		var admin = await factory.CreateUserAsync(Unique("boss"), isAdmin: true);
		var ordinary = await factory.CreateUserAsync(Unique("plain"));
		var session = await AsAsync(admin);
		var pattern = Unique("find").ToLowerInvariant() + "*";
		await session.SendJsonAsync("/dashboard/Admin/ReservedNames", HttpMethod.Post, "/api/admin/reserved-names", new { pattern });

		using var doc = JsonDocument.Parse(await session.GetHtmlAsync($"{AuditApi}?q={Uri.EscapeDataString(pattern[..^1].ToUpperInvariant())}"));

		var entries = doc.RootElement.GetProperty("entries").EnumerateArray().ToList();
		Assert.Single(entries);
		Assert.Equal("reserved-name.add", entries[0].GetProperty("action").GetString());
		Assert.Equal(admin.UserName, entries[0].GetProperty("actor").GetString());
		Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().GetAsync(AuditApi)).StatusCode);
		Assert.Equal(HttpStatusCode.Forbidden, (await (await AsAsync(ordinary)).GetAsync(AuditApi)).StatusCode);
	}
}
