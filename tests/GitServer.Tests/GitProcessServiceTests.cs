using System.Diagnostics;
using System.Text;
using GitServer.Services;
using GitServer.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GitServer.Tests;

public class GitProcessServiceTests : IDisposable
{
	private readonly TempRepoFixture _repo = new();
	private readonly GitProcessService _git = new(new TestGitExecutablePathProvider(), NullLogger<GitProcessService>.Instance);

	public void Dispose() => _repo.Dispose();

	[Fact]
	public async Task InitBare_CreatesValidBareRepo()
	{
		await _git.InitBare(_repo.Path);

		Assert.True(File.Exists(Path.Combine(_repo.Path, "HEAD")));
		Assert.True(Directory.Exists(Path.Combine(_repo.Path, "refs")));
	}

	[Fact]
	public async Task IsEmpty_TrueRightAfterInit()
	{
		await _git.InitBare(_repo.Path);

		Assert.True(await _git.IsEmpty(_repo.Path));
	}

	[Fact]
	public async Task AfterCommit_ReportsBranchAndLogAndTree()
	{
		await _git.InitBare(_repo.Path);
		CommitOneFile(_repo.Path, "hello.txt", "hello world", "first commit");

		Assert.False(await _git.IsEmpty(_repo.Path));

		var branches = await _git.GetBranches(_repo.Path);
		Assert.Contains("main", branches);

		var log = await _git.GetCommitLog(_repo.Path, "main", 0, 10);
		Assert.Single(log);
		Assert.Equal("first commit", log[0].Message);

		var count = await _git.GetCommitCount(_repo.Path, "main");
		Assert.Equal(1, count);

		var tree = await _git.GetTree(_repo.Path, "main", "");
		Assert.Contains(tree, e => e.Name == "hello.txt");

		var content = await _git.GetFileContent(_repo.Path, "main", "hello.txt");
		Assert.Equal("hello world", content.TrimEnd('\n', '\r'));
	}

	[Fact]
	public async Task StreamUploadPack_Advertise_ReturnsCapabilities()
	{
		await _git.InitBare(_repo.Path);
		CommitOneFile(_repo.Path, "hello.txt", "hi", "c1");

		using var responseStream = new MemoryStream();
		await _git.StreamUploadPack(_repo.Path, Stream.Null, responseStream, advertise: true);

		var text = Encoding.UTF8.GetString(responseStream.ToArray());
		// For a non-empty repo, capabilities ride on the same pkt-line as the first ref
		// (NUL-separated) rather than on a separate "capabilities^{}" pseudo-ref line.
		Assert.Contains("refs/heads/main", text);
		Assert.Contains("multi_ack", text);
	}

	[Fact]
	public async Task StreamReceivePack_Advertise_ReturnsCapabilities()
	{
		await _git.InitBare(_repo.Path);

		using var responseStream = new MemoryStream();
		await _git.StreamReceivePack(_repo.Path, Stream.Null, responseStream, advertise: true);

		var text = Encoding.UTF8.GetString(responseStream.ToArray());
		Assert.Contains("report-status", text);
	}

	[Fact]
	public async Task MissingRepoPath_ThrowsRepositoryDataMissingException()
	{
		var missingPath = Path.Combine(Path.GetTempPath(), $"gitserver-tests-missing-{Guid.NewGuid():N}");

		await Assert.ThrowsAsync<RepositoryDataMissingException>(() => _git.IsEmpty(missingPath));
	}

	[Fact]
	public async Task GetVersion_UnresolvableExecutable_ReturnsNotInstalledSentinel()
	{
		var pathProvider = new TestGitExecutablePathProvider();
		pathProvider.SetPath(Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.exe"));
		var git = new GitProcessService(pathProvider, NullLogger<GitProcessService>.Instance);

		Assert.Equal(GitProcessService.NotInstalledVersion, await git.GetVersion());
	}

	// Commits a file into the bare repo via a throwaway working clone, since GitProcessService
	// itself only exposes server-side plumbing (no "commit" operation).
	private static void CommitOneFile(string bareRepoPath, string fileName, string content, string message)
	{
		var workDir = Path.Combine(Path.GetTempPath(), $"gitserver-tests-work-{Guid.NewGuid():N}");
		Directory.CreateDirectory(workDir);
		try
		{
			RunGit(workDir, $"clone \"{bareRepoPath}\" .");
			// Pin the branch name explicitly so the test doesn't depend on the runner's
			// init.defaultBranch config (which varies between "master" and "main").
			RunGit(workDir, "symbolic-ref HEAD refs/heads/main");
			File.WriteAllText(Path.Combine(workDir, fileName), content);
			RunGit(workDir, "add .");
			RunGit(workDir, $"-c user.email=test@example.com -c user.name=Test commit -m \"{message}\"");
			RunGit(workDir, "push origin main");
		}
		finally
		{
			foreach (var file in Directory.GetFiles(workDir, "*", SearchOption.AllDirectories))
				File.SetAttributes(file, FileAttributes.Normal);
			Directory.Delete(workDir, recursive: true);
		}
	}

	private static void RunGit(string workDir, string arguments)
	{
		var gitExe = new TestGitExecutablePathProvider().CurrentPath;
		var psi = new ProcessStartInfo(gitExe)
		{
			Arguments = arguments,
			WorkingDirectory = workDir,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
		};
		using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git");
		proc.WaitForExit();
		if (proc.ExitCode != 0)
			throw new InvalidOperationException($"git {arguments} failed: {proc.StandardError.ReadToEnd()}");
	}
}
