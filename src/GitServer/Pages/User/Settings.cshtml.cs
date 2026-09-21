using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GitServer.Pages.User;

[Authorize]
public class UserSettingsModel(UserManager<AppUser> userManager, SignInManager<AppUser> signInManager, AccountService accounts, AccessTokenService tokens, LocalizationService L) : PageModel
{
	public AppUser? CurrentUser { get; set; }
	public string DisplayName { get; set; } = "";
	public string? Bio { get; set; }
	public string? AvatarUrl { get; set; }
	public string? Country { get; set; }
	public string? CompanyName { get; set; }
	public string? PreferredLanguage { get; set; }
	public string? TimeZoneId { get; set; }
	public string? Message { get; set; }
	public bool IsError { get; set; }
	public bool HasPassword { get; set; }

	/// <summary>A token that was just created, shown once.</summary>
	public string? NewTokenValue { get; set; }

	[BindProperty] public string NewDisplayName { get; set; } = "";
	[BindProperty] public string? NewBio { get; set; }
	[BindProperty] public string? NewAvatarUrl { get; set; }
	[BindProperty] public string? NewCountry { get; set; }
	[BindProperty] public string? NewCompanyName { get; set; }
	[BindProperty] public string? NewPreferredLanguage { get; set; }
	[BindProperty] public string? NewTimeZoneId { get; set; }
	[BindProperty] public string CurrentPassword { get; set; } = "";
	[BindProperty] public string NewPassword { get; set; } = "";
	[BindProperty] public string TokenName { get; set; } = "";
	[BindProperty] public int? TokenValidDays { get; set; }

	private void LoadFrom(AppUser user)
	{
		CurrentUser = user;
		DisplayName = user.DisplayName;
		Bio = user.Bio;
		AvatarUrl = user.AvatarUrl;
		Country = user.Country;
		CompanyName = user.CompanyName;
		PreferredLanguage = user.PreferredLanguage;
		TimeZoneId = user.TimeZoneId;
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
		user.TimeZoneId = string.IsNullOrWhiteSpace(NewTimeZoneId) ? null : NewTimeZoneId;

		var result = await userManager.UpdateAsync(user);
		Message = result.Succeeded ? L["success_profile_saved"] : string.Join(" ", result.Errors.Select(e => e.Description));
		IsError = !result.Succeeded;

		if (result.Succeeded && !string.IsNullOrEmpty(user.TimeZoneId))
		{
			Response.Cookies.Append(TimeZoneService.CookieName, user.TimeZoneId, new CookieOptions
			{
				Expires = DateTimeOffset.UtcNow.AddYears(1),
				IsEssential = true,
				SameSite = SameSiteMode.Lax,
				HttpOnly = true
			});
		}

		LoadFrom(user);
		HasPassword = await userManager.HasPasswordAsync(user);
		return Page();
	}

	public async Task<IActionResult> OnPostCreateTokenAsync()
	{
		var user = await userManager.GetUserAsync(User);
		if (user == null) return NotFound();

		LoadFrom(user);
		HasPassword = await userManager.HasPasswordAsync(user);

		if (string.IsNullOrWhiteSpace(TokenName))
		{
			Message = L["error_token_name_required"];
			IsError = true;
			return Page();
		}

		NewTokenValue = await tokens.CreateAsync(user, TokenName.Trim(), TokenValidDays);
		Message = L["settings_tokens_created"];
		return Page();
	}

	public async Task<IActionResult> OnPostRevokeTokenAsync(int id)
	{
		var user = await userManager.GetUserAsync(User);
		if (user == null) return NotFound();

		await tokens.RevokeAsync(user.Id, id);
		return RedirectToPage();
	}

	public async Task<IActionResult> OnPostDeleteAccountAsync()
	{
		var user = await userManager.GetUserAsync(User);
		if (user == null) return NotFound();

		LoadFrom(user);
		HasPassword = await userManager.HasPasswordAsync(user);

		// The last administrator must not disappear, or nobody could manage the site any more.
		if (user.IsAdmin && !await userManager.Users.AnyAsync(u => u.IsAdmin && u.Id != user.Id && u.EmailConfirmed))
		{
			Message = L["error_last_admin_cannot_delete"];
			IsError = true;
			return Page();
		}

		if (HasPassword && !await userManager.CheckPasswordAsync(user, CurrentPassword))
		{
			Message = L["error_current_password_wrong"];
			IsError = true;
			return Page();
		}

		var failure = await accounts.DeleteAsync(user);
		if (failure != null)
		{
			Message = failure;
			IsError = true;
			return Page();
		}

		await signInManager.SignOutAsync();
		return Redirect("/");
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
