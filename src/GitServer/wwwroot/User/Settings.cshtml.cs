using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.wwwroot.User;

[Authorize]
public class UserSettingsModel(UserManager<AppUser> userManager, LocalizationService L) : PageModel
{
	public AppUser? CurrentUser { get; set; }
	public string DisplayName { get; set; } = "";
	public string? Bio { get; set; }
	public string? AvatarUrl { get; set; }
	public string? Country { get; set; }
	public string? CompanyName { get; set; }
	public string? PreferredLanguage { get; set; }
	public string? Message { get; set; }
	public bool IsError { get; set; }
	public bool HasPassword { get; set; }

	[BindProperty] public string NewDisplayName { get; set; } = "";
	[BindProperty] public string? NewBio { get; set; }
	[BindProperty] public string? NewAvatarUrl { get; set; }
	[BindProperty] public string? NewCountry { get; set; }
	[BindProperty] public string? NewCompanyName { get; set; }
	[BindProperty] public string? NewPreferredLanguage { get; set; }
	[BindProperty] public string CurrentPassword { get; set; } = "";
	[BindProperty] public string NewPassword { get; set; } = "";

	private void LoadFrom(AppUser user)
	{
		CurrentUser = user;
		DisplayName = user.DisplayName;
		Bio = user.Bio;
		AvatarUrl = user.AvatarUrl;
		Country = user.Country;
		CompanyName = user.CompanyName;
		PreferredLanguage = user.PreferredLanguage;
	}

	public async Task OnGetAsync()
	{
		var user = await userManager.GetUserAsync(User);
		if (user == null) return;
		LoadFrom(user);
		HasPassword = await userManager.HasPasswordAsync(user);
	}

	public async Task<IActionResult> OnPostProfileAsync()
	{
		var user = await userManager.GetUserAsync(User);
		if (user == null) return NotFound();

		user.DisplayName = NewDisplayName;
		user.Bio = NewBio;
		user.AvatarUrl = NewAvatarUrl;
		user.Country = string.IsNullOrWhiteSpace(NewCountry) ? null : NewCountry.Trim();
		user.CompanyName = string.IsNullOrWhiteSpace(NewCompanyName) ? null : NewCompanyName.Trim();
		user.PreferredLanguage = string.IsNullOrWhiteSpace(NewPreferredLanguage) ? null : NewPreferredLanguage;

		var result = await userManager.UpdateAsync(user);
		Message = result.Succeeded ? L["success_profile_saved"] : string.Join(" ", result.Errors.Select(e => e.Description));
		IsError = !result.Succeeded;

		LoadFrom(user);
		HasPassword = await userManager.HasPasswordAsync(user);
		return Page();
	}

	public async Task<IActionResult> OnPostPasswordAsync()
	{
		var user = await userManager.GetUserAsync(User);
		if (user == null) return NotFound();

		LoadFrom(user);
		HasPassword = await userManager.HasPasswordAsync(user);

		if (string.IsNullOrEmpty(NewPassword))
		{
			Message = L["error_new_password_required"];
			IsError = true;
			return Page();
		}

		IdentityResult result;
		if (HasPassword)
			result = await userManager.ChangePasswordAsync(user, CurrentPassword, NewPassword);
		else
			result = await userManager.AddPasswordAsync(user, NewPassword);

		Message = result.Succeeded ? L["success_password_changed"] : string.Join(" ", result.Errors.Select(e => e.Description));
		IsError = !result.Succeeded;

		HasPassword = await userManager.HasPasswordAsync(user);
		return Page();
	}
}
