using System.Diagnostics;
using System.IO.Compression;
using GitServer.Data;
using GitServer.Models;
using Microsoft.EntityFrameworkCore;

namespace GitServer.Services;

public class GitInstallException(string message) : Exception(message);

/// <summary>Downloads, extracts and activates MinGit distributions for the admin git-updater
/// feature, so the app is no longer dependent on a pre-installed, system-wide git.exe.</summary>
public class GitInstallerService(
    IHttpClientFactory httpClientFactory,
    AppDbContext db,
    IGitExecutablePathProvider pathProvider,
    IWebHostEnvironment env,
    ILogger<GitInstallerService> logger)
{
    private string InstallRootPath => Path.Combine(env.ContentRootPath, "App_Data", "git");

    /// <summary>The real git.exe lives at mingw64\bin\git.exe (4+ MB); cmd\git.exe is a tiny stub
    /// that just re-execs it. Our server-side, stateless-rpc/plumbing-only usage needs nothing else
    /// from the distribution except mingw64\bin itself (git.exe's DLLs and the actual subcommand
    /// binaries live there) — cmd\, mingw64\libexec (submodule/subtree/mergetool scripts we never
    /// call), mingw64\doc, etc\ and usr\ can all go. mingw64\share\licenses is kept for attribution.</summary>
    public static string GetGitExePath(string installPath) => Path.Combine(installPath, "mingw64", "bin", "git.exe");

    /// <summary>The only part of a trimmed install kept for the admin "view licenses" browser.</summary>
    public static string GetLicensesPath(string installPath) => Path.Combine(installPath, "mingw64", "share", "licenses");

    private static readonly string[] RemovableBinPatterns =
    [
        "Avalonia.*.dll", "av_libglesv2.dll", "gcmcore.dll", "git-credential-manager.exe*",
        "Atlassian.Bitbucket.dll", "blocked-file-util.exe",
    ];

    public async Task<GitInstallation> DownloadAndInstallAsync(GitRelease release, IProgress<(long downloaded, long total)>? progress = null, CancellationToken ct = default)
    {
        if (release.AssetUrl is null)
            throw new GitInstallException("This release has no MinGit 64-bit asset.");

        if (await db.GitInstallations.AnyAsync(i => i.TagName == release.TagName, ct))
            throw new GitInstallException($"{release.TagName} is already installed.");

        var installPath = Path.Combine(InstallRootPath, release.TagName);
        if (Directory.Exists(installPath))
            throw new GitInstallException($"Install folder for {release.TagName} already exists.");

        var tempZip = Path.Combine(Path.GetTempPath(), $"mingit-{Guid.NewGuid():N}.zip");
        try
        {
            using (var client = httpClientFactory.CreateClient("GitHubReleases"))
            using (var response = await client.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? release.AssetSize;

                await using var httpStream = await response.Content.ReadAsStreamAsync(ct);
                await using var fileStream = File.Create(tempZip);

                var buffer = new byte[81920];
                long downloaded = 0;
                int read;
                while ((read = await httpStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                    downloaded += read;
                    progress?.Report((downloaded, totalBytes));
                }
            }

            Directory.CreateDirectory(installPath);
            ZipFile.ExtractToDirectory(tempZip, installPath);
            RemoveUnneededFiles(installPath);

            var gitExePath = GetGitExePath(installPath);
            if (!File.Exists(gitExePath))
                throw new GitInstallException($"Extracted archive did not contain mingw64\\bin\\git.exe.");

            var actualVersion = await GetExecutableVersionAsync(gitExePath, ct);
            logger.LogInformation("Installed MinGit {tag} at {path}, reports version {version}", release.TagName, installPath, actualVersion);

            var installation = new GitInstallation
            {
                Version = actualVersion,
                TagName = release.TagName,
                InstallPath = installPath,
                InstalledAt = DateTime.UtcNow,
                IsActive = false,
            };
            db.GitInstallations.Add(installation);
            await db.SaveChangesAsync(ct);

            // First install ever: make it active right away instead of leaving the admin with
            // a working download that git.exe still isn't actually configured to use.
            if (await db.GitInstallations.CountAsync(ct) == 1)
                await ActivateAsync(installation.Id, ct);

            return installation;
        }
        catch
        {
            if (Directory.Exists(installPath))
                Directory.Delete(installPath, recursive: true);
            throw;
        }
        finally
        {
            if (File.Exists(tempZip))
                File.Delete(tempZip);
        }
    }

    public async Task ActivateAsync(int installationId, CancellationToken ct = default)
    {
        var installation = await db.GitInstallations.FindAsync([installationId], ct)
            ?? throw new GitInstallException("Installation not found.");

        var gitExePath = GetGitExePath(installation.InstallPath);
        if (!File.Exists(gitExePath))
            throw new GitInstallException("Installation files are missing on disk.");

        await foreach (var other in db.GitInstallations.Where(i => i.IsActive).AsAsyncEnumerable().WithCancellation(ct))
            other.IsActive = false;

        installation.IsActive = true;
        await db.SaveChangesAsync(ct);

        pathProvider.SetPath(gitExePath);
    }

    public async Task DeleteAsync(int installationId, CancellationToken ct = default)
    {
        var installation = await db.GitInstallations.FindAsync([installationId], ct)
            ?? throw new GitInstallException("Installation not found.");

        // An active installation is normally protected (it's the one in use), but if its files
        // were already removed from disk outside the app there's nothing left to protect —
        // allow clearing the stale record so it stops being shown as the "active" version.
        var filesPresent = File.Exists(GetGitExePath(installation.InstallPath));
        if (installation.IsActive && filesPresent)
            throw new GitInstallException("Cannot delete the active git installation.");

        db.GitInstallations.Remove(installation);
        await db.SaveChangesAsync(ct);

        if (Directory.Exists(installation.InstallPath))
            Directory.Delete(installation.InstallPath, recursive: true);
    }

    /// <summary>Strips the extracted MinGit distribution down to just mingw64\bin (git.exe, its
    /// runtime DLLs and the actual subcommand binaries — everything our stateless-rpc/plumbing-only
    /// usage needs) plus mingw64\share\licenses (kept for attribution). Everything else — cmd\, the
    /// libexec scripts, docs, etc\, usr\ — is deleted.</summary>
    private void RemoveUnneededFiles(string installPath)
    {
        var mingw64Path = Path.Combine(installPath, "mingw64");

        foreach (var entry in Directory.GetFileSystemEntries(installPath))
        {
            if (string.Equals(entry, mingw64Path, StringComparison.OrdinalIgnoreCase)) continue;
            TryDelete(entry);
        }

        if (Directory.Exists(mingw64Path))
        {
            var sharePath = Path.Combine(mingw64Path, "share");
            foreach (var entry in Directory.GetFileSystemEntries(mingw64Path))
            {
                var name = Path.GetFileName(entry);
                if (name.Equals("bin", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Equals("share", StringComparison.OrdinalIgnoreCase)) continue;
                TryDelete(entry);
            }

            if (Directory.Exists(sharePath))
            {
                foreach (var entry in Directory.GetFileSystemEntries(sharePath))
                {
                    if (Path.GetFileName(entry).Equals("licenses", StringComparison.OrdinalIgnoreCase)) continue;
                    TryDelete(entry);
                }
            }
        }

        var binPath = Path.Combine(mingw64Path, "bin");
        if (!Directory.Exists(binPath)) return;

        foreach (var pattern in RemovableBinPatterns)
        {
            foreach (var file in Directory.GetFiles(binPath, pattern))
                TryDelete(file);
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not remove {path} from MinGit install", path);
        }
    }

    private static async Task<string> GetExecutableVersionAsync(string gitExePath, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(gitExePath)
        {
            Arguments = "--version",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi) ?? throw new GitInstallException("Failed to start the extracted git.exe for verification.");
        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0)
            throw new GitInstallException("Extracted git.exe did not run successfully (--version failed).");

        const string prefix = "git version ";
        var text = stdout.Trim();
        return text.StartsWith(prefix) ? text[prefix.Length..] : text;
    }
}
