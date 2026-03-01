using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace GitServer.Pages.Auth;

public class RegisterModel : PageModel
{
    private readonly UserManager<AppUser> _userManager;
    private readonly SignInManager<AppUser> _signInManager;
    private readonly GitServerOptions _options;

    public RegisterModel(UserManager<AppUser> userManager, SignInManager<AppUser> signInManager, IOptions<GitServerOptions> options)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _options = options.Value;
    }

    [BindProperty] public string Username { get; set; } = "";
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string DisplayName { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    public string? ErrorMessage { get; set; }

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

        var user = new AppUser
        {
            UserName = Username,
            Email = Email,
            DisplayName = string.IsNullOrEmpty(DisplayName) ? Username : DisplayName,
        };

        // First user becomes admin
        if (!_userManager.Users.Any())
            user.IsAdmin = true;

        var result = await _userManager.CreateAsync(user, Password);
        if (!result.Succeeded)
        {
            ErrorMessage = string.Join(" ", result.Errors.Select(e => e.Description));
            return Page();
        }

        await _signInManager.SignInAsync(user, isPersistent: false);
        return RedirectToPage("/Index");
    }
}
