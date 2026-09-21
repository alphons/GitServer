using System.Net;
using System.Text.Json;
using GitServer.Data;
using GitServer.Models;
using GitServer.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GitServer.Tests.TestSupport.WebSession;

namespace GitServer.Tests;

/// <summary>The issue tracker as different people use it: who may open, comment on and close issues, that a
/// private repository's issues stay private, and that a repository can't be used to reach another one's issues.</summary>
public class IssuesEndToEndTests : IClassFixture<GitServerFactory>
{
	private readonly GitServerFactory _f;

	public IssuesEndToEndTests(GitServerFactory factory) => _f = factory;

	private static string Unique(string stem) => stem + Guid.NewGuid().ToString("N")[..6];
	private static string En(string key) =>
		JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(TestPaths.LocalizationRoot, "en", "strings.json")))![key];

	private Task<T> Db<T>(Func<AppDbContext, Task<T>> action) => _f.UseServicesAsync(sp => action(sp.GetRequiredService<AppDbContext>()));
	private async Task<WebSession> AsAsync(AppUser user) => await new WebSession(_f).LoginAsync(user.UserName!);
	private static bool IsLoginRedirect(HttpResponseMessage r) =>
		r.StatusCode == HttpStatusCode.Redirect && r.Headers.Location != null &&
		(r.Headers.Location.IsAbsoluteUri ? r.Headers.Location.AbsolutePath : r.Headers.Location.OriginalString)
			.StartsWith("/dashboard/Auth/Login", StringComparison.OrdinalIgnoreCase);

	// A page that always renders a form, so a signed-in session can obtain an antiforgery token even when the target page is forbidden.
	private const string TokenPage = "/dashboard/User/Settings";

	private Task<HttpResponseMessage> PostAsync(WebSession s, string url, params (string, string)[] fields) => s.PostFormAsync(TokenPage, url, fields);

	private Task<Issue> AddIssueAsync(Repository repo, AppUser author, string title = "A problem", string body = "It **breaks**.") =>
		Db(async db =>
		{
			var issue = new Issue { RepositoryId = repo.Id, AuthorId = author.Id, Title = title, Body = body };
			db.Issues.Add(issue);
			await db.SaveChangesAsync();
			return issue;
		});

	private Task Grant(Repository repo, AppUser user, AccessLevel level) =>
		Db(async db => { db.RepositoryAccesses.Add(new RepositoryAccess { RepositoryId = repo.Id, UserId = user.Id, Level = level }); await db.SaveChangesAsync(); return 0; });

	private Task<int> IssueCount(Repository repo) => Db(db => db.Issues.CountAsync(i => i.RepositoryId == repo.Id));
	private Task<int> CommentCount(int issueId) => Db(db => db.IssueComments.CountAsync(c => c.IssueId == issueId));

	// ---- Opening issues ---------------------------------------------------------------------------

	[Fact]
	public async Task AnySignedInUser_CanOpenAnIssue_OnAPublicRepo_AndItAppearsInTheList()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var bob = await _f.CreateUserAsync(Unique("bob"));
		var repo = await _f.CreateRepoAsync(alice, "tracker");
		var session = await AsAsync(bob);

		var response = await session.PostFormAsync($"/{alice.UserName}/tracker/issues/new", $"/{alice.UserName}/tracker/issues/new",
			("Title", "Crash on start"), ("Body", "Steps to reproduce"));

		Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
		var issue = await Db(db => db.Issues.SingleAsync(i => i.RepositoryId == repo.Id));
		Assert.Equal(bob.Id, issue.AuthorId);
		Assert.Equal($"/{alice.UserName}/tracker/issues/{issue.Id}", Location(response));
		var list = await session.GetHtmlAsync($"/{alice.UserName}/tracker/issues");
		Assert.Contains("Crash on start", list);
		Assert.Contains($"#{issue.Id}", list);
	}

	[Fact]
	public async Task OpeningAnIssue_RequiresSigningIn()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var repo = await _f.CreateRepoAsync(alice, "tracker");

		var page = await new WebSession(_f).GetAsync($"/{alice.UserName}/tracker/issues/new");
		var post = await new WebSession(_f).PostFormAsync("/dashboard/Auth/Login", $"/{alice.UserName}/tracker/issues/new", ("Title", "anon"), ("Body", "x"));

		Assert.True(IsLoginRedirect(page));
		Assert.True(IsLoginRedirect(post));
		Assert.Equal(0, await IssueCount(repo));
	}

	[Fact]
	public async Task IssueTitlesAndAuthors_AreHtmlEncoded()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var repo = await _f.CreateRepoAsync(alice, "tracker");
		var issue = await AddIssueAsync(repo, alice, title: "<script>alert('x')</script>");

		var list = await new WebSession(_f).GetHtmlAsync($"/{alice.UserName}/tracker/issues");
		var detail = await new WebSession(_f).GetHtmlAsync($"/{alice.UserName}/tracker/issues/{issue.Id}");

		Assert.DoesNotContain("<script>alert('x')", list);
		Assert.DoesNotContain("<script>alert('x')", detail);
		Assert.Contains("&lt;script&gt;", detail);
	}

	[Fact]
	public async Task IssuesCanBeOpened_OnAReadOnlyRepo_BecauseReadOnlyOnlyBlocksPushes()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var repo = await _f.CreateRepoAsync(alice, "frozen", readOnly: true);

		var response = await (await AsAsync(alice)).PostFormAsync($"/{alice.UserName}/frozen/issues/new", $"/{alice.UserName}/frozen/issues/new",
			("Title", "still discussable"), ("Body", "x"));

		Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
		Assert.Equal(1, await IssueCount(repo));
	}

	// ---- Private repositories keep their issues private -----------------------------------------------------

	[Fact]
	public async Task OnAPrivateRepo_AStrangerCannotOpenAnIssue_AReaderAndTheOwnerCan()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var reader = await _f.CreateUserAsync(Unique("reader"));
		var mallory = await _f.CreateUserAsync(Unique("mallory"));
		var repo = await _f.CreateRepoAsync(alice, "secret", isPrivate: true);
		await Grant(repo, reader, AccessLevel.Read);
		var url = $"/{alice.UserName}/secret/issues/new";

		var byStranger = await PostAsync(await AsAsync(mallory), url, ("Title", "spam"), ("Body", "x"));
		var byReader = await PostAsync(await AsAsync(reader), url, ("Title", "question"), ("Body", "x"));
		var byOwner = await PostAsync(await AsAsync(alice), url, ("Title", "note"), ("Body", "x"));

		Assert.True(IsLoginRedirect(byStranger), "status " + (int)byStranger.StatusCode);
		Assert.Equal(HttpStatusCode.Redirect, byReader.StatusCode);
		Assert.Equal(HttpStatusCode.Redirect, byOwner.StatusCode);
		Assert.Equal(new[] { "note", "question" }, (await Db(db => db.Issues.Where(i => i.RepositoryId == repo.Id).Select(i => i.Title).OrderBy(t => t).ToListAsync())));
	}

	[Fact]
	public async Task OnAPrivateRepo_TheIssueListAndDetail_AreHiddenFromStrangersAndAnonymous()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var mallory = await _f.CreateUserAsync(Unique("mallory"));
		var repo = await _f.CreateRepoAsync(alice, "secret", isPrivate: true);
		var issue = await AddIssueAsync(repo, alice, title: "internal roadmap");
		var paths = new[] { $"/{alice.UserName}/secret/issues", $"/{alice.UserName}/secret/issues/{issue.Id}",
			$"/{alice.UserName}/secret/issues/{issue.Id}/raw" };

		foreach (var path in paths)
		{
			Assert.True(IsLoginRedirect(await new WebSession(_f).GetAsync(path)), "anonymous " + path);
			Assert.True(IsLoginRedirect(await (await AsAsync(mallory)).GetAsync(path)), "stranger " + path);
			Assert.Equal(HttpStatusCode.OK, (await (await AsAsync(alice)).GetAsync(path)).StatusCode);
		}
	}

	[Fact]
	public async Task OnAPrivateRepo_AStrangerCannotCommentOnAnIssue()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var mallory = await _f.CreateUserAsync(Unique("mallory"));
		var repo = await _f.CreateRepoAsync(alice, "secret", isPrivate: true);
		var issue = await AddIssueAsync(repo, alice);

		var response = await PostAsync(await AsAsync(mallory), $"/{alice.UserName}/secret/issues/{issue.Id}?handler=Comment", ("CommentBody", "let me in"));

		Assert.True(IsLoginRedirect(response), "status " + (int)response.StatusCode);
		Assert.Equal(0, await CommentCount(issue.Id));
	}

	[Fact]
	public async Task AnIssueOfAnotherRepository_CannotBeReachedThroughThisOne()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var repoA = await _f.CreateRepoAsync(alice, "a-repo");
		await _f.CreateRepoAsync(alice, "b-repo");
		var issue = await AddIssueAsync(repoA, alice);

		var session = await AsAsync(alice);

		Assert.Equal(HttpStatusCode.NotFound, (await session.GetAsync($"/{alice.UserName}/b-repo/issues/{issue.Id}")).StatusCode);
		Assert.Equal(HttpStatusCode.NotFound, (await session.GetAsync($"/{alice.UserName}/b-repo/issues/{issue.Id}/raw")).StatusCode);
		var comment = await PostAsync(session, $"/{alice.UserName}/b-repo/issues/{issue.Id}?handler=Comment", ("CommentBody", "x"));
		Assert.Equal(HttpStatusCode.NotFound, comment.StatusCode);
		Assert.Equal(0, await CommentCount(issue.Id));
	}

	// ---- Comments ---------------------------------------------------------------------------------------

	[Fact]
	public async Task ASignedInUser_CanComment_AndTheCommentShowsUp_WithItsRawText()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var bob = await _f.CreateUserAsync(Unique("bob"));
		var repo = await _f.CreateRepoAsync(alice, "tracker");
		var issue = await AddIssueAsync(repo, alice);
		var session = await AsAsync(bob);

		var response = await session.PostFormAsync($"/{alice.UserName}/tracker/issues/{issue.Id}", $"/{alice.UserName}/tracker/issues/{issue.Id}?handler=Comment",
			("CommentBody", "Same here, **on Windows**."));

		Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
		var comment = await Db(db => db.IssueComments.SingleAsync(c => c.IssueId == issue.Id));
		Assert.Equal(bob.Id, comment.AuthorId);
		Assert.Contains(bob.UserName!, await session.GetHtmlAsync($"/{alice.UserName}/tracker/issues/{issue.Id}"));
		var raw = await session.GetAsync($"/{alice.UserName}/tracker/issues/{issue.Id}/comments/{comment.Id}/raw");
		Assert.Equal("Same here, **on Windows**.", await raw.Content.ReadAsStringAsync());
		Assert.StartsWith("text/plain", raw.Content.Headers.ContentType!.MediaType);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public async Task ABlankComment_IsIgnored(string body)
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var repo = await _f.CreateRepoAsync(alice, "tracker");
		var issue = await AddIssueAsync(repo, alice);

		await PostAsync(await AsAsync(alice), $"/{alice.UserName}/tracker/issues/{issue.Id}?handler=Comment", ("CommentBody", body));

		Assert.Equal(0, await CommentCount(issue.Id));
	}

	[Fact]
	public async Task Commenting_RequiresSigningIn_AndTheIssueMustExist()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var repo = await _f.CreateRepoAsync(alice, "tracker");
		var issue = await AddIssueAsync(repo, alice);

		var anonymous = await new WebSession(_f).PostFormAsync("/dashboard/Auth/Login", $"/{alice.UserName}/tracker/issues/{issue.Id}?handler=Comment", ("CommentBody", "x"));
		var missing = await PostAsync(await AsAsync(alice), $"/{alice.UserName}/tracker/issues/999999?handler=Comment", ("CommentBody", "x"));

		Assert.True(anonymous.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
		Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
		Assert.Equal(0, await CommentCount(issue.Id));
	}

	// ---- Closing and reopening ------------------------------------------------------------------------

	[Fact]
	public async Task TheAuthor_TheOwner_AndAWriter_CanCloseAndReopen_AReaderCannot()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var author = await _f.CreateUserAsync(Unique("author"));
		var writer = await _f.CreateUserAsync(Unique("writer"));
		var reader = await _f.CreateUserAsync(Unique("reader"));
		var repo = await _f.CreateRepoAsync(alice, "tracker");
		await Grant(repo, writer, AccessLevel.Write);
		await Grant(repo, reader, AccessLevel.Read);
		var issue = await AddIssueAsync(repo, author);
		var basePath = $"/{alice.UserName}/tracker/issues/{issue.Id}";
		async Task<bool> IsClosed() => (await Db(db => db.Issues.SingleAsync(i => i.Id == issue.Id))).IsClosed;

		var byReader = await PostAsync(await AsAsync(reader), basePath + "?handler=Close");
		Assert.False(await IsClosed());
		Assert.True(IsLoginRedirect(byReader));

		await PostAsync(await AsAsync(author), basePath + "?handler=Close");
		Assert.True(await IsClosed());
		await PostAsync(await AsAsync(writer), basePath + "?handler=Reopen");
		Assert.False(await IsClosed());
		await PostAsync(await AsAsync(alice), basePath + "?handler=Close");
		Assert.True(await IsClosed());
		Assert.True(IsLoginRedirect(await PostAsync(await AsAsync(reader), basePath + "?handler=Reopen")));
		Assert.True(await IsClosed());
	}

	[Fact]
	public async Task TheListSeparatesOpenFromClosedIssues()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var repo = await _f.CreateRepoAsync(alice, "tracker");
		await AddIssueAsync(repo, alice, "still open");
		var closed = await AddIssueAsync(repo, alice, "already done");
		await Db(async db => { (await db.Issues.SingleAsync(i => i.Id == closed.Id)).IsClosed = true; await db.SaveChangesAsync(); return 0; });
		var session = new WebSession(_f);

		var open = await session.GetHtmlAsync($"/{alice.UserName}/tracker/issues");
		var done = await session.GetHtmlAsync($"/{alice.UserName}/tracker/issues?closed=1");

		Assert.Contains("still open", open);
		Assert.DoesNotContain("already done", open);
		Assert.Contains("already done", done);
		Assert.DoesNotContain("still open", done);
	}

	[Fact]
	public async Task AnEmptyTracker_SaysSo()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		await _f.CreateRepoAsync(alice, "empty");

		var html = await new WebSession(_f).GetHtmlAsync($"/{alice.UserName}/empty/issues");

		Assert.Contains(En("issues_empty_open"), html);
	}

	[Fact]
	public async Task ForAnUnknownIssue_TheDetailAndRawEndpointsAre404()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		await _f.CreateRepoAsync(alice, "tracker");
		var session = new WebSession(_f);

		Assert.Equal(HttpStatusCode.NotFound, (await session.GetAsync($"/{alice.UserName}/tracker/issues/424242")).StatusCode);
		Assert.Equal(HttpStatusCode.NotFound, (await session.GetAsync($"/{alice.UserName}/tracker/issues/424242/raw")).StatusCode);
		Assert.Equal(HttpStatusCode.NotFound, (await session.GetAsync($"/{alice.UserName}/nope/issues")).StatusCode);
	}
}
