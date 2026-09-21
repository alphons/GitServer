using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.Pages.Admin;

// The entries are loaded from /api/admin/audit; this page only hosts them.
public class AuditLogModel(UserManager<AppUser> userManager) : PageModel
{
	public async Task<IActionResult> OnGetAsync()
	{
		if (!AccessPolicy.IsSiteAdmin(await userManager.GetUserAsync(User))) return Forbid();
		return Page();
	}
}
