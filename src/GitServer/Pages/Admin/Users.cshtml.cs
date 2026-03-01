using GitServer.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GitServer.Pages.Admin;

public class UsersModel : PageModel
{
    private readonly UserManager<AppUser> _userManager;

    public UsersModel(UserManager<AppUser> userManager) => _userManager = userManager;

    public List<AppUser> Users { get; set; } = new();
    public string? CurrentUserId { get; set; }
    public string? Message { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser == null || !currentUser.IsAdmin) return Forbid();

        CurrentUserId = currentUser.Id;
        Users = await _userManager.Users.OrderBy(u => u.UserName).ToListAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostToggleAdminAsync(string userId)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser == null || !currentUser.IsAdmin) return Forbid();

        var target = await _userManager.FindByIdAsync(userId);
        if (target == null) return NotFound();

        target.IsAdmin = !target.IsAdmin;
        await _userManager.UpdateAsync(target);

        Message = $"{target.UserName} is nu {(target.IsAdmin ? "admin" : "geen admin")}.";
        CurrentUserId = currentUser.Id;
        Users = await _userManager.Users.OrderBy(u => u.UserName).ToListAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string userId)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser == null || !currentUser.IsAdmin) return Forbid();
        if (userId == currentUser.Id) return BadRequest("Kan jezelf niet verwijderen.");

        var target = await _userManager.FindByIdAsync(userId);
        if (target == null) return NotFound();

        await _userManager.DeleteAsync(target);

        Message = $"Gebruiker {target.UserName} verwijderd.";
        CurrentUserId = currentUser.Id;
        Users = await _userManager.Users.OrderBy(u => u.UserName).ToListAsync();
        return Page();
    }
}
