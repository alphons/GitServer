using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.Pages.Admin;

// The list and its edits go through /api/admin/reserved-names; this page only hosts them.
public class ReservedNamesModel(UserManager<AppUser> userManager) : PageModel
{
	public async Task<IActionResult> OnGetAsync()
	{
		if (!AccessPolicy.IsSiteAdmin(await userManager.GetUserAsync(User))) return Forbid();
		return Page();
	}
}
