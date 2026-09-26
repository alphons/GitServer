using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using GitServer.Controllers;
using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using GitServer.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GitServer.Tests;

/// <summary>Git LFS end to end: the real git + git-lfs client pushes and clones large files against the server on a real
/// port; the batch API's access rules and upload verification are checked with plain HTTP.</summary>
public class LfsEndToEndTests : IClassFixture<BrowserServerFactory>, IDisposable
{
	private readonly BrowserServerFactory _f;
	private readonly List<string> _dirs = new();

	public LfsEndToEndTests(BrowserServerFactory factory)
	{
		_f = factory;
		_ = _f.Server;   // starts both hosts, so BaseUrl is known
	}

	public void Dispose()
	{
		foreach (var dir in _dirs.Where(Directory.Exists))
		{
			foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
				File.SetAttributes(file, FileAttributes.Normal);
			Directory.Delete(dir, recursive: true);
		}
	}

	private static string Unique(string stem) => stem + Guid.NewGuid().ToString("N")[..6];
	private string RemoteUrl(string owner, string repo) => $"{_f.BaseUrl}/git/{owner}/{repo}.git";
	private static string BasicHeader(AppUser user) => "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user.UserName}:{GitServerFactory.Password}"));
	private string LfsObject(string owner, string repo, string oid) => Path.Combine(_f.ReposPath, owner, repo + ".git", "lfs", "objects", oid[..2], oid[2..4], oid);
	private static string Oid(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

	private string NewDir()
	{
		var dir = Path.Combine(Path.GetTempPath(), $"gitserver-lfs-{Guid.NewGuid():N}");
		Directory.CreateDirectory(dir);
		_dirs.Add(dir);
		return dir;
	}

	/// <summary>Runs the full git client (with git-lfs) non-interactively, authenticating with the given header.</summary>
	private static string Git(string workingDir, string? authorization, params string[] args)
	{
		var psi = new ProcessStartInfo("git")
		{
			WorkingDirectory = workingDir,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
		};
		psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
		psi.Environment["GCM_INTERACTIVE"] = "never";
		psi.ArgumentList.Add("-c"); psi.ArgumentList.Add("credential.helper=");
		psi.ArgumentList.Add("-c"); psi.ArgumentList.Add("user.name=Test");
		psi.ArgumentList.Add("-c"); psi.ArgumentList.Add("user.email=test@example.com");
		psi.ArgumentList.Add("-c"); psi.ArgumentList.Add("commit.gpgsign=false");
		if (authorization != null) { psi.ArgumentList.Add("-c"); psi.ArgumentList.Add($"http.extraHeader=Authorization: {authorization}"); }
		foreach (var a in args) psi.ArgumentList.Add(a);

		using var process = Process.Start(psi)!;
		var stdout = process.StandardOutput.ReadToEndAsync();
		var stderr = process.StandardError.ReadToEndAsync();
		if (!process.WaitForExit(120_000)) { process.Kill(true); throw new TimeoutException($"git {string.Join(' ', args)} hung"); }
		if (process.ExitCode != 0)
			throw new InvalidOperationException($"git {string.Join(' ', args)} failed ({process.ExitCode}): {stderr.Result}");
		return stdout.Result.Trim();
	}

	/// <summary>A local repository with one LFS-tracked binary file, pushed to owner/repo. Returns the file's bytes.</summary>
	private byte[] PushLfsFile(AppUser owner, string repo, string file = "data.bin", int size = 300_000)
	{
		var dir = NewDir();
		var content = RandomNumberGenerator.GetBytes(size);
		Git(dir, null, "init", "-b", "main");
		Git(dir, null, "lfs", "install", "--local");
		Git(dir, null, "lfs", "track", "*.bin");
		File.WriteAllBytes(Path.Combine(dir, file), content);
		File.WriteAllText(Path.Combine(dir, "readme.txt"), "small file\n");
		Git(dir, null, "add", ".");
		Git(dir, null, "commit", "-m", "Add large file");
		Git(dir, BasicHeader(owner), "push", RemoteUrl(owner.UserName!, repo), "main");
		return content;
	}

	private HttpClient Client(AppUser? user = null)
	{
		var client = new HttpClient { BaseAddress = new Uri(_f.BaseUrl) };
		if (user != null) client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(BasicHeader(user));
		return client;
	}

	private static HttpRequestMessage Batch(string url, string operation, string oid, long size) => new(HttpMethod.Post, url)
	{
		Content = new StringContent($$"""{"operation":"{{operation}}","transfers":["basic"],"objects":[{"oid":"{{oid}}","size":{{size}}}]}""",
			Encoding.UTF8, LfsController.MediaType),
	};

	[Fact]
	public async Task TheGitLfsClient_PushesAndClonesALargeFile()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		await _f.CreateRepoAsync(alice, "assets", isPrivate: true);

		var content = PushLfsFile(alice, "assets");

		var oid = Oid(content);
		Assert.Equal(content, await File.ReadAllBytesAsync(LfsObject(alice.UserName!, "assets", oid)));
		var pointer = LocalGit.Exec(Path.Combine(_f.ReposPath, alice.UserName!, "assets.git"), null, "show", "main:data.bin");
		Assert.Equal(new LfsPointer(oid, content.Length), LfsStore.ParsePointer(Encoding.UTF8.GetString(pointer.Out)));

		var clone = Path.Combine(NewDir(), "clone");
		Git(Path.GetDirectoryName(clone)!, BasicHeader(alice), "clone", RemoteUrl(alice.UserName!, "assets"), clone);   // no -b: HEAD must name the pushed branch
		Assert.Equal(content, await File.ReadAllBytesAsync(Path.Combine(clone, "data.bin")));
	}

	[Fact]
	public async Task TheRawFile_IsTheLargeFile_AndTheFilePageSaysItIsInLfs()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		await _f.CreateRepoAsync(alice, "public-assets");
		var content = PushLfsFile(alice, "public-assets");

		var raw = await Client().GetByteArrayAsync($"/{alice.UserName}/public-assets/raw/main/data.bin");
		var page = await Client().GetStringAsync($"/{alice.UserName}/public-assets/blob/main/data.bin");

		Assert.Equal(content, raw);
		Assert.Contains("Git LFS", page);
		Assert.Contains($"{content.Length} bytes", page);
	}

	[Fact]
	public async Task Uploading_NeedsWriteAccess_DownloadingReadAccess()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var reader = await _f.CreateUserAsync(Unique("reader"));
		var repo = await _f.CreateRepoAsync(alice, "guarded", isPrivate: true);
		await _f.UseServicesAsync(async sp =>
		{
			var db = sp.GetRequiredService<AppDbContext>();
			db.RepositoryAccesses.Add(new RepositoryAccess { RepositoryId = repo.Id, UserId = reader.Id, Level = AccessLevel.Read });
			await db.SaveChangesAsync();
		});
		var content = PushLfsFile(alice, "guarded");
		var oid = Oid(content);
		var batchUrl = $"/git/{alice.UserName}/guarded.git/info/lfs/objects/batch";

		var anonymousDownload = await Client().SendAsync(Batch(batchUrl, "download", oid, content.Length));
		var readerDownload = await Client(reader).SendAsync(Batch(batchUrl, "download", oid, content.Length));
		var readerUpload = await Client(reader).SendAsync(Batch(batchUrl, "upload", Oid([1, 2, 3]), 3));
		var readerPut = await Client(reader).PutAsync($"/git/{alice.UserName}/guarded.git/info/lfs/objects/{Oid([1, 2, 3])}", new ByteArrayContent([1, 2, 3]));
		var readerObject = await Client(reader).GetByteArrayAsync($"/git/{alice.UserName}/guarded.git/info/lfs/objects/{oid}");

		Assert.Equal(HttpStatusCode.Unauthorized, anonymousDownload.StatusCode);
		Assert.Equal(HttpStatusCode.OK, readerDownload.StatusCode);
		Assert.Contains("\"download\"", await readerDownload.Content.ReadAsStringAsync());
		Assert.Equal(HttpStatusCode.Forbidden, readerUpload.StatusCode);
		Assert.Equal(HttpStatusCode.Forbidden, readerPut.StatusCode);
		Assert.Equal(content, readerObject);
	}

	[Fact]
	public async Task AnUpload_WhoseContentDoesNotMatchItsId_IsRefused()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		await _f.CreateRepoAsync(alice, "verified");
		var claimed = Oid([9, 9, 9]);

		var put = await Client(alice).PutAsync($"/git/{alice.UserName}/verified.git/info/lfs/objects/{claimed}", new ByteArrayContent([1, 2, 3]));

		Assert.Equal((HttpStatusCode)422, put.StatusCode);
		Assert.False(File.Exists(LfsObject(alice.UserName!, "verified", claimed)));
	}

	[Fact]
	public async Task ABatchUpload_SkipsObjectsTheServerAlreadyHas_AndDownloadReportsMissingOnes()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		await _f.CreateRepoAsync(alice, "dedup");
		var content = PushLfsFile(alice, "dedup");
		var batchUrl = $"/git/{alice.UserName}/dedup.git/info/lfs/objects/batch";

		var again = await (await Client(alice).SendAsync(Batch(batchUrl, "upload", Oid(content), content.Length))).Content.ReadAsStringAsync();
		var missing = await (await Client(alice).SendAsync(Batch(batchUrl, "download", Oid([4, 5]), 2))).Content.ReadAsStringAsync();

		Assert.DoesNotContain("\"actions\"", again);
		Assert.Contains("\"code\":404", missing);
	}

	[Fact]
	public async Task AFork_GetsItsOwnCopyOfTheLfsObjects()
	{
		var alice = await _f.CreateUserAsync(Unique("alice"));
		var bob = await _f.CreateUserAsync(Unique("bob"));
		var source = await _f.CreateRepoAsync(alice, "big");
		var content = PushLfsFile(alice, "big");

		await _f.UseServicesAsync(async sp =>
		{
			var repos = sp.GetRequiredService<RepositoryService>();
			var bobUser = await sp.GetRequiredService<AppDbContext>().Users.SingleAsync(u => u.Id == bob.Id);
			await repos.ForkAsync((await repos.GetAsync(alice.UserName!, "big"))!, bobUser, null, "big");
		});

		Assert.Equal(content, await File.ReadAllBytesAsync(LfsObject(bob.UserName!, "big", Oid(content))));
	}

	[Theory]
	[InlineData("version https://git-lfs.github.com/spec/v1\noid sha256:4d7a214614ab2935c943f9e0ff69d22eadbb8f32b1258daaa5e2ca24d17e2393\nsize 12345\n", true)]
	[InlineData("version https://git-lfs.github.com/spec/v1\r\noid sha256:4d7a214614ab2935c943f9e0ff69d22eadbb8f32b1258daaa5e2ca24d17e2393\r\nsize 12345\r\n", true)]
	[InlineData("version https://git-lfs.github.com/spec/v1\noid sha256:XYZ\nsize 1\n", false)]
	[InlineData("just a normal text file\n", false)]
	public void Pointers_AreRecognised(string content, bool isPointer) =>
		Assert.Equal(isPointer, LfsStore.ParsePointer(content) != null);
}
