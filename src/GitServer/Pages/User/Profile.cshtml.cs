using GitServer.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.Pages.User;

// The repository lists are loaded by the page from GET /api/users/{username}/repos.
public class ProfileModel(UserManager<AppUser> userManager) : PageModel
{
	public AppUser? ProfileUser { get; set; }
	public bool IsOwner { get; set; }
	public string Query { get; set; } = "";

	public async Task<IActionResult> OnGetAsync(string username, string? q)
	{
		ProfileUser = await userManager.FindByNameAsync(username);
		if (ProfileUser == null) return NotFound();

		IsOwner = userManager.GetUserId(User) == ProfileUser.Id;
		Query = q ?? "";
		return Page();
	}
}
