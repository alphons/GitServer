using System.Collections.Concurrent;

namespace GitServer.Services;

public record GitInstallProgress(long BytesDownloaded, long TotalBytes, bool Completed, bool Failed, string? Error, string? InstalledVersion);

/// <summary>In-memory progress state for background MinGit downloads, polled by the admin
/// UI while GitInstallerService.DownloadAndInstallAsync runs on a background task.</summary>
public class GitInstallProgressTracker
{
    private readonly ConcurrentDictionary<string, GitInstallProgress> _jobs = new();

    public string Start()
    {
        var jobId = Guid.NewGuid().ToString("N");
        _jobs[jobId] = new GitInstallProgress(0, 0, false, false, null, null);
        return jobId;
    }

    public void Report(string jobId, long bytesDownloaded, long totalBytes) =>
        _jobs[jobId] = _jobs[jobId] with { BytesDownloaded = bytesDownloaded, TotalBytes = totalBytes };

    public void Complete(string jobId, string installedVersion) =>
        _jobs[jobId] = _jobs[jobId] with { Completed = true, InstalledVersion = installedVersion };

    public void Fail(string jobId, string error) =>
        _jobs[jobId] = _jobs[jobId] with { Completed = true, Failed = true, Error = error };

    public GitInstallProgress? Get(string jobId) => _jobs.TryGetValue(jobId, out var p) ? p : null;
}
