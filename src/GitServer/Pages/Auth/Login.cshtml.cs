using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.Pages.Auth;

public class LoginModel(
    SignInManager<AppUser> signInManager,
    UserManager<AppUser> userManager,
    AppDbContext db,
    LocalizationService L) : PageModel
{
	[BindProperty] public string Username { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public bool RememberMe { get; set; }
    public string? ErrorMessage { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        var user = await userManager.FindByNameAsync(Username)
                   ?? await userManager.FindByEmailAsync(Username);

        if (user == null)
        {
            ErrorMessage = L["error_invalid_credentials"];
            return Page();
        }

        var result = await signInManager.PasswordSignInAsync(user, Password, RememberMe, lockoutOnFailure: false);

        if (result.Succeeded)
        {
            user.LastLoginAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            if (!string.IsNullOrEmpty(user.PreferredLanguage))
            {
                Response.Cookies.Append("lang", user.PreferredLanguage, new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax,
                    HttpOnly = true
                });
            }

            if (!string.IsNullOrEmpty(user.TimeZoneId))
            {
                Response.Cookies.Append(TimeZoneService.CookieName, user.TimeZoneId, new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax,
                    HttpOnly = true
                });
            }

            return LocalRedirect(returnUrl ?? "/");
        }

        ErrorMessage = L["error_invalid_credentials"];
        return Page();
    }
}
