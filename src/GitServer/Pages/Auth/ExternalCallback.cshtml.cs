using GitServer.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace GitServer.Pages.Auth;

public class ExternalCallbackModel : PageModel
{
    private readonly SignInManager<AppUser> _signInManager;
    private readonly UserManager<AppUser> _userManager;

    public ExternalCallbackModel(SignInManager<AppUser> signInManager, UserManager<AppUser> userManager)
    {
        _signInManager = signInManager;
        _userManager = userManager;
    }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null, string? remoteError = null)
    {
        if (remoteError != null)
            return RedirectToPage("/Auth/Login");

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info == null)
            return RedirectToPage("/Auth/Login");

        // Try sign in with existing external login
        var result = await _signInManager.ExternalLoginSignInAsync(
            info.LoginProvider, info.ProviderKey, isPersistent: true, bypassTwoFactor: true);

        if (result.Succeeded)
            return LocalRedirect(returnUrl ?? "/");

        // Register new user
        var email = info.Principal.FindFirstValue(ClaimTypes.Email) ?? "";
        var name = info.Principal.FindFirstValue(ClaimTypes.Name) ?? "";
        var username = email.Split('@')[0].Replace(".", "-").Replace("+", "-");

        // Ensure unique username
        var baseUsername = username;
        var counter = 1;
        while (await _userManager.FindByNameAsync(username) != null)
            username = baseUsername + counter++;

        var user = new AppUser
        {
            UserName = username,
            Email = email,
            DisplayName = name,
            EmailConfirmed = true,
        };

        if (!_userManager.Users.Any())
            user.IsAdmin = true;

        var createResult = await _userManager.CreateAsync(user);
        if (!createResult.Succeeded)
            return RedirectToPage("/Auth/Login");

        await _userManager.AddLoginAsync(user, info);
        await _signInManager.SignInAsync(user, isPersistent: true);

        return LocalRedirect(returnUrl ?? "/");
    }
}
