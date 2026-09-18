using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GitServer.Pages.Auth;

public class RegisterModel(
	UserManager<AppUser> userManager,
	IEmailService emailService,
	AppDbContext db,
	SiteSettingsService siteSettings,
	LocalizationService L) : PageModel
{
	[BindProperty] public string Email { get; set; } = "";
	public string? ErrorMessage { get; set; }
	public string? SuccessMessage { get; set; }
	public bool RegistrationDisabled { get; set; }

	public async Task<IActionResult> OnGetAsync()
	{
		RegistrationDisabled = !(await siteSettings.GetAsync()).AllowRegistration;
		return Page();
	}

	public async Task<IActionResult> OnPostAsync()
	{
		if (!(await siteSettings.GetAsync()).AllowRegistration)
		{
			RegistrationDisabled = true;
			return Page();
		}

		var email = Email.Trim();

		var blockedPatterns = await db.BlockedEmailPatterns.Select(p => p.Pattern).ToListAsync();
		if (EmailBlocklist.IsBlocked(email, blockedPatterns))
		{
			ErrorMessage = L["error_email_blocked"];
			return Page();
		}

		var user = await userManager.FindByEmailAsync(email);

		// An account is "already registered" once it has a real password — regardless of
		// EmailConfirmed, which older accounts predating the email-first flow never had set.
		if (user != null && (user.EmailConfirmed || await userManager.HasPasswordAsync(user)))
		{
			ErrorMessage = L["error_email_already_registered"];
			return Page();
		}

		if (user == null)
		{
			user = new AppUser
			{
				UserName = "pending-" + Guid.NewGuid().ToString("N"),
				Email = email,
				DisplayName = "",
			};

			var createResult = await userManager.CreateAsync(user);
			if (!createResult.Succeeded)
			{
				ErrorMessage = string.Join(" ", createResult.Errors.Select(e => e.Description));
				return Page();
			}
		}

		var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
		var link = Url.Page("/Auth/CompleteRegistration", pageHandler: null,
			values: new { email = user.Email, token }, protocol: Request.Scheme)!;

		await emailService.SendEmailAsync(email, L["register_email_subject"],
			L.RenderEmail("register", ("link", link),
				("button_label", L["complete_registration_submit"]),
				("fallback_label", L["email_button_fallback"])));

		SuccessMessage = L["register_email_sent"];
		return Page();
	}
}
