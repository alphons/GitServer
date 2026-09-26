using System.Net;
using System.Text;
using GitServer.Models;
using GitServer.Services;
using GitServer.Tests.TestSupport;
using Xunit;
using static GitServer.Tests.TestSupport.GitWire;

namespace GitServer.Tests;

/// <summary>HEAD — the branch a clone checks out — always names a branch that exists: new repositories start on "main"
/// whatever the machine's git config says, a push repairs a HEAD that points nowhere, and the default branch setting
/// moves HEAD along.</summary>
public class DefaultBranchEndToEndTests : IClassFixture<GitServerFactory>
{
	private const string ReceivePackType = "application/x-git-receive-pack-request";
	private readonly GitServerFactory _f;

	public DefaultBranchEndToEndTests(GitServerFactory factory) => _f = factory;

	private static string Unique(string stem) => stem + Guid.NewGuid().ToString("N")[..6];
	private string Folder(AppUser owner, string repo) => Path.Combine(_f.ReposPath, owner.UserName!, repo + ".git");
	private static string Head(string folder) => Encoding.UTF8.GetString(LocalGit.Exec(folder, null, "symbolic-ref", "HEAD").Out).Trim();

	private async Task<HttpResponseMessage> PushAsync(AppUser owner, string repo, string branch)
	{
		using var local = new LocalGit();
		var sha = local.Commit("a.txt", "hello\n");
		return await _f.NewClient().SendAsync(Post($"/git/{owner.UserName}/{repo}.git/git-receive-pack", ReceivePackType,
			ReceivePackRequest(WebhookService.ZeroSha, sha, $"refs/heads/{branch}", local.PackFor(sha)), Basic(owner.UserName!, GitServerFactory.Password)));
	}

	[Fact]
	public async Task ANewRepository_StartsOnMain()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		await _f.CreateRepoAsync(alice, "fresh");

		Assert.Equal("refs/heads/main", Head(Folder(alice, "fresh")));
	}

	[Fact]
	public async Task APush_RepairsAHeadThatPointsToABranchThatDoesNotExist()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		await _f.CreateRepoAsync(alice, "legacy");
		var folder = Folder(alice, "legacy");
		LocalGit.Exec(folder, null, "symbolic-ref", "HEAD", "refs/heads/master");   // as older versions created them

		var push = await PushAsync(alice, "legacy", "main");

		Assert.Equal(HttpStatusCode.OK, push.StatusCode);
		Assert.Equal("refs/heads/main", Head(folder));
	}

	[Fact]
	public async Task APush_LeavesAHeadThatExistsAlone()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		await _f.CreateRepoAsync(alice, "twobranches");
		await PushAsync(alice, "twobranches", "main");

		await PushAsync(alice, "twobranches", "develop");

		Assert.Equal("refs/heads/main", Head(Folder(alice, "twobranches")));
	}

	[Fact]
	public async Task TheDefaultBranchSetting_MovesHead_AndRefusesInvalidNames()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		await _f.CreateRepoAsync(alice, "configured");
		var session = await new WebSession(_f).LoginAsync(alice.UserName!);
		var page = $"/{alice.UserName}/configured/settings";

		var good = await session.PostFormAsync(page, page + "?handler=Update", ("DefaultBranch", "release/1.0"));
		var bad = await session.PostFormAsync(page, page + "?handler=Update", ("DefaultBranch", "--upload-pack=evil"));

		Assert.Equal(HttpStatusCode.OK, good.StatusCode);
		Assert.Equal("refs/heads/release/1.0", Head(Folder(alice, "configured")));
		Assert.Contains("Invalid branch name", await bad.Content.ReadAsStringAsync());
		Assert.Equal("refs/heads/release/1.0", Head(Folder(alice, "configured")));
	}

	[Theory]
	[InlineData("main", true)]
	[InlineData("release/1.0", true)]
	[InlineData("feature_x-2", true)]
	[InlineData("-evil", false)]
	[InlineData("a..b", false)]
	[InlineData("a b", false)]
	[InlineData("x.lock", false)]
	[InlineData("trailing/", false)]
	[InlineData("quote\"", false)]
	public void BranchNames_AreValidated(string name, bool valid) =>
		Assert.Equal(valid, GitProcessService.IsValidBranchName(name));
}
