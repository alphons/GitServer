using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.wwwroot.Auth;

public class LoginModel(
    SignInManager<AppUser> signInManager, 
    UserManager<AppUser> userManager, 
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
            return LocalRedirect(returnUrl ?? "/");

        ErrorMessage = L["error_invalid_credentials"];
        return Page();
    }
}
