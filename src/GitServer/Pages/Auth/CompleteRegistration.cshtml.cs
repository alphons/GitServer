using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GitServer.Pages.Auth;

public class CompleteRegistrationModel(
	UserManager<AppUser> userManager,
	SignInManager<AppUser> signInManager,
	LocalizationService L) : PageModel
{
	[BindProperty] public string Email { get; set; } = "";
	[BindProperty] public string Token { get; set; } = "";
	[BindProperty] public string Username { get; set; } = "";
	[BindProperty] public string DisplayName { get; set; } = "";
	[BindProperty] public string Password { get; set; } = "";
	[BindProperty] public string ConfirmPassword { get; set; } = "";
	public string? ErrorMessage { get; set; }

	public async Task<IActionResult> OnGetAsync(string email, string token)
	{
		Email = email;
		Token = token;

		var user = await userManager.FindByEmailAsync(email);
		if (user == null || user.EmailConfirmed)
		{
			ErrorMessage = L["error_invalid_or_expired_link"];
			return Page();
		}

		return Page();
	}

	public async Task<IActionResult> OnPostAsync()
	{
		var user = await userManager.FindByEmailAsync(Email);
		if (user == null || user.EmailConfirmed)
		{
			ErrorMessage = L["error_invalid_or_expired_link"];
			return Page();
		}

		if (Password != ConfirmPassword)
		{
			ErrorMessage = L["error_passwords_do_not_match"];
			return Page();
		}

		var username = Username.Trim();
		var existing = await userManager.FindByNameAsync(username);
		if (existing != null && existing.Id != user.Id)
		{
			ErrorMessage = L["error_username_taken"];
			return Page();
		}

		var usernameResult = await userManager.SetUserNameAsync(user, username);
		if (!usernameResult.Succeeded)
		{
			ErrorMessage = string.Join(" ", usernameResult.Errors.Select(e => e.Description));
			return Page();
		}

		user.DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? username : DisplayName;

		// First completed registration becomes admin
		if (!await userManager.Users.AnyAsync(u => u.EmailConfirmed))
			user.IsAdmin = true;

		await userManager.UpdateAsync(user);

		var passwordResult = await userManager.AddPasswordAsync(user, Password);
		if (!passwordResult.Succeeded)
		{
			ErrorMessage = string.Join(" ", passwordResult.Errors.Select(e => e.Description));
			return Page();
		}

		var confirmResult = await userManager.ConfirmEmailAsync(user, Token);
		if (!confirmResult.Succeeded)
		{
			ErrorMessage = L["error_invalid_or_expired_link"];
			return Page();
		}

		await signInManager.SignInAsync(user, isPersistent: false);
		return RedirectToPage("/Index");
	}
}
