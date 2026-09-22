using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.Pages.Admin;

// The status/test/migrate actions are all driven from /api/admin/database-migration; this page only hosts them.
public class DatabaseMigrationModel(UserManager<AppUser> userManager) : PageModel
{
	public async Task<IActionResult> OnGetAsync()
	{
		if (!AccessPolicy.IsSiteAdmin(await userManager.GetUserAsync(User))) return Forbid();
		return Page();
	}
}
