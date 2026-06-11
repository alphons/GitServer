using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.wwwroot.Repo;

public class CommitsModel(
	RepositoryService repos, 
	GitProcessService git, 
	UserManager<AppUser> userManager) : PageModel
{

	public string UserName { get; set; } = "";
	public string RepoName { get; set; } = "";
	public string Branch { get; set; } = "main";
	public new int Page { get; set; }
	public int TotalCount { get; set; }
	public List<CommitInfo> Commits { get; set; } = new();

	public async Task<IActionResult> OnGetAsync(string user, string repo, string? branch, int page = 0)
	{
		UserName = user;
		RepoName = repo;
		Page = page;

		var repoObj = await repos.GetAsync(user, repo);
		if (repoObj == null) return NotFound();

		var userId = userManager.GetUserId(User);
		if (!await repos.CanReadAsync(repoObj, userId)) return Forbid();

		var repoPath = repos.GetRepoPath(user, repo);
		if (await git.IsEmpty(repoPath)) return Page();

		var defaultBranch = await git.GetDefaultBranch(repoPath);
		Branch = branch ?? defaultBranch;

		TotalCount = await git.GetCommitCount(repoPath, Branch);
		Commits = await git.GetCommitLog(repoPath, Branch, page * 25, 25);

		return Page();
	}
}
