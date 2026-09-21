using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.Pages.User;

/// <summary>Personal access tokens: used instead of the password for git over HTTPS.</summary>
[Authorize]
public class AccessTokensModel(UserManager<AppUser> userManager, AccessTokenService tokens, LocalizationService L) : PageModel
{
	public AppUser? CurrentUser { get; set; }
	public string? Message { get; set; }
	public bool IsError { get; set; }

	/// <summary>A token that was just created, shown once.</summary>
	public string? NewTokenValue { get; set; }

	[BindProperty] public string TokenName { get; set; } = "";
	[BindProperty] public int? TokenValidDays { get; set; }

	public async Task OnGetAsync() => CurrentUser = await userManager.GetUserAsync(User);

	public async Task<IActionResult> OnPostCreateTokenAsync()
	{
		var user = await userManager.GetUserAsync(User);
		if (user == null) return NotFound();
		CurrentUser = user;

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
}
