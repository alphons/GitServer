using System.Collections.Concurrent;
using System.Diagnostics;

namespace GitServer.Services;

public record CommitInfo(string Sha, string ShortSha, string Message, string Author, string Email, DateTime Date, string Tree, List<string> Parents, string Body = "");
public record TagInfo(string Name, string Sha, string ShortSha, DateTime Date, string Subject, string Body, string Tagger, bool Annotated);
public record CommitDetail(CommitInfo Info, string Diff, List<string> ChangedFiles);
public record TreeEntry(string Mode, string Type, string Sha, string Name, string Path);

/// <summary>The repository's DB record exists but its bare-git folder is missing on disk
/// (e.g. deleted or moved outside the application).</summary>
public class RepositoryDataMissingException(string repoPath)
	: Exception($"Repository data not found on disk at '{repoPath}'. It may have been deleted or moved outside the application.")
{
	public string RepoPath { get; } = repoPath;
}

public class GitProcessService(IGitExecutablePathProvider pathProvider, ILogger<GitProcessService> logger)
{
	private string _gitExe => pathProvider.CurrentPath;
	private readonly ILogger<GitProcessService> _logger = logger;

	// Repos may be owned by a different account than the one running the app pool
	// (e.g. after an admin copies/moves the repo folder) — git 2.35.2+ refuses to
	// operate on such directories ("detected dubious ownership") unless told otherwise.
	// GIT_CONFIG_* env vars apply this for every invocation without touching any config file.
	private static void ApplySafeDirectory(ProcessStartInfo psi)
	{
		psi.Environment["GIT_CONFIG_COUNT"] = "1";
		psi.Environment["GIT_CONFIG_KEY_0"] = "safe.directory";
		psi.Environment["GIT_CONFIG_VALUE_0"] = "*";
	}

	private ProcessStartInfo CreatePsi(string repoPath, string arguments)
	{
		var psi = new ProcessStartInfo(_gitExe)
		{
			Arguments = arguments,
			WorkingDirectory = repoPath,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = System.Text.Encoding.UTF8,
			StandardErrorEncoding = System.Text.Encoding.UTF8,
			UseShellExecute = false,
			CreateNoWindow = true,
		};
		psi.Environment["GIT_HTTP_EXPORT_ALL"] = "1";
		psi.Environment["HOME"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		psi.Environment["GIT_DIR"] = repoPath;
		ApplySafeDirectory(psi);
		return psi;
	}

	private async Task<string> RunGitAsync(string repoPath, string arguments)
	{
		if (!Directory.Exists(repoPath))
			throw new RepositoryDataMissingException(repoPath);

		var psi = CreatePsi(repoPath, arguments);
		using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git process");

		var stdoutTask = proc.StandardOutput.ReadToEndAsync();
		var stderrTask = proc.StandardError.ReadToEndAsync();

		await proc.WaitForExitAsync();
		var stdout = await stdoutTask;
		var stderr = await stderrTask;

		if (proc.ExitCode != 0)
			_logger.LogWarning("git {args} exited {code}: {err}", arguments, proc.ExitCode, stderr);

		return stdout;
	}

	public async Task StreamUploadPack(string repoPath, Stream requestBody, Stream responseStream, bool advertise)
	{
		var args = advertise
			? $"upload-pack --stateless-rpc --advertise-refs \"{repoPath}\""
			: $"upload-pack --stateless-rpc \"{repoPath}\"";

		await StreamGitProcess(repoPath, args, requestBody, responseStream, advertise);
	}

	public async Task StreamReceivePack(string repoPath, Stream requestBody, Stream responseStream, bool advertise)
	{
		var args = advertise
			? $"receive-pack --stateless-rpc --advertise-refs \"{repoPath}\""
			: $"receive-pack --stateless-rpc \"{repoPath}\"";

		await StreamGitProcess(repoPath, args, requestBody, responseStream, advertise);
	}

	private async Task StreamGitProcess(string repoPath, string arguments, Stream requestBody, Stream responseStream, bool advertise)
	{
		if (!Directory.Exists(repoPath))
			throw new RepositoryDataMissingException(repoPath);

		var psi = new ProcessStartInfo(_gitExe)
		{
			Arguments = arguments,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
		};
		psi.Environment["GIT_HTTP_EXPORT_ALL"] = "1";
		psi.Environment["HOME"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		ApplySafeDirectory(psi);

		using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git");

		var stderrTask = proc.StandardError.ReadToEndAsync();

		if (!advertise)
		{
			var copyIn = requestBody.CopyToAsync(proc.StandardInput.BaseStream);
			var copyOut = proc.StandardOutput.BaseStream.CopyToAsync(responseStream);

			await copyIn;
			proc.StandardInput.Close();
			await copyOut;
		}
		else
		{
			proc.StandardInput.Close();
			await proc.StandardOutput.BaseStream.CopyToAsync(responseStream);
		}

		await proc.WaitForExitAsync();
		var stderr = await stderrTask;

		if (proc.ExitCode != 0)
			_logger.LogWarning("git {args} exited {code}: {err}", arguments, proc.ExitCode, stderr);
	}

	/// <summary>Creates an empty bare repository whose HEAD is <paramref name="initialBranch"/>, whatever the machine's
	/// init.defaultBranch says, so the first push of that branch is what a clone checks out.</summary>
	public async Task InitBare(string repoPath, string initialBranch = "main")
	{
		Directory.CreateDirectory(repoPath);
		var psi = new ProcessStartInfo(_gitExe)
		{
			Arguments = $"init --bare --initial-branch={(IsValidBranchName(initialBranch) ? initialBranch : "main")} \"{repoPath}\"",
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
		};
		psi.Environment["HOME"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		ApplySafeDirectory(psi);

		using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git");
		await proc.WaitForExitAsync();
	}

	/// <summary>Makes <paramref name="targetPath"/> a standalone bare copy of <paramref name="sourcePath"/> (all branches and
	/// tags, no shared objects or hardlinks), so either one can later be deleted without affecting the other.
	/// Throws when git fails; the caller removes a half-written target.</summary>
	public async Task CloneBare(string sourcePath, string targetPath)
	{
		if (!Directory.Exists(sourcePath))
			throw new RepositoryDataMissingException(sourcePath);

		var psi = new ProcessStartInfo(_gitExe)
		{
			Arguments = $"clone --bare --no-hardlinks \"{sourcePath}\" \"{targetPath}\"",
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
		};
		psi.Environment["HOME"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		ApplySafeDirectory(psi);

		using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git");
		var stdoutTask = proc.StandardOutput.ReadToEndAsync();
		var stderrTask = proc.StandardError.ReadToEndAsync();
		await proc.WaitForExitAsync();
		await stdoutTask;
		var stderr = await stderrTask;
		if (proc.ExitCode != 0)
			throw new InvalidOperationException($"git clone exited {proc.ExitCode}: {stderr.Trim()}");

		// The clone remembers the source's path on disk as "origin"; a fork has no business knowing it.
		await RunGitAsync(targetPath, "remote remove origin");
	}

	// Keyed by executable path so a runtime git.exe switch (admin git-updater) picks up the
	// new version immediately instead of serving the previously active version's cached value.
	private static readonly ConcurrentDictionary<string, Lazy<Task<string>>> _versionCache = new();

	public Task<string> GetVersion()
	{
		var lazy = _versionCache.GetOrAdd(_gitExe, _ => new Lazy<Task<string>>(FetchVersion));
		return lazy.Value;
	}

	public const string NotInstalledVersion = "not installed";

	// The configured/active git.exe may be missing on disk (e.g. an admin deleted the install
	// folder, or a switched-to installation was removed outside the app). Process.Start throws
	// in that case rather than returning null, so this must not let that exception escape —
	// callers (e.g. the layout's version badge) treat this as a normal, non-fatal state.
	private async Task<string> FetchVersion()
	{
		var psi = new ProcessStartInfo(_gitExe)
		{
			Arguments = "--version",
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
		};

		Process proc;
		try
		{
			proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git");
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Could not start git executable at {path}", _gitExe);
			return NotInstalledVersion;
		}

		using (proc)
		{
			var stdout = await proc.StandardOutput.ReadToEndAsync();
			await proc.WaitForExitAsync();

			// "git version 2.45.1.windows.1" -> "2.45.1"
			var prefix = "git version ";
			var text = stdout.Trim();
			var version = text.StartsWith(prefix) ? text[prefix.Length..] : text;

			var windowsSuffixIndex = version.IndexOf(".windows.", StringComparison.OrdinalIgnoreCase);
			return windowsSuffixIndex >= 0 ? version[..windowsSuffixIndex] : version;
		}
	}

	public async Task<bool> IsEmpty(string repoPath)
	{
		var result = await RunGitAsync(repoPath, "rev-list --all --count");
		return !int.TryParse(result.Trim(), out var count) || count == 0;
	}

	/// <summary>A branch name safe to put on a git command line and valid as a ref: letters, digits, '.', '_', '-' and '/',
	/// not starting with '-' or '/', no "..", no trailing ".lock".</summary>
	public static bool IsValidBranchName(string name) =>
		System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z0-9._][A-Za-z0-9._/\-]*$") &&
		!name.Contains("..") && !name.Contains("//") && !name.EndsWith('/') && !name.EndsWith(".lock") && name.Length <= 200;

	/// <summary>Points HEAD (the branch a clone checks out) at <paramref name="branch"/>. False when the name is not valid.</summary>
	public async Task<bool> SetHead(string repoPath, string branch)
	{
		if (!IsValidBranchName(branch)) return false;
		await RunGitAsync(repoPath, $"symbolic-ref HEAD refs/heads/{branch}");
		return true;
	}

	/// <summary>Makes sure HEAD names a branch that exists, once there are branches: <paramref name="preferred"/> when it
	/// exists, otherwise the first one. Repairs repositories whose HEAD still says "master" while only "main" was
	/// pushed — a clone of those checked out nothing.</summary>
	public async Task EnsureHeadExists(string repoPath, string preferred)
	{
		var head = (await RunGitAsync(repoPath, "symbolic-ref HEAD")).Trim();
		var branches = await GetBranches(repoPath);
		if (branches.Count == 0 || (head.StartsWith("refs/heads/") && branches.Contains(head["refs/heads/".Length..]))) return;

		await SetHead(repoPath, branches.Contains(preferred) ? preferred : branches[0]);
	}

	public async Task<string> GetDefaultBranch(string repoPath)
	{
		var result = await RunGitAsync(repoPath, "symbolic-ref HEAD");
		var branch = result.Trim();
		var headBranch = branch.StartsWith("refs/heads/") ? branch["refs/heads/".Length..] : "main";

		// HEAD can point to a branch that no longer exists (e.g. "master" was never pushed,
		// only "main" was) — fall back to whatever branch actually exists.
		var branches = await GetBranches(repoPath);
		if (branches.Contains(headBranch)) return headBranch;
		return branches.FirstOrDefault() ?? headBranch;
	}

	public async Task<List<string>> GetBranches(string repoPath)
	{
		var result = await RunGitAsync(repoPath, "branch --format=%(refname:short)");
		return result.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
	}

	public async Task<List<string>> GetTags(string repoPath)
	{
		var result = await RunGitAsync(repoPath, "tag");
		return result.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).ToList();
	}

	/// <summary>Tags with their target commit and (for annotated tags) the tag message.
	/// "*objectname" resolves an annotated tag to the commit it points at; for a lightweight
	/// tag it is empty and "objectname" is already the commit.</summary>
	public async Task<List<TagInfo>> GetTagInfos(string repoPath)
	{
		var format = "%(refname:short)%1f%(objectname)%1f%(*objectname)%1f%(creatordate:iso-strict)%1f%(contents:subject)%1f%(objecttype)%1f%(taggername)%1f%(contents:body)%1e";
		var result = await RunGitAsync(repoPath, $"for-each-ref --sort=-creatordate --format=\"{format}\" refs/tags");

		var tags = new List<TagInfo>();
		foreach (var block in result.Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
		{
			var f = block.TrimStart('\n', '\r').Split('\x1f');
			if (f.Length < 8 || string.IsNullOrWhiteSpace(f[0])) continue;

			var annotated = f[5].Trim() == "tag";
			var commit = annotated && f[2].Trim().Length > 0 ? f[2].Trim() : f[1].Trim();
			var date = DateTime.TryParse(f[3].Trim(), out var d) ? d : DateTime.UtcNow;

			tags.Add(new TagInfo(
				f[0].Trim(), commit, commit.Length >= 7 ? commit[..7] : commit,
				date, f[4].Trim(), f[7].Trim('\n', '\r'), f[6].Trim(), annotated));
		}
		return tags;
	}

	/// <summary>Tag names per commit sha, for showing tag badges in a commit list.</summary>
	public async Task<Dictionary<string, List<string>>> GetTagsByCommit(string repoPath)
	{
		var tags = await GetTagInfos(repoPath);
		return tags
			.GroupBy(t => t.Sha)
			.ToDictionary(g => g.Key, g => g.Select(t => t.Name).ToList());
	}

	// The commit body is free-form multi-line text, so fields are separated by ASCII unit/record
	// separators (%x1f / %x1e) instead of newlines — those bytes can never appear in a commit.
	private const string CommitFormat = "--format=%H%x1f%h%x1f%s%x1f%an%x1f%ae%x1f%aI%x1f%T%x1f%P%x1f%b%x1e";

	private static CommitInfo ParseCommit(string block, string fallbackSha)
	{
		var f = block.Split('\x1f');
		string Field(int i) => f.ElementAtOrDefault(i) ?? "";
		var date = DateTime.TryParse(Field(5).Trim(), out var d) ? d : DateTime.UtcNow;
		return new CommitInfo(
			Field(0).Trim() is { Length: > 0 } sha ? sha : fallbackSha,
			Field(1).Trim(),
			Field(2).Trim(),
			Field(3).Trim(),
			Field(4).Trim(),
			date,
			Field(6).Trim(),
			Field(7).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList(),
			Field(8).Trim('\n', '\r'));
	}

	public async Task<List<CommitInfo>> GetCommitLog(string repoPath, string branch, int skip, int take)
	{
		var result = await RunGitAsync(repoPath, $"log {CommitFormat} --skip={skip} --max-count={take} {branch} --");

		return result
			.Split('\x1e', StringSplitOptions.RemoveEmptyEntries)
			.Select(b => b.TrimStart('\n', '\r'))
			.Where(b => b.Length > 0)
			.Select(b => ParseCommit(b, ""))
			.ToList();
	}

	/// <summary>Every branch and tag with the object it points at, e.g. "refs/heads/main" → sha.</summary>
	public async Task<Dictionary<string, string>> GetRefs(string repoPath)
	{
		var result = await RunGitAsync(repoPath, "for-each-ref \"--format=%(objectname) %(refname)\"");
		return result
			.Split('\n', StringSplitOptions.RemoveEmptyEntries)
			.Select(l => l.Trim().Split(' ', 2))
			.Where(p => p.Length == 2)
			.ToDictionary(p => p[1], p => p[0]);
	}

	/// <summary>The commits a ref move brought in (at most <paramref name="max"/>, oldest first): before..after,
	/// or the latest ones reachable from <paramref name="after"/> for a new ref.</summary>
	public async Task<List<CommitInfo>> GetCommitsBetween(string repoPath, string? before, string after, int max)
	{
		var range = before == null ? after : $"{before}..{after}";
		var result = await RunGitAsync(repoPath, $"log {CommitFormat} --max-count={max} {range} --");
		var commits = result
			.Split('\x1e', StringSplitOptions.RemoveEmptyEntries)
			.Select(b => b.TrimStart('\n', '\r'))
			.Where(b => b.Length > 0)
			.Select(b => ParseCommit(b, ""))
			.ToList();
		commits.Reverse();
		return commits;
	}

	public async Task<CommitDetail> GetCommitDetail(string repoPath, string sha)
	{
		var infoResult = await RunGitAsync(repoPath, $"log -1 {CommitFormat} {sha}");
		var info = ParseCommit(infoResult.Split('\x1e')[0], sha);

		var diff = await RunGitAsync(repoPath, $"show --stat --patch {sha}");
		var changedFiles = await RunGitAsync(repoPath, $"diff-tree --no-commit-id -r --name-only {sha}");

		return new CommitDetail(info, diff, changedFiles.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList());
	}

	public async Task<List<TreeEntry>> GetTree(string repoPath, string treeish, string path)
	{
		var treeishArg = string.IsNullOrEmpty(path) ? treeish : $"{treeish}:{path}";
		var result = await RunGitAsync(repoPath, $"ls-tree \"{treeishArg}\"");
		var entries = new List<TreeEntry>();

		foreach (var line in result.Split('\n', StringSplitOptions.RemoveEmptyEntries))
		{
			// format: <mode> <type> <sha>\t<name>
			var tabIdx = line.IndexOf('\t');
			if (tabIdx < 0) continue;
			var meta = line[..tabIdx].Split(' ');
			if (meta.Length < 3) continue;
			var name = line[(tabIdx + 1)..];
			var entryPath = string.IsNullOrEmpty(path) ? name : $"{path}/{name}";
			entries.Add(new TreeEntry(meta[0], meta[1], meta[2], name, entryPath));
		}

		// Sort: trees first, then blobs
		return entries.OrderBy(e => e.Type == "tree" ? 0 : 1).ThenBy(e => e.Name).ToList();
	}

	public async Task<string> GetFileContent(string repoPath, string treeish, string path)
	{
		return await RunGitAsync(repoPath, $"show \"{treeish}:{path}\"");
	}

	public async Task<long> GetFileSize(string repoPath, string treeish, string path)
	{
		var result = await RunGitAsync(repoPath, $"cat-file -s \"{treeish}:{path}\"");
		return long.TryParse(result.Trim(), out var size) ? size : 0;
	}

	public async Task StreamFileRaw(string repoPath, string treeish, string path, Stream responseStream)
	{
		var psi = new ProcessStartInfo(_gitExe)
		{
			Arguments = $"show \"{treeish}:{path}\"",
			WorkingDirectory = repoPath,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
		};
		psi.Environment["GIT_DIR"] = repoPath;
		psi.Environment["HOME"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		ApplySafeDirectory(psi);

		using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git");
		var stderrTask = proc.StandardError.ReadToEndAsync();
		await proc.StandardOutput.BaseStream.CopyToAsync(responseStream);
		await proc.WaitForExitAsync();
		await stderrTask;
	}

	public async Task StreamArchive(string repoPath, string treeish, Stream responseStream)
	{
		var psi = new ProcessStartInfo(_gitExe)
		{
			Arguments = $"archive --format=zip {treeish}",
			WorkingDirectory = repoPath,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
		};
		psi.Environment["HOME"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		ApplySafeDirectory(psi);

		using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git");
		var stderrTask = proc.StandardError.ReadToEndAsync();
		await proc.StandardOutput.BaseStream.CopyToAsync(responseStream);
		await proc.WaitForExitAsync();
		await stderrTask;
	}

	public async Task<int> GetCommitCount(string repoPath, string branch)
	{
		var result = await RunGitAsync(repoPath, $"rev-list --count {branch}");
		return int.TryParse(result.Trim(), out var count) ? count : 0;
	}

	public async Task<string> GetReadme(string repoPath, string treeish)
	{
		var tree = await GetTree(repoPath, treeish, "");
		var readme = tree.FirstOrDefault(e => e.Type == "blob" &&
			e.Name.StartsWith("README", StringComparison.OrdinalIgnoreCase));

		if (readme is null) return "";
		return await GetFileContent(repoPath, treeish, readme.Name);
	}

	// ---- Pull requests ------------------------------------------------------------------------

	public record GitResult(int ExitCode, string Output, string Error)
	{
		public bool Ok => ExitCode == 0;
	}

	/// <summary>Runs git with each argument passed as-is (no shell quoting to get wrong), optional stdin and extra
	/// environment variables, and returns the exit code with both output streams.</summary>
	private async Task<GitResult> RunGitDetailedAsync(string repoPath, IEnumerable<string> args, string? stdin = null, IDictionary<string, string>? env = null)
	{
		if (!Directory.Exists(repoPath))
			throw new RepositoryDataMissingException(repoPath);

		var psi = CreatePsi(repoPath, "");
		foreach (var a in args) psi.ArgumentList.Add(a);
		foreach (var (k, v) in env ?? new Dictionary<string, string>()) psi.Environment[k] = v;
		using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git process");

		var stdoutTask = proc.StandardOutput.ReadToEndAsync();
		var stderrTask = proc.StandardError.ReadToEndAsync();
		if (stdin != null) await proc.StandardInput.WriteAsync(stdin);
		proc.StandardInput.Close();
		await proc.WaitForExitAsync();
		return new GitResult(proc.ExitCode, await stdoutTask, await stderrTask);
	}

	/// <summary>The commit a ref or sha names, or null.</summary>
	public async Task<string?> ResolveCommit(string repoPath, string rev)
	{
		var r = await RunGitDetailedAsync(repoPath, ["rev-parse", "--verify", "--quiet", rev + "^{commit}"]);
		return r.Ok ? r.Output.Trim() : null;
	}

	/// <summary>Copies branch <paramref name="sourceBranch"/> of <paramref name="sourceRepoPath"/> (the same repository or a fork)
	/// into <paramref name="targetRepoPath"/> as <paramref name="refName"/>, e.g. refs/pull/7/head, so the pull request can be
	/// compared and merged inside the target alone. Returns the fetched commit, or null when the branch doesn't exist.</summary>
	public async Task<string?> FetchBranchAs(string targetRepoPath, string sourceRepoPath, string sourceBranch, string refName)
	{
		if (!IsValidBranchName(sourceBranch)) return null;
		var r = await RunGitDetailedAsync(targetRepoPath, ["fetch", "--no-tags", "--quiet", sourceRepoPath, $"+refs/heads/{sourceBranch}:{refName}"]);
		if (!r.Ok) _logger.LogInformation("Fetching {Branch} from {Source} failed: {Err}", sourceBranch, sourceRepoPath, r.Error.Trim());
		return r.Ok ? await ResolveCommit(targetRepoPath, refName) : null;
	}

	public async Task<string?> MergeBase(string repoPath, string a, string b)
	{
		var r = await RunGitDetailedAsync(repoPath, ["merge-base", a, b]);
		return r.Ok ? r.Output.Trim() : null;
	}

	/// <summary>The unified diff and changed file names between two commits.</summary>
	public async Task<(string Diff, List<string> Files)> GetDiffBetween(string repoPath, string from, string to)
	{
		var diff = await RunGitDetailedAsync(repoPath, ["diff", "--no-color", from, to]);
		var names = await RunGitDetailedAsync(repoPath, ["diff", "--name-only", from, to]);
		return (diff.Output, names.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList());
	}

	/// <summary>The tree a merge of <paramref name="theirs"/> into <paramref name="ours"/> would produce, or null when it conflicts.
	/// Works on the bare repository itself: no work tree, no checkout (git 2.38+).</summary>
	public async Task<string?> MergeTree(string repoPath, string ours, string theirs)
	{
		var r = await RunGitDetailedAsync(repoPath, ["merge-tree", "--write-tree", "--no-messages", ours, theirs]);
		return r.Ok ? r.Output.Split('\n')[0].Trim() : null;
	}

	/// <summary>Creates a commit with the given tree and parents, authored and committed by the given person.</summary>
	public async Task<string?> CommitTree(string repoPath, string tree, IEnumerable<string> parents, string message, string name, string email)
	{
		var args = new List<string> { "commit-tree", tree };
		foreach (var p in parents) { args.Add("-p"); args.Add(p); }
		args.Add("-F");
		args.Add("-");
		var env = new Dictionary<string, string>
		{
			["GIT_AUTHOR_NAME"] = name, ["GIT_AUTHOR_EMAIL"] = email,
			["GIT_COMMITTER_NAME"] = name, ["GIT_COMMITTER_EMAIL"] = email,
		};
		var r = await RunGitDetailedAsync(repoPath, args, message, env);
		if (!r.Ok) _logger.LogWarning("commit-tree failed: {Err}", r.Error.Trim());
		return r.Ok ? r.Output.Trim() : null;
	}

	/// <summary>Moves <paramref name="refName"/> to <paramref name="newSha"/>, but only if it still points at <paramref name="expectedOldSha"/>:
	/// a push that landed in the meantime makes this fail instead of being overwritten.</summary>
	public async Task<bool> UpdateRef(string repoPath, string refName, string newSha, string expectedOldSha) =>
		(await RunGitDetailedAsync(repoPath, ["update-ref", refName, newSha, expectedOldSha])).Ok;
}
