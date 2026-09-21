using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GitServer.Pages.Admin;

public class GitVersionModel(
	UserManager<AppUser> userManager,
	AppDbContext db,
	GitProcessService gitProcess,
	GitReleaseService gitReleases,
	GitInstallerService gitInstaller,
	AuditService audit,
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
		return AccessPolicy.IsSiteAdmin(currentUser);
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

	public async Task<IActionResult> OnPostActivateAsync(int id)
	{
		if (!await RequireAdminAsync()) return Forbid();

		try
		{
			await gitInstaller.ActivateAsync(id);
			Message = L["admin_gitversion_activated"];
			await audit.WriteAsync("git.activate", "#" + id);
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
			await audit.WriteAsync("git.delete", "#" + id);
		}
		catch (GitInstallException ex)
		{
			Message = ex.Message;
			IsError = true;
		}

		await ReloadAsync();
		return Page();
	}
}
