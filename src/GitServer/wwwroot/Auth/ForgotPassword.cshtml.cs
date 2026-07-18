using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.wwwroot.Auth;

public class ForgotPasswordModel(
	UserManager<AppUser> userManager,
	IEmailService emailService,
	LocalizationService L) : PageModel
{
	[BindProperty] public string Email { get; set; } = "";
	public string? SuccessMessage { get; set; }

	public void OnGet() { }

	public async Task<IActionResult> OnPostAsync()
	{
		var email = Email.Trim();
		var user = await userManager.FindByEmailAsync(email);

		if (user != null && user.EmailConfirmed)
		{
			var token = await userManager.GeneratePasswordResetTokenAsync(user);
			var link = Url.Page("/Auth/ResetPassword", pageHandler: null,
				values: new { email = user.Email, token }, protocol: Request.Scheme)!;

			await emailService.SendEmailAsync(email, L["reset_password_email_subject"],
				L.Format("reset_password_email_body", link));
		}

		// Always show the same message, whether or not the email is registered.
		SuccessMessage = L["reset_password_email_sent"];
		return Page();
	}
}
