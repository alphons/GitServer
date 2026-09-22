using System.Net;
using System.Text.Json;
using GitServer.Tests.TestSupport;
using Xunit;

namespace GitServer.Tests;

/// <summary>Wiring and authorization for /api/admin/database-migration; the copy itself is covered end-to-end
/// against a real SQL Server by <see cref="DatabaseMigrationToolTests"/>.</summary>
public class DatabaseMigrationApiEndToEndTests : IClassFixture<GitServerFactory>
{
	private const string Api = "/api/admin/database-migration";
	private readonly GitServerFactory factory;

	public DatabaseMigrationApiEndToEndTests(GitServerFactory factory) => this.factory = factory;

	private static string Unique(string stem) => stem + Guid.NewGuid().ToString("N")[..6];
	private async Task<WebSession> AsAsync(GitServer.Models.AppUser user) => await new WebSession(factory).LoginAsync(user.UserName!);

	[Fact]
	public async Task Status_ReportsSqliteAsTheCurrentProvider_ForAdminsOnly()
	{
		var admin = await factory.CreateUserAsync(Unique("boss"), isAdmin: true);
		var ordinary = await factory.CreateUserAsync(Unique("plain"));

		var html = await (await AsAsync(admin)).GetHtmlAsync($"{Api}/status");
		var status = JsonDocument.Parse(html).RootElement;
		// Only available while this test host itself runs on Sqlite (the CI matrix also runs it on SqlServer).
		Assert.Equal(!GitServerFactory.UsesSqlServer, status.GetProperty("isAvailable").GetBoolean());

		Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().GetAsync($"{Api}/status")).StatusCode);
		Assert.Equal(HttpStatusCode.Forbidden, (await (await AsAsync(ordinary)).GetAsync($"{Api}/status")).StatusCode);
	}

	[Fact]
	public async Task TestAndRun_AreAdminOnly()
	{
		var admin = await factory.CreateUserAsync(Unique("boss"), isAdmin: true);
		var ordinary = await factory.CreateUserAsync(Unique("plain"));
		var adminSession = await AsAsync(admin);
		var ordinarySession = await AsAsync(ordinary);

		// Admins may call these (the SQL Server sample connection string in appsettings.json won't actually
		// connect here, so the response just needs to come back as a normal, authorized JSON result).
		Assert.Equal(HttpStatusCode.OK, (await adminSession.SendJsonAsync("/dashboard/Admin/DatabaseMigration", HttpMethod.Post, $"{Api}/test")).StatusCode);

		// The admin page itself 403s for an ordinary user, so its antiforgery token comes from a page they can reach.
		Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().PostAsync($"{Api}/test", null)).StatusCode);
		Assert.Equal(HttpStatusCode.Forbidden, (await ordinarySession.SendJsonAsync("/dashboard/User/ApiKeys", HttpMethod.Post, $"{Api}/test")).StatusCode);
		Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().PostAsync($"{Api}/run", null)).StatusCode);
		Assert.Equal(HttpStatusCode.Forbidden, (await ordinarySession.SendJsonAsync("/dashboard/User/ApiKeys", HttpMethod.Post, $"{Api}/run")).StatusCode);
	}
}
