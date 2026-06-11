using GitServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.Pages.User;

[Authorize]
public class UserSettingsModel : PageModel
{
    private readonly UserManager<AppUser> _userManager;

    public UserSettingsModel(UserManager<AppUser> userManager) => _userManager = userManager;

    public string DisplayName { get; set; } = "";
    public string? Bio { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Message { get; set; }
    public bool IsError { get; set; }
    public bool HasPassword { get; set; }

    [BindProperty] public string NewDisplayName { get; set; } = "";
    [BindProperty] public string? NewBio { get; set; }
    [BindProperty] public string? NewAvatarUrl { get; set; }
    [BindProperty] public string CurrentPassword { get; set; } = "";
    [BindProperty] public string NewPassword { get; set; } = "";

    public async Task OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return;
        DisplayName = user.DisplayName;
        Bio = user.Bio;
        AvatarUrl = user.AvatarUrl;
        HasPassword = await _userManager.HasPasswordAsync(user);
    }

    public async Task<IActionResult> OnPostProfileAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return NotFound();

        user.DisplayName = NewDisplayName;
        user.Bio = NewBio;
        user.AvatarUrl = NewAvatarUrl;

        var result = await _userManager.UpdateAsync(user);
        Message = result.Succeeded ? "Profiel opgeslagen." : string.Join(" ", result.Errors.Select(e => e.Description));
        IsError = !result.Succeeded;

        DisplayName = user.DisplayName; Bio = user.Bio; AvatarUrl = user.AvatarUrl;
        return Page();
    }

    public async Task<IActionResult> OnPostPasswordAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return NotFound();

        IdentityResult result;
        if (await _userManager.HasPasswordAsync(user))
            result = await _userManager.ChangePasswordAsync(user, CurrentPassword, NewPassword);
        else
            result = await _userManager.AddPasswordAsync(user, NewPassword);

        Message = result.Succeeded ? "Wachtwoord gewijzigd." : string.Join(" ", result.Errors.Select(e => e.Description));
        IsError = !result.Succeeded;

        DisplayName = user.DisplayName; Bio = user.Bio; AvatarUrl = user.AvatarUrl;
        HasPassword = await _userManager.HasPasswordAsync(user);
        return Page();
    }
}
