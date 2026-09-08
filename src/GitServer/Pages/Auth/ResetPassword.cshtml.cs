using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.Pages.Auth;

public class ResetPasswordModel(
	UserManager<AppUser> userManager,
	LocalizationService L) : PageModel
{
	[BindProperty] public string Email { get; set; } = "";
	[BindProperty] public string Token { get; set; } = "";
	[BindProperty] public string Password { get; set; } = "";
	[BindProperty] public string ConfirmPassword { get; set; } = "";
	public string? ErrorMessage { get; set; }
	public bool SuccessDone { get; set; }

	public void OnGet(string email, string token)
	{
		Email = email;
		Token = token;
	}

	public async Task<IActionResult> OnPostAsync()
	{
		if (Password != ConfirmPassword)
		{
			ErrorMessage = L["error_passwords_do_not_match"];
			return Page();
		}

		var user = await userManager.FindByEmailAsync(Email);
		if (user == null)
		{
			ErrorMessage = L["error_invalid_or_expired_link"];
			return Page();
		}

		var result = await userManager.ResetPasswordAsync(user, Token, Password);
		if (!result.Succeeded)
		{
			ErrorMessage = string.Join(" ", result.Errors.Select(e => e.Description));
			return Page();
		}

		SuccessDone = true;
		return Page();
	}
}
