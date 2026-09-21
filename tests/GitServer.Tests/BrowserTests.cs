using System.Text.RegularExpressions;
using GitServer.Tests.TestSupport;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace GitServer.Tests;

/// <summary>The pages whose behaviour lives in JavaScript, driven in a real (headless) browser against the real application.
/// The server-side of the same calls is covered by the HTTP end-to-end tests; these check what the user actually sees.</summary>
public class BrowserTests : IClassFixture<BrowserFixture>
{
	private readonly BrowserFixture browser;

	public BrowserTests(BrowserFixture browser) => this.browser = browser;

	private static string Unique(string stem) => stem + Guid.NewGuid().ToString("N")[..6];

	[Fact]
	public async Task Profile_ListsRepositories_FiltersWhileTyping_AndPages()
	{
		var alice = await browser.Site.CreateUserAsync(Unique("alice"));
		await browser.Site.AddRepoRowsAsync(alice, null, "repo", 25);
		await browser.Site.CreateRepoAsync(alice, "needle-repo");
		var page = await browser.NewPageAsync(alice);

		await page.GotoAsync($"/dashboard/User/{alice.UserName}");
		await Expect(page.Locator("#profile-repos-container .repo-card")).ToHaveCountAsync(20);

		await page.Locator("#profile-repos-container .pagination button", new() { HasTextString = "Next" }).First.ClickAsync();
		await Expect(page.Locator("#profile-repos-container .repo-card")).ToHaveCountAsync(6);

		await page.FillAsync("#profile-repo-search", "needle");
		await Expect(page.Locator("#profile-repos-container .repo-card")).ToHaveCountAsync(1);
		await Expect(page.Locator("#profile-repos-container .repo-card .repo-name")).ToHaveTextAsync(new Regex("needle-repo"));
	}

	[Fact]
	public async Task ApiKeys_CanBeCreated_Disabled_EnabledAndDeleted_InATableWithHeaders()
	{
		var alice = await browser.Site.CreateUserAsync(Unique("alice"));
		var page = await browser.NewPageAsync(alice);
		await page.GotoAsync("/dashboard/User/ApiKeys");

		await page.FillAsync("#apikeys-name", "ci server");
		await page.ClickAsync("#apikeys-form button[type=submit]");

		await Expect(page.Locator("#apikeys-new-value")).ToHaveTextAsync(new Regex("^gsk_"));   // shown once
		var row = page.Locator("#apikeys-list tbody tr");
		await Expect(row).ToHaveCountAsync(1);
		await Expect(page.Locator("#apikeys-list table.data-table thead th")).ToHaveCountAsync(6);
		await Expect(row.Locator(".badge")).ToHaveTextAsync("Enabled");

		await row.GetByRole(AriaRole.Button, new() { Name = "Disable" }).ClickAsync();
		await Expect(row.Locator(".badge")).ToHaveTextAsync("Disabled");
		await row.GetByRole(AriaRole.Button, new() { Name = "Enable" }).ClickAsync();
		await Expect(row.Locator(".badge")).ToHaveTextAsync("Enabled");

		page.Dialog += (_, dialog) => dialog.AcceptAsync();
		await row.GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();
		await Expect(page.Locator("#apikeys-list tbody tr")).ToHaveCountAsync(0);
		await Expect(page.Locator("#apikeys-list")).ToContainTextAsync("no API keys");
	}

	[Fact]
	public async Task AccessTokens_AreListedInATableWithHeaders()
	{
		var alice = await browser.Site.CreateUserAsync(Unique("alice"));
		var page = await browser.NewPageAsync(alice);
		await page.GotoAsync("/dashboard/User/AccessTokens");

		await page.FillAsync("#tokenName", "laptop");
		await page.ClickAsync("form[method=post] button.btn-primary");

		await Expect(page.Locator("table.data-table thead th")).ToHaveCountAsync(5);
		await Expect(page.Locator("table.data-table tbody tr")).ToHaveCountAsync(1);
		await Expect(page.Locator("table.data-table tbody tr td").First).ToHaveTextAsync("laptop");
	}

	[Fact]
	public async Task ReservedNames_CanBeAdded_Edited_AndDeleted_ByAnAdmin()
	{
		var admin = await browser.Site.CreateUserAsync(Unique("boss"), isAdmin: true);
		var page = await browser.NewPageAsync(admin);
		await page.GotoAsync("/dashboard/Admin/ReservedNames");

		await Expect(page.Locator("#reserved-list tbody tr", new() { HasTextString = "dashboard" })).ToContainTextAsync("Built in");
		await Expect(page.Locator("#reserved-list tbody tr", new() { HasTextString = "user*" })).ToHaveCountAsync(1);

		var stem = Unique("zz").ToLowerInvariant();
		await page.FillAsync("#reserved-new", stem + "*");
		await page.ClickAsync("#reserved-add-form button[type=submit]");
		var row = page.Locator("#reserved-list tbody tr", new() { HasTextString = stem + "*" });
		await Expect(row).ToHaveCountAsync(1);

		await row.GetByRole(AriaRole.Button, new() { Name = "Edit" }).ClickAsync();
		var editing = page.Locator("#reserved-list tbody tr:has(input)");   // the row no longer contains its old text while editing
		await editing.Locator("input").FillAsync(stem + "-renamed");
		await editing.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
		await Expect(page.Locator("#reserved-list tbody tr", new() { HasTextString = stem + "-renamed" })).ToHaveCountAsync(1);

		await page.FillAsync("#reserved-new", "USER*");   // a duplicate, case-insensitively
		await page.ClickAsync("#reserved-add-form button[type=submit]");
		await Expect(page.Locator("#reserved-error")).ToBeVisibleAsync();

		var renamed = page.Locator("#reserved-list tbody tr", new() { HasTextString = stem + "-renamed" });
		await renamed.GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();
		await Expect(renamed).ToHaveCountAsync(0);
	}

	[Fact]
	public async Task GroupDetail_ListsRepositoriesWithPaging_AndSuggestsMembersWhileTyping()
	{
		var owner = await browser.Site.CreateUserAsync(Unique("owner"));
		var candidate = await browser.Site.CreateUserAsync(Unique("candidate"));
		var group = await browser.Site.CreateGroupAsync(Unique("Team"), owner);
		await browser.Site.AddRepoRowsAsync(null, group, "grp", 21);
		var page = await browser.NewPageAsync(owner);

		await page.GotoAsync($"/dashboard/User/GroupDetail/{group.Id}");
		await Expect(page.Locator("#group-repos-container .repo-card")).ToHaveCountAsync(20);
		await page.Locator("#group-repos-container .pagination button", new() { HasTextString = "Next" }).First.ClickAsync();
		await Expect(page.Locator("#group-repos-container .repo-card")).ToHaveCountAsync(1);

		await page.FillAsync("#memberName", candidate.UserName![..12]);
		await Expect(page.Locator("#memberResults .collaborator-result-item")).ToContainTextAsync(candidate.UserName!);
		await page.Locator("#memberResults .collaborator-result-item").First.ClickAsync();
		await Expect(page.Locator("#memberName")).ToHaveValueAsync(candidate.UserName!);
	}

	[Fact]
	public async Task AdminUsers_ListsUsers_AndFiltersWhileTyping()
	{
		var admin = await browser.Site.CreateUserAsync(Unique("boss"), isAdmin: true);
		var stem = Unique("findme");
		await browser.Site.CreateUserAsync(stem);
		await browser.Site.CreateUserAsync(Unique("bystander"));
		var page = await browser.NewPageAsync(admin);

		await page.GotoAsync("/dashboard/Admin/Users");
		await Expect(page.Locator("#users-list-container tbody tr.user-row").First).ToBeVisibleAsync();

		await page.FillAsync("#users-filter", stem);
		await Expect(page.Locator("#users-list-container tbody tr.user-row")).ToHaveCountAsync(1);
		await Expect(page.Locator("#users-list-container tbody tr.user-row")).ToContainTextAsync(stem);
	}
}
