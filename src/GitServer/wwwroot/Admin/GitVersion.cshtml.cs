using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GitServer.wwwroot.Admin;

public class GitVersionModel(
	UserManager<AppUser> userManager,
	AppDbContext db,
	GitProcessService gitProcess,
	GitReleaseService gitReleases,
	GitInstallerService gitInstaller,
	GitInstallProgressTracker progressTracker,
	IServiceScopeFactory scopeFactory,
	LocalizationService L) : PageModel
{
	public List<GitInstallation> Installations { get; set; } = new();
	public Dictionary<int, bool> FilesPresent { get; set; } = new();
	public string CurrentVersion { get; set; } = "";
	public GitRelease? LatestRelease { get; set; }
	public string? Message { get; set; }
	public bool IsError { get; set; }

	private async Task<bool> RequireAdminAsync()
	{
		var currentUser = await userManager.GetUserAsync(User);
		return currentUser != null && currentUser.IsAdmin;
	}

	private async Task ReloadAsync()
	{
		var all = await db.GitInstallations.OrderByDescending(i => i.InstalledAt).ToListAsync();

		// The DB record can outlive the files (e.g. an admin deleted the install folder by hand),
		// so check disk on every load rather than trusting the stored row alone — and drop rows
		// whose files are gone instead of leaving stale "ghost" entries around.
		var filesPresent = all.ToDictionary(i => i.Id, i => System.IO.File.Exists(GitInstallerService.GetGitExePath(i.InstallPath)));
		var missing = all.Where(i => !filesPresent[i.Id]).ToList();

		if (missing.Count > 0)
		{
			db.GitInstallations.RemoveRange(missing);
			await db.SaveChangesAsync();

			var missingIds = missing.Select(i => i.Id).ToHashSet();
			all = all.Where(i => !missingIds.Contains(i.Id)).ToList();
			foreach (var id in missingIds) filesPresent.Remove(id);

			if (string.IsNullOrEmpty(Message))
				Message = L.Format("admin_gitversion_missing_removed", missing.Count);
		}

		Installations = all;
		FilesPresent = filesPresent;
		CurrentVersion = await gitProcess.GetVersion();
	}

	public async Task<IActionResult> OnGetAsync()
	{
		if (!await RequireAdminAsync()) return Forbid();

		await ReloadAsync();
		return Page();
	}

	public async Task<IActionResult> OnPostCheckUpdateAsync()
	{
		if (!await RequireAdminAsync()) return Forbid();

		LatestRelease = await gitReleases.GetLatestReleaseAsync();
		if (LatestRelease == null)
		{
			Message = L["admin_gitversion_check_failed"];
			IsError = true;
		}

		await ReloadAsync();
		return Page();
	}

	// Starts the download/install in the background and hands back a jobId the page polls via
	// OnGetInstallProgressAsync, so the UI can show a live progress bar instead of blocking the request.
	public async Task<IActionResult> OnPostStartInstallAsync(string tagName)
	{
		if (!await RequireAdminAsync()) return Forbid();

		var jobId = progressTracker.Start();

		_ = Task.Run(async () =>
		{
			using var scope = scopeFactory.CreateScope();
			var releases = scope.ServiceProvider.GetRequiredService<GitReleaseService>();
			var installer = scope.ServiceProvider.GetRequiredService<GitInstallerService>();
			var loc = scope.ServiceProvider.GetRequiredService<LocalizationService>();

			try
			{
				var release = await releases.GetLatestReleaseAsync();
				if (release == null || release.TagName != tagName)
					throw new GitInstallException(loc["admin_gitversion_check_failed"]);

				var progress = new Progress<(long downloaded, long total)>(p =>
					progressTracker.Report(jobId, p.downloaded, p.total));

				var installation = await installer.DownloadAndInstallAsync(release, progress);
				progressTracker.Complete(jobId, installation.Version);
			}
			catch (Exception ex)
			{
				progressTracker.Fail(jobId, ex is GitInstallException ? ex.Message : "Install failed.");
			}
		});

		return new JsonResult(new { jobId });
	}

	public IActionResult OnGetInstallProgress(string jobId)
	{
		var progress = progressTracker.Get(jobId);
		if (progress == null) return NotFound();

		return new JsonResult(new
		{
			progress.BytesDownloaded,
			progress.TotalBytes,
			progress.Completed,
			progress.Failed,
			progress.Error,
			progress.InstalledVersion,
		});
	}

	public async Task<IActionResult> OnPostActivateAsync(int id)
	{
		if (!await RequireAdminAsync()) return Forbid();

		try
		{
			await gitInstaller.ActivateAsync(id);
			Message = L["admin_gitversion_activated"];
		}
		catch (GitInstallException ex)
		{
			Message = ex.Message;
			IsError = true;
		}

		await ReloadAsync();
		return Page();
	}

	public async Task<IActionResult> OnPostDeleteAsync(int id)
	{
		if (!await RequireAdminAsync()) return Forbid();

		try
		{
			await gitInstaller.DeleteAsync(id);
			Message = L["admin_gitversion_deleted"];
		}
		catch (GitInstallException ex)
		{
			Message = ex.Message;
			IsError = true;
		}

		await ReloadAsync();
		return Page();
	}

	// Lists the entries of one subdirectory of an installation's mingw64\share\licenses folder
	// (the only part of a trimmed install kept around), for the license-browser modal. "path" is
	// relative to that folder and is confined there — it can go deeper, never above it (see ResolveSafePath).
	public async Task<IActionResult> OnGetBrowseAsync(int id, string? path)
	{
		if (!await RequireAdminAsync()) return Forbid();

		var installation = await db.GitInstallations.FindAsync(id);
		if (installation == null) return NotFound();

		var (fullPath, ok) = ResolveSafePath(GitInstallerService.GetLicensesPath(installation.InstallPath), path);
		if (!ok || !Directory.Exists(fullPath)) return NotFound();

		var entries = new DirectoryInfo(fullPath).GetFileSystemInfos()
			.Select(e => new
			{
				name = e.Name,
				isDirectory = e is DirectoryInfo,
				size = e is FileInfo f ? f.Length : (long?)null,
			})
			.OrderByDescending(e => e.isDirectory)
			.ThenBy(e => e.name, StringComparer.OrdinalIgnoreCase)
			.ToList();

		return new JsonResult(entries);
	}

	// Returns one file's content as text for the browser modal's preview pane.
	public async Task<IActionResult> OnGetFileContentAsync(int id, string path)
	{
		if (!await RequireAdminAsync()) return Forbid();

		var installation = await db.GitInstallations.FindAsync(id);
		if (installation == null) return NotFound();

		var (fullPath, ok) = ResolveSafePath(GitInstallerService.GetLicensesPath(installation.InstallPath), path);
		if (!ok || !System.IO.File.Exists(fullPath)) return NotFound();

		const long maxPreviewSize = 2 * 1024 * 1024;
		var info = new FileInfo(fullPath);
		if (info.Length > maxPreviewSize)
			return new JsonResult(new { error = L["admin_gitversion_file_too_large"] });

		var bytes = await System.IO.File.ReadAllBytesAsync(fullPath);
		if (bytes.Take(8000).Any(b => b == 0))
			return new JsonResult(new { error = L["admin_gitversion_file_binary"] });

		return new JsonResult(new { content = System.Text.Encoding.UTF8.GetString(bytes) });
	}

	/// <summary>Resolves a browser-supplied relative path against an installation's root, rejecting
	/// anything (via "..", absolute paths, etc.) that would resolve outside of it.</summary>
	private static (string fullPath, bool ok) ResolveSafePath(string installPath, string? relativePath)
	{
		var root = Path.GetFullPath(installPath);
		if (!root.EndsWith(Path.DirectorySeparatorChar)) root += Path.DirectorySeparatorChar;

		var combined = Path.GetFullPath(Path.Combine(root, relativePath ?? ""));
		var ok = combined.Equals(root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
			|| combined.StartsWith(root, StringComparison.OrdinalIgnoreCase);
		return (combined, ok);
	}
}
