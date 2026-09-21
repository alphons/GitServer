using GitServer.Data;
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
	AppDbContext db,
	LocalizationService L,
	ReservedNames reserved) : PageModel
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

		// All or nothing: a taken username or a too-weak password must leave the mailed link usable for
		// a retry. Without the transaction the email confirmation below would already be saved, which
		// invalidates the link and leaves an account with a placeholder username and no password.
		await using var transaction = await db.Database.BeginTransactionAsync();

		async Task<IActionResult> FailAsync(string message)
		{
			await transaction.RollbackAsync();
			ErrorMessage = message;
			return Page();
		}

		// Confirm the email token first: AddPasswordAsync/SetUserNameAsync bump the user's
		// security stamp, which the default token provider ties the token to — confirming
		// afterwards would always fail with "invalid or expired link".
		var confirmResult = await userManager.ConfirmEmailAsync(user, Token);
		if (!confirmResult.Succeeded)
			return await FailAsync(L["error_invalid_or_expired_link"]);

		var username = Username.Trim();
		if (await reserved.IsReservedAsync(username))
			return await FailAsync(L["error_name_reserved"]);

		var existing = await userManager.FindByNameAsync(username);
		if (existing != null && existing.Id != user.Id)
			return await FailAsync(L["error_username_taken"]);

		var usernameResult = await userManager.SetUserNameAsync(user, username);
		if (!usernameResult.Succeeded)
			return await FailAsync(string.Join(" ", usernameResult.Errors.Select(e => e.Description)));

		user.DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? username : DisplayName;

		// First completed registration becomes admin. ConfirmEmailAsync above already persisted
		// EmailConfirmed=true for this user, so exclude them from the check or it always counts
		// itself as "an existing confirmed user".
		if (!await userManager.Users.AnyAsync(u => u.EmailConfirmed && u.Id != user.Id))
			user.IsAdmin = true;

		await userManager.UpdateAsync(user);

		var passwordResult = await userManager.AddPasswordAsync(user, Password);
		if (!passwordResult.Succeeded)
			return await FailAsync(string.Join(" ", passwordResult.Errors.Select(e => e.Description)));

		await transaction.CommitAsync();

		await signInManager.SignInAsync(user, isPersistent: false);
		return RedirectToPage("/Index");
	}
}
