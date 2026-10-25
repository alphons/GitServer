using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GitServer.Pages.Admin;

public class SettingsModel(
	UserManager<AppUser> userManager,
	SiteSettingsService siteSettings,
	AuditService audit,
	LocalizationService L,
	IOptions<GitServerOptions> gitOptions,
	AppDbContext db) : PageModel
{
	[BindProperty] public bool AllowRegistration { get; set; }
	[BindProperty] public bool AllowUserRepoCreation { get; set; }
	[BindProperty] public bool AllowPushToCreateRepositories { get; set; }
	[BindProperty] public bool AllowAnonymousPush { get; set; }
	[BindProperty] public bool ShowCommitAuthorAvatar { get; set; }
	[BindProperty] public bool AllowWebhooksToPrivateNetworks { get; set; }
	[BindProperty] public int ApiKeyLifetimeDays { get; set; } = 90;
	public string? Message { get; set; }

	/// <summary>Read-only: where the repositories live (set in configuration, not editable here).</summary>
	public string RepositoriesPath { get; private set; } = "";

	/// <summary>Read-only: full path of the SQLite database file; null when another provider is in use.</summary>
	public string? SqliteDatabasePath { get; private set; }

	/// <summary>Result of trying to write to a path: <see cref="Missing"/> when the folder does not exist, otherwise <see cref="Error"/> holds why writing failed.</summary>
	public record PathCheck(bool Ok, bool Missing = false, string? Error = null);

	public PathCheck RepositoriesCheck { get; private set; } = new(false);
	public PathCheck? DatabaseCheck { get; private set; }

	/// <summary>Writes and deletes a small temp file, so it reflects what the app's own account can really do (not just ACLs).</summary>
	private static PathCheck CheckFolderWritable(string folder)
	{
		if (!Directory.Exists(folder)) return new PathCheck(false, Missing: true);
		try
		{
			var probe = Path.Combine(folder, $".write-test-{Guid.NewGuid():N}.tmp");
			System.IO.File.WriteAllText(probe, "");
			System.IO.File.Delete(probe);
			return new PathCheck(true);
		}
		catch (Exception ex)
		{
			return new PathCheck(false, Error: ex.Message);
		}
	}

	/// <summary>SQLite needs a writable folder (for the -wal/-shm files) and a writable database file.</summary>
	private static PathCheck CheckDatabaseWritable(string file)
	{
		var folder = Path.GetDirectoryName(file)!;
		var folderCheck = CheckFolderWritable(folder);
		if (!folderCheck.Ok) return folderCheck;
		if (!System.IO.File.Exists(file)) return new PathCheck(false, Missing: true);
		try
		{
			using var stream = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
			return new PathCheck(true);
		}
		catch (Exception ex)
		{
			return new PathCheck(false, Error: ex.Message);
		}
	}

	private async Task<bool> RequireAdminAsync()
	{
		var currentUser = await userManager.GetUserAsync(User);
		return AccessPolicy.IsSiteAdmin(currentUser);
	}

	private async Task LoadAsync()
	{
		var settings = await siteSettings.GetAsync();
		AllowRegistration = settings.AllowRegistration;
		AllowUserRepoCreation = settings.AllowUserRepoCreation;
		AllowPushToCreateRepositories = settings.AllowPushToCreateRepositories;
		AllowAnonymousPush = settings.AllowAnonymousPush;
		ShowCommitAuthorAvatar = settings.ShowCommitAuthorAvatar;
		AllowWebhooksToPrivateNetworks = settings.AllowWebhooksToPrivateNetworks;
		ApiKeyLifetimeDays = settings.ApiKeyLifetimeDays;
	}

	private void LoadPaths()
	{
		RepositoriesPath = gitOptions.Value.RepositoriesPath;
		RepositoriesCheck = CheckFolderWritable(RepositoriesPath);
		if (db.Database.IsSqlite())
		{
			var file = db.Database.GetDbConnection().DataSource;
			SqliteDatabasePath = string.IsNullOrEmpty(file) ? null : Path.GetFullPath(file);
			if (SqliteDatabasePath != null) DatabaseCheck = CheckDatabaseWritable(SqliteDatabasePath);
		}
	}

	public async Task<IActionResult> OnGetAsync()
	{
		if (!await RequireAdminAsync()) return Forbid();

		await LoadAsync();
		LoadPaths();
		return Page();
	}

	public async Task<IActionResult> OnPostAsync()
	{
		if (!await RequireAdminAsync()) return Forbid();

		await siteSettings.SaveAsync(new SiteSettings
		{
			AllowRegistration = AllowRegistration,
			AllowUserRepoCreation = AllowUserRepoCreation,
			AllowPushToCreateRepositories = AllowPushToCreateRepositories,
			AllowAnonymousPush = AllowAnonymousPush,
			ShowCommitAuthorAvatar = ShowCommitAuthorAvatar,
			AllowWebhooksToPrivateNetworks = AllowWebhooksToPrivateNetworks,
			ApiKeyLifetimeDays = ApiKeyLifetimeDays,
		});

		await audit.WriteAsync("settings.update", null,
			$"registration={AllowRegistration}, userRepos={AllowUserRepoCreation}, pushCreate={AllowPushToCreateRepositories}, anonPush={AllowAnonymousPush}, avatar={ShowCommitAuthorAvatar}, privateWebhooks={AllowWebhooksToPrivateNetworks}, apiKeyDays={ApiKeyLifetimeDays}");
		Message = L["admin_settings_saved"];
		LoadPaths();
		return Page();
	}
}
