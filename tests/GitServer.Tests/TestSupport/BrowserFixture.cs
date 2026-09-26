using GitServer.Models;
using Microsoft.Playwright;
using Xunit;

namespace GitServer.Tests.TestSupport;

/// <summary>One real server plus one headless Chromium for a test class. Needs the browser once per machine:
/// <c>powershell tests/GitServer.Tests/bin/Debug/net10.0/playwright.ps1 install chromium</c> (or <c>pwsh</c>).</summary>
public sealed class BrowserFixture : IAsyncLifetime
{
	private IPlaywright? playwright;
	private IBrowser? browser;

	public BrowserServerFactory Site { get; } = new();

	public async Task InitializeAsync()
	{
		_ = Site.Services;   // starts both hosts
		playwright = await Playwright.CreateAsync();
		try
		{
			browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
		}
		catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist"))
		{
			throw new InvalidOperationException(
				"Chromium is not installed for Playwright. Run: powershell tests/GitServer.Tests/bin/Debug/net10.0/playwright.ps1 install chromium", ex);
		}
	}

	public async Task DisposeAsync()
	{
		if (browser != null) await browser.DisposeAsync();
		playwright?.Dispose();
		Site.Dispose();
	}

	/// <summary>A fresh browser page (own cookies), optionally signed in through the real login form.</summary>
	public async Task<IPage> NewPageAsync(AppUser? signedInAs = null)
	{
		var context = await browser!.NewContextAsync(new BrowserNewContextOptions { BaseURL = Site.BaseUrl, Locale = "en-US" });
		// Generous: the rest of the suite (13 other classes, each with their own in-process host) runs in parallel, and
		// password hashing is deliberately slow — under a saturated CI/dev machine 30s was occasionally not enough.
		context.SetDefaultTimeout(60_000);
		var page = await context.NewPageAsync();
		if (signedInAs != null)
		{
			await page.GotoAsync("/dashboard/Auth/Login");
			await page.FillAsync("input[name=Username]", signedInAs.UserName!);
			await page.FillAsync("input[name=Password]", GitServerFactory.Password);
			await page.ClickAsync("form button[type=submit]");
			// Only the redirect matters here, not the next page's "load" event: that waits for external resources too
			// (Gravatar avatars, cdnjs), and one slow request on a CI runner used to time the whole test out.
			await page.WaitForURLAsync(url => !url.Contains("/Auth/Login"), new() { WaitUntil = WaitUntilState.Commit });
		}
		return page;
	}
}
