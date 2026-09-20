using System.Diagnostics;
using System.Text;

namespace GitServer.Tests.TestSupport;

/// <summary>A throwaway local (non-bare) repository driven by the real git executable, used as the
/// "client side" in end-to-end tests: make a commit, produce the pack a push would send, or unpack
/// what a clone received and check it.</summary>
public sealed class LocalGit : IDisposable
{
	private static readonly string GitExe = new TestGitExecutablePathProvider().CurrentPath;

	public string Dir { get; } = Path.Combine(Path.GetTempPath(), $"gitserver-local-{Guid.NewGuid():N}");

	public LocalGit()
	{
		Directory.CreateDirectory(Dir);
		Run("init", "-b", "main");
	}

	public static (int Exit, byte[] Out, string Err) Exec(string workingDir, byte[]? stdin, params string[] args)
	{
		var psi = new ProcessStartInfo(GitExe)
		{
			WorkingDirectory = workingDir,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
		};
		foreach (var a in args) psi.ArgumentList.Add(a);

		using var process = Process.Start(psi) ?? throw new InvalidOperationException("git did not start");
		var stdout = new MemoryStream();
		var stdoutTask = process.StandardOutput.BaseStream.CopyToAsync(stdout);
		var stderrTask = process.StandardError.ReadToEndAsync();

		if (stdin != null) process.StandardInput.BaseStream.Write(stdin, 0, stdin.Length);
		process.StandardInput.Close();

		process.WaitForExit();
		stdoutTask.Wait();
		return (process.ExitCode, stdout.ToArray(), stderrTask.Result);
	}

	public string Run(params string[] args)
	{
		var (exit, output, err) = Exec(Dir, null, args);
		if (exit != 0) throw new InvalidOperationException($"git {string.Join(' ', args)} failed ({exit}): {err}");
		return Encoding.UTF8.GetString(output).Trim();
	}

	/// <summary>Writes a file, commits it, and returns the new commit's sha.</summary>
	public string Commit(string file, string content, string message = "commit")
	{
		File.WriteAllText(Path.Combine(Dir, file), content);
		Run("add", file);
		Run("-c", "user.name=Test", "-c", "user.email=test@example.com", "-c", "commit.gpgsign=false", "commit", "-m", message);
		return Run("rev-parse", "HEAD");
	}

	/// <summary>The packfile a push of <paramref name="sha"/> would send (all reachable objects).</summary>
	public byte[] PackFor(string sha)
	{
		var (exit, output, err) = Exec(Dir, Encoding.ASCII.GetBytes(sha + "\n"), "pack-objects", "--revs", "--stdout");
		if (exit != 0) throw new InvalidOperationException("git pack-objects failed: " + err);
		return output;
	}

	/// <summary>Stores a received packfile in this repository.</summary>
	public void IndexPack(byte[] pack)
	{
		var (exit, _, err) = Exec(Dir, pack, "index-pack", "--stdin", "--fix-thin");
		if (exit != 0) throw new InvalidOperationException("git index-pack failed: " + err);
	}

	public bool HasCommit(string sha) => Exec(Dir, null, "cat-file", "-e", sha + "^{commit}").Exit == 0;

	/// <summary>The sha a ref points to inside a (bare) server-side repository, or null if the ref doesn't exist.</summary>
	public static string? ServerRef(string bareRepoDir, string refName)
	{
		var (exit, output, _) = Exec(bareRepoDir, null, "--git-dir", bareRepoDir, "rev-parse", "--verify", "--quiet", refName);
		return exit == 0 ? Encoding.UTF8.GetString(output).Trim() : null;
	}

	public void Dispose()
	{
		if (!Directory.Exists(Dir)) return;
		foreach (var file in Directory.GetFiles(Dir, "*", SearchOption.AllDirectories))
			File.SetAttributes(file, FileAttributes.Normal);
		Directory.Delete(Dir, recursive: true);
	}
}
