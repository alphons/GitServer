using GitServer.Data;
using GitServer.Extensions;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GitServer.Controllers.Api;

/// <summary>Git installation management (site admins only): the install job and the license browser.</summary>
[ApiController]
[Authorize]
[ApiAntiforgery]
[Route("api/admin/git")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class AdminGitApiController(
	UserManager<AppUser> userManager, AppDbContext db,
	GitInstallProgressTracker progressTracker, IServiceScopeFactory scopeFactory,
	AuditService audit, LocalizationService L) : ControllerBase
{
	private async Task<bool> IsAdminAsync() => AccessPolicy.IsSiteAdmin(await userManager.GetUserAsync(User));

	/// <summary>Starts downloading and installing the latest MinGit release in the background and returns a job id;
	/// poll <c>GET installs/{jobId}</c> for the progress, so a UI can show a live progress bar instead of blocking the request.</summary>
	[HttpPost("installs")]
	[ProducesResponseType<StartInstallResponse>(StatusCodes.Status200OK)]
	public async Task<ActionResult<StartInstallResponse>> StartInstall([FromBody] StartInstallRequest request)
	{
		if (!await IsAdminAsync()) return Forbid();

		var tagName = request.TagName;
		var jobId = progressTracker.Start();
		await audit.WriteAsync("git.install", tagName);

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

		return new StartInstallResponse(jobId);
	}

	/// <summary>The progress of an install job started with <c>POST installs</c>.</summary>
	[HttpGet("installs/{jobId}")]
	[ProducesResponseType<GitInstallProgress>(StatusCodes.Status200OK)]
	public async Task<ActionResult<GitInstallProgress>> GetInstallJob(string jobId)
	{
		if (!await IsAdminAsync()) return Forbid();

		var progress = progressTracker.Get(jobId);
		if (progress == null) return NotFound();

		return progress;
	}

	/// <summary>Lists the entries of one subdirectory of an installation's mingw64\share\licenses folder (the only part of a
	/// trimmed install kept around), for the license browser. <paramref name="path"/> is relative to that folder and
	/// confined there: it can go deeper, never above it (see ResolveSafePath). Directories come first.</summary>
	[HttpGet("installations/{id:int}/entries")]
	[ProducesResponseType<IReadOnlyList<FileEntryDto>>(StatusCodes.Status200OK)]
	public async Task<ActionResult<IReadOnlyList<FileEntryDto>>> Browse(int id, string? path)
	{
		if (!await IsAdminAsync()) return Forbid();

		var installation = await db.GitInstallations.FindAsync(id);
		if (installation == null) return NotFound();

		var (fullPath, ok) = ResolveSafePath(GitInstallerService.GetLicensesPath(installation.InstallPath), path);
		if (!ok || !Directory.Exists(fullPath)) return NotFound();

		return new DirectoryInfo(fullPath).GetFileSystemInfos()
			.Select(e => new FileEntryDto(e.Name, e is DirectoryInfo, e is FileInfo f ? f.Length : null))
			.OrderByDescending(e => e.IsDirectory)
			.ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	/// <summary>Returns one file's content as text for the license browser's preview pane. Files over 2 MB and binary files
	/// are not returned; <c>error</c> then says why.</summary>
	[HttpGet("installations/{id:int}/file")]
	[ProducesResponseType<FileContentResponse>(StatusCodes.Status200OK)]
	public async Task<ActionResult<FileContentResponse>> FileContent(int id, string path)
	{
		if (!await IsAdminAsync()) return Forbid();

		var installation = await db.GitInstallations.FindAsync(id);
		if (installation == null) return NotFound();

		var (fullPath, ok) = ResolveSafePath(GitInstallerService.GetLicensesPath(installation.InstallPath), path);
		if (!ok || !System.IO.File.Exists(fullPath)) return NotFound();

		const long maxPreviewSize = 2 * 1024 * 1024;
		if (new FileInfo(fullPath).Length > maxPreviewSize)
			return new FileContentResponse(null, L["admin_gitversion_file_too_large"]);

		var bytes = await System.IO.File.ReadAllBytesAsync(fullPath);
		if (bytes.Take(8000).Any(b => b == 0))
			return new FileContentResponse(null, L["admin_gitversion_file_binary"]);

		return new FileContentResponse(System.Text.Encoding.UTF8.GetString(bytes), null);
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
