using System.Diagnostics;
using GitServer.Services;

namespace GitServer.Tests.TestSupport;

/// <summary>Locates a working git.exe for integration tests: an explicit override, git on PATH
/// (present by default on GitHub Actions runners), or this repo's own active MinGit install under
/// App_Data\git (useful on a dev machine that no longer has a system git on PATH, since the app's
/// GitExecutable fallback was intentionally removed).</summary>
public class TestGitExecutablePathProvider : IGitExecutablePathProvider
{
	private static readonly Lazy<string> Resolved = new(Resolve);

	public string CurrentPath { get; private set; } = Resolved.Value;

	public void SetPath(string path) => CurrentPath = path;

	private static string Resolve()
	{
		var overridePath = Environment.GetEnvironmentVariable("GIT_TEST_EXECUTABLE");
		if (!string.IsNullOrWhiteSpace(overridePath) && Works(overridePath))
			return overridePath;

		if (Works("git"))
			return "git";

		var repoRoot = FindRepoRoot();
		if (repoRoot != null)
		{
			var installRoot = Path.Combine(repoRoot, "src", "GitServer", "App_Data", "git");
			if (Directory.Exists(installRoot))
			{
				foreach (var versionDir in Directory.GetDirectories(installRoot))
				{
					var candidate = Path.Combine(versionDir, "mingw64", "bin", "git.exe");
					if (File.Exists(candidate) && Works(candidate))
						return candidate;
				}
			}
		}

		throw new InvalidOperationException(
			"No working git executable found for tests. Set GIT_TEST_EXECUTABLE, put git on PATH, " +
			"or install a MinGit version via /Admin/GitVersion in a local run of the app first.");
	}

	private static bool Works(string path)
	{
		try
		{
			var psi = new ProcessStartInfo(path)
			{
				Arguments = "--version",
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
			};
			using var proc = Process.Start(psi);
			if (proc == null) return false;
			proc.WaitForExit(5000);
			return proc.ExitCode == 0;
		}
		catch
		{
			return false;
		}
	}

	private static string? FindRepoRoot()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null)
		{
			if (File.Exists(Path.Combine(dir.FullName, "GitServer.slnx")))
				return dir.FullName;
			dir = dir.Parent;
		}
		return null;
	}
}
