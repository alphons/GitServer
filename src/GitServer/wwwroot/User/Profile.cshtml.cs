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
	public bool IsOwner { get; set; }
	public string Query { get; set; } = "";

	public async Task<IActionResult> OnGetAsync(string username, string? q)
	{
		ProfileUser = await userManager.FindByNameAsync(username);
		if (ProfileUser == null) return NotFound();

		var currentUserId = userManager.GetUserId(User);
		IsOwner = currentUserId == ProfileUser.Id;
		Query = q ?? "";

		Repos = await repos.GetUserReposAsync(ProfileUser.Id, includePrivate: IsOwner, query: Query);
		return Page();
	}
}
