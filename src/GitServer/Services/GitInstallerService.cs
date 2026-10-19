using System.Diagnostics;
using System.IO.Compression;
using GitServer.Data;
using GitServer.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GitServer.Services;

public class GitInstallException(string message) : Exception(message);

/// <summary>Downloads, extracts and activates MinGit distributions for the admin git-updater
/// feature, so the app is no longer dependent on a pre-installed, system-wide git.exe.</summary>
public class GitInstallerService(
	IHttpClientFactory httpClientFactory,
	AppDbContext db,
	IGitExecutablePathProvider pathProvider,
	IWebHostEnvironment env,
	IOptions<GitServerOptions> options,
	GitReleaseService releases,
	ILogger<GitInstallerService> logger)
{
	public const string SystemManagedMessage = "Git is provided by the operating system or the GitExecutable setting here; the MinGit installer is only for Windows.";

	private string InstallRootPath
	{
		get
		{
			return ConfigPaths.Resolve(options.Value.GitExecutableInstallRoot, "GitServer:GitExecutableInstallRoot", env.ContentRootPath);
		}
	}

	/// <summary>The real git.exe lives at mingw64\bin\git.exe (4+ MB); cmd\git.exe is a tiny stub
	/// that just re-execs it.</summary>
	public static string GetGitExePath(string installPath) => Path.Combine(GetPrefixPath(installPath), "bin", "git.exe");

	/// <summary>The toolchain folder of a MinGit distribution: mingw64 up to git 2.55, ucrt64 from 2.56 on
	/// (clangarm64 in older arm64 builds). Falls back to mingw64 when none exists yet.</summary>
	private static readonly string[] PrefixFolders = ["ucrt64", "mingw64", "clangarm64"];

	public static string GetPrefixPath(string installPath) =>
		Path.Combine(installPath, PrefixFolders.FirstOrDefault(f => Directory.Exists(Path.Combine(installPath, f))) ?? "mingw64");

	/// <summary>Where the admin "view licenses" browser reads from.</summary>
	public static string GetLicensesPath(string installPath) => Path.Combine(GetPrefixPath(installPath), "share", "licenses");

	public async Task<GitInstallation> DownloadAndInstallAsync(GitRelease release, IProgress<(long downloaded, long total)>? progress = null, CancellationToken ct = default)
	{
		if (await db.GitInstallations.AnyAsync(i => i.TagName == release.TagName, ct))
			throw new GitInstallException($"{release.TagName} is already installed.");

		return await DownloadCoreAsync(release, wasActive: false, progress, ct);
	}

	/// <summary>Re-downloads and re-extracts an already-installed version over itself — e.g. to pick up a fix to the
	/// install/extraction logic that an existing install predates (its files on disk are unaffected by a code change).
	/// Keeps the installation active if it was active before.</summary>
	public async Task<GitInstallation> ReinstallAsync(int installationId, CancellationToken ct = default)
	{
		var installation = await db.GitInstallations.FindAsync([installationId], ct)
			?? throw new GitInstallException("Installation not found.");

		var release = await releases.GetReleaseByTagAsync(installation.TagName, ct)
			?? throw new GitInstallException($"Could not find the {installation.TagName} release on GitHub anymore.");

		var wasActive = installation.IsActive;
		db.GitInstallations.Remove(installation);
		await db.SaveChangesAsync(ct);
		if (Directory.Exists(installation.InstallPath))
			DeleteDirectory(installation.InstallPath);

		return await DownloadCoreAsync(release, wasActive, progress: null, ct);
	}

	private async Task<GitInstallation> DownloadCoreAsync(GitRelease release, bool wasActive, IProgress<(long downloaded, long total)>? progress, CancellationToken ct)
	{
		if (!pathProvider.IsManagedByInstaller)
			throw new GitInstallException(SystemManagedMessage);

		if (release.AssetUrl is null)
			throw new GitInstallException("This release has no MinGit 64-bit asset.");

		var installPath = Path.Combine(InstallRootPath, release.TagName);
		// The folder can outlive its database row (a fresh or reset database, a switch to SQL Server). A working
		// install is simply registered again; a broken leftover (e.g. an interrupted extract) is replaced.
		if (Directory.Exists(installPath))
		{
			var existingExe = GetGitExePath(installPath);
			if (File.Exists(existingExe))
			{
				try
				{
					var existingVersion = await GetExecutableVersionAsync(existingExe, ct);
					logger.LogInformation("Adopting existing MinGit {tag} at {path}, reports version {version}", release.TagName, installPath, existingVersion);
					return await RegisterAsync(release.TagName, installPath, existingVersion, wasActive, ct);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					logger.LogWarning(ex, "Existing MinGit folder {path} does not work; reinstalling", installPath);
				}
			}
			DeleteDirectory(installPath);
		}

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

			var gitExePath = GetGitExePath(installPath);
			if (!File.Exists(gitExePath))
				throw new GitInstallException("Extracted archive did not contain bin\\git.exe in a mingw64 or ucrt64 folder.");

			var actualVersion = await GetExecutableVersionAsync(gitExePath, ct);
			logger.LogInformation("Installed MinGit {tag} at {path}, reports version {version}", release.TagName, installPath, actualVersion);

			return await RegisterAsync(release.TagName, installPath, actualVersion, wasActive, ct);
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

	private async Task<GitInstallation> RegisterAsync(string tagName, string installPath, string version, bool wasActive, CancellationToken ct)
	{
		var installation = new GitInstallation
		{
			Version = version,
			TagName = tagName,
			InstallPath = installPath,
			InstalledAt = DateTime.UtcNow,
			IsActive = false,
		};
		db.GitInstallations.Add(installation);
		await db.SaveChangesAsync(ct);

		// First install ever, or re-installing the one that was active: make it active right away
		// instead of leaving the admin with a working download that git.exe isn't configured to use.
		if (wasActive || await db.GitInstallations.CountAsync(ct) == 1)
			await ActivateAsync(installation.Id, ct);

		return installation;
	}

	private static void DeleteDirectory(string path)
	{
		foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
			File.SetAttributes(file, FileAttributes.Normal);
		Directory.Delete(path, recursive: true);
	}

	public async Task ActivateAsync(int installationId, CancellationToken ct = default)
	{
		if (!pathProvider.IsManagedByInstaller)
			throw new GitInstallException(SystemManagedMessage);

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
