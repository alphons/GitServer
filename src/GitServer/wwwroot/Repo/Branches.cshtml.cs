using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.wwwroot.Repo;

public class BranchesModel(
	RepositoryService repos,
	GitProcessService git,
	UserManager<AppUser> userManager) : PageModel
{

	public string UserName { get; set; } = "";
	public string RepoName { get; set; } = "";
	public string DefaultBranch { get; set; } = "main";
	public List<string> Branches { get; set; } = new();
	public List<string> Tags { get; set; } = new();

	public async Task<IActionResult> OnGetAsync(string user, string repo)
	{
		UserName = user;
		RepoName = repo;

		var repoObj = await repos.GetAsync(user, repo);
		if (repoObj == null) return NotFound();

		var userId = userManager.GetUserId(User);
		if (!await repos.CanReadAsync(repoObj, userId)) return Forbid();

		var repoPath = repos.GetRepoPath(user, repo);
		if (await git.IsEmpty(repoPath)) return Page();

		DefaultBranch = await git.GetDefaultBranch(repoPath);
		Branches = await git.GetBranches(repoPath);
		Tags = await git.GetTags(repoPath);

		return Page();
	}
}
