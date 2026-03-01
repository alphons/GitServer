using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;

namespace GitServer.Services;

public record CommitInfo(string Sha, string ShortSha, string Message, string Author, string Email, DateTime Date);
public record CommitDetail(CommitInfo Info, string Diff, List<string> ChangedFiles);
public record TreeEntry(string Mode, string Type, string Sha, string Name, string Path);

public class GitProcessService
{
    private readonly string _gitExe;
    private readonly ILogger<GitProcessService> _logger;

    public GitProcessService(IOptions<GitServerOptions> options, ILogger<GitProcessService> logger)
    {
        _gitExe = options.Value.GitExecutable;
        _logger = logger;
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
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.Environment["GIT_HTTP_EXPORT_ALL"] = "1";
        psi.Environment["HOME"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        psi.Environment["GIT_DIR"] = repoPath;
        return psi;
    }

    private async Task<string> RunGitAsync(string repoPath, string arguments)
    {
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
        await stderrTask;
    }

    public async Task InitBare(string repoPath)
    {
        Directory.CreateDirectory(repoPath);
        var psi = new ProcessStartInfo(_gitExe)
        {
            Arguments = $"init --bare \"{repoPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.Environment["HOME"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git");
        await proc.WaitForExitAsync();
    }

    public async Task<bool> IsEmpty(string repoPath)
    {
        var result = await RunGitAsync(repoPath, "rev-list --all --count");
        return !int.TryParse(result.Trim(), out var count) || count == 0;
    }

    public async Task<string> GetDefaultBranch(string repoPath)
    {
        var result = await RunGitAsync(repoPath, "symbolic-ref HEAD");
        var branch = result.Trim();
        return branch.StartsWith("refs/heads/") ? branch["refs/heads/".Length..] : "main";
    }

    public async Task<List<string>> GetBranches(string repoPath)
    {
        var result = await RunGitAsync(repoPath, "branch --format=%(refname:short)");
        return result.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    public async Task<List<string>> GetTags(string repoPath)
    {
        var result = await RunGitAsync(repoPath, "tag");
        return result.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    public async Task<List<CommitInfo>> GetCommitLog(string repoPath, string branch, int skip, int take)
    {
        var format = "--format=%H%n%h%n%s%n%an%n%ae%n%aI%n---COMMIT---";
        var result = await RunGitAsync(repoPath, $"log {format} --skip={skip} --max-count={take} {branch} --");

        var commits = new List<CommitInfo>();
        var blocks = result.Split("---COMMIT---\n", StringSplitOptions.RemoveEmptyEntries);

        foreach (var block in blocks)
        {
            var lines = block.Split('\n');
            if (lines.Length < 6) continue;
            var date = DateTime.TryParse(lines[5].Trim(), out var d) ? d : DateTime.UtcNow;
            commits.Add(new CommitInfo(lines[0].Trim(), lines[1].Trim(), lines[2].Trim(), lines[3].Trim(), lines[4].Trim(), date));
        }

        return commits;
    }

    public async Task<CommitDetail> GetCommitDetail(string repoPath, string sha)
    {
        var infoResult = await RunGitAsync(repoPath, $"log -1 --format=%H%n%h%n%s%n%an%n%ae%n%aI {sha}");
        var lines = infoResult.Split('\n');
        var date = DateTime.TryParse(lines.ElementAtOrDefault(5)?.Trim(), out var d) ? d : DateTime.UtcNow;
        var info = new CommitInfo(
            lines.ElementAtOrDefault(0)?.Trim() ?? sha,
            lines.ElementAtOrDefault(1)?.Trim() ?? sha[..7],
            lines.ElementAtOrDefault(2)?.Trim() ?? "",
            lines.ElementAtOrDefault(3)?.Trim() ?? "",
            lines.ElementAtOrDefault(4)?.Trim() ?? "",
            date);

        var diff = await RunGitAsync(repoPath, $"show --stat --patch {sha}");
        var changedFiles = await RunGitAsync(repoPath, $"diff-tree --no-commit-id -r --name-only {sha}");

        return new CommitDetail(info, diff, changedFiles.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList());
    }

    public async Task<List<TreeEntry>> GetTree(string repoPath, string treeish, string path)
    {
        var pathArg = string.IsNullOrEmpty(path) ? "" : $":{path}";
        var result = await RunGitAsync(repoPath, $"ls-tree {treeish}{pathArg}");
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
        return await RunGitAsync(repoPath, $"show {treeish}:{path}");
    }

    public async Task<long> GetFileSize(string repoPath, string treeish, string path)
    {
        var result = await RunGitAsync(repoPath, $"cat-file -s {treeish}:{path}");
        return long.TryParse(result.Trim(), out var size) ? size : 0;
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
}
