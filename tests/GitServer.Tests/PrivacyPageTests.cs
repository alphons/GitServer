using System.Net;
using GitServer.Tests.TestSupport;
using Xunit;

namespace GitServer.Tests;

/// <summary>The privacy statement: public, linked from every page, and stating the configured audit log retention.</summary>
public class PrivacyPageTests : IClassFixture<GitServerFactory>
{
	private readonly GitServerFactory _f;

	public PrivacyPageTests(GitServerFactory factory) => _f = factory;

	[Fact]
	public async Task ThePrivacyStatement_IsPublic_LinkedFromTheFooter_AndStatesTheRetention()
	{
		var client = _f.NewClient();

		var page = await client.GetAsync("/dashboard/Privacy");
		var html = await page.Content.ReadAsStringAsync();
		var home = await client.GetStringAsync("/");

		Assert.Equal(HttpStatusCode.OK, page.StatusCode);
		Assert.Contains("Privacy statement", html);
		Assert.Contains("after 365 days", html);                 // GitServerOptions.AuditLogRetentionDays default
		Assert.Contains("gravatar.com", html);
		Assert.Contains("privacy@example.test", html);           // the contact address the test factory configures
		Assert.DoesNotContain("Use this page to detail", html);  // the template placeholder is gone
		Assert.Contains("href=\"/dashboard/Privacy\"", home);
	}
}
