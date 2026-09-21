using GitServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.Pages.User;

// The keys themselves are listed and changed through /api/user/api-keys; this page only hosts them.
[Authorize]
public class ApiKeysModel(UserManager<AppUser> userManager) : PageModel
{
	public AppUser? CurrentUser { get; set; }

	public async Task OnGetAsync() => CurrentUser = await userManager.GetUserAsync(User);
}
