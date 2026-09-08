using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.Pages.Repo;

public class DataMissingModel(
	RepositoryService repos,
	UserManager<AppUser> userManager) : PageModel
{
	public string UserName { get; set; } = "";
	public string RepoName { get; set; } = "";
	public Repository? Repo { get; set; }
	public bool IsOwnerOrAdmin { get; set; }

	public async Task<IActionResult> OnGetAsync(string user, string repo)
	{
		UserName = user;
		RepoName = repo;

		Repo = await repos.GetAsync(user, repo);
		if (Repo == null) return NotFound();

		var currentUser = await userManager.GetUserAsync(User);
		IsOwnerOrAdmin = currentUser != null && (currentUser.Id == Repo.OwnerId || currentUser.IsAdmin);

		return Page();
	}

	public async Task<IActionResult> OnPostDeleteAsync(string user, string repo)
	{
		var repoObj = await repos.GetAsync(user, repo);
		if (repoObj == null) return NotFound();

		var currentUser = await userManager.GetUserAsync(User);
		if (currentUser == null || (currentUser.Id != repoObj.OwnerId && !currentUser.IsAdmin))
			return Forbid();

		await repos.DeleteAsync(repoObj, user);
		return Redirect("/");
	}
}
