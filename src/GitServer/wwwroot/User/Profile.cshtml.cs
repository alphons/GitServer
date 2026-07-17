using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.wwwroot.User;

public class ProfileModel(UserManager<AppUser> userManager, RepositoryService repos) : PageModel
{
	public AppUser? ProfileUser { get; set; }
	public List<Repository> Repos { get; set; } = new();

	public async Task<IActionResult> OnGetAsync(string username)
	{
		ProfileUser = await userManager.FindByNameAsync(username);
		if (ProfileUser == null) return NotFound();

		var currentUserId = userManager.GetUserId(User);
		var isOwner = currentUserId == ProfileUser.Id;

		Repos = await repos.GetUserReposAsync(ProfileUser.Id, includePrivate: isOwner);
		return Page();
	}
}
