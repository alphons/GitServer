using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.Pages.Repo;

public class DataMissingModel(
	RepositoryService repos, AccessPolicy access,
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
		UserName = Repo.OwnerName;
		RepoName = Repo.Name;

		var currentUser = await userManager.GetUserAsync(User);
		IsOwnerOrAdmin = await access.CanDeleteAsync(Repo, currentUser);

		return Page();
	}

	public async Task<IActionResult> OnPostDeleteAsync(string user, string repo)
	{
		var repoObj = await repos.GetAsync(user, repo);
		if (repoObj == null) return NotFound();

		var currentUser = await userManager.GetUserAsync(User);
		if (!await access.CanDeleteAsync(repoObj, currentUser))
			return Forbid();

		await repos.DeleteAsync(repoObj, repoObj.OwnerName);
		return Redirect("/");
	}
}
