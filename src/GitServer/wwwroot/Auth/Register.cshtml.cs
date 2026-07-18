using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace GitServer.wwwroot.Auth;

public class RegisterModel(
	UserManager<AppUser> userManager,
	IEmailService emailService,
	IOptions<GitServerOptions> options,
	LocalizationService L) : PageModel
{
	private readonly GitServerOptions _options = options.Value;

	[BindProperty] public string Email { get; set; } = "";
	public string? ErrorMessage { get; set; }
	public string? SuccessMessage { get; set; }

	public IActionResult OnGet()
	{
		if (!_options.AllowRegistration)
			return RedirectToPage("/Auth/Login");
		return Page();
	}

	public async Task<IActionResult> OnPostAsync()
	{
		if (!_options.AllowRegistration)
			return RedirectToPage("/Auth/Login");

		var email = Email.Trim();
		var user = await userManager.FindByEmailAsync(email);

		if (user != null && user.EmailConfirmed)
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
			L.Format("register_email_body", link));

		SuccessMessage = L["register_email_sent"];
		return Page();
	}
}
