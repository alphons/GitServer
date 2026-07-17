using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GitServer.wwwroot.Admin;

public class UsersModel(UserManager<AppUser> userManager, LocalizationService L) : PageModel
{
	public List<AppUser> Users { get; set; } = new();
    public string? CurrentUserId { get; set; }
    public string? Message { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var currentUser = await userManager.GetUserAsync(User);
        if (currentUser == null || !currentUser.IsAdmin) return Forbid();

        CurrentUserId = currentUser.Id;
        Users = await userManager.Users.OrderBy(u => u.UserName).ToListAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostToggleAdminAsync(string userId)
    {
        var currentUser = await userManager.GetUserAsync(User);
        if (currentUser == null || !currentUser.IsAdmin) return Forbid();

        var target = await userManager.FindByIdAsync(userId);
        if (target == null) return NotFound();

        target.IsAdmin = !target.IsAdmin;
        await userManager.UpdateAsync(target);

        Message = L.Format(target.IsAdmin ? "admin_now_is_admin" : "admin_now_not_admin", target.UserName!);
        CurrentUserId = currentUser.Id;
        Users = await userManager.Users.OrderBy(u => u.UserName).ToListAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string userId)
    {
        var currentUser = await userManager.GetUserAsync(User);
        if (currentUser == null || !currentUser.IsAdmin) return Forbid();
        if (userId == currentUser.Id) return BadRequest(L["admin_cannot_delete_self"]);

        var target = await userManager.FindByIdAsync(userId);
        if (target == null) return NotFound();

        await userManager.DeleteAsync(target);

        Message = L.Format("admin_user_deleted", target.UserName!);
        CurrentUserId = currentUser.Id;
        Users = await userManager.Users.OrderBy(u => u.UserName).ToListAsync();
        return Page();
    }
}
