using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.Pages.Admin;

public class SettingsModel(
	UserManager<AppUser> userManager,
	SiteSettingsService siteSettings,
	LocalizationService L) : PageModel
{
	[BindProperty] public bool AllowRegistration { get; set; }
	[BindProperty] public bool AllowUserRepoCreation { get; set; }
	[BindProperty] public bool AllowPushToCreateRepositories { get; set; }
	[BindProperty] public bool AllowAnonymousPush { get; set; }
	[BindProperty] public bool ShowCommitAuthorAvatar { get; set; }
	public string? Message { get; set; }

	private async Task<bool> RequireAdminAsync()
	{
		var currentUser = await userManager.GetUserAsync(User);
		return currentUser != null && currentUser.IsAdmin;
	}

	private async Task LoadAsync()
	{
		var settings = await siteSettings.GetAsync();
		AllowRegistration = settings.AllowRegistration;
		AllowUserRepoCreation = settings.AllowUserRepoCreation;
		AllowPushToCreateRepositories = settings.AllowPushToCreateRepositories;
		AllowAnonymousPush = settings.AllowAnonymousPush;
		ShowCommitAuthorAvatar = settings.ShowCommitAuthorAvatar;
	}

	public async Task<IActionResult> OnGetAsync()
	{
		if (!await RequireAdminAsync()) return Forbid();

		await LoadAsync();
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
		});

		Message = L["admin_settings_saved"];
		return Page();
	}
}
