using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.Pages.Repo;

public class CommitModel(
	RepositoryService repos, 
	GitProcessService git, 
	UserManager<AppUser> userManager) : PageModel
{
	public string UserName { get; set; } = "";
	public string RepoName { get; set; } = "";
	public CommitDetail? Detail { get; set; }
	public List<string> Tags { get; set; } = new();

	public async Task<IActionResult> OnGetAsync(string user, string repo, string sha)
	{
		UserName = user;
		RepoName = repo;

		var repoObj = await repos.GetAsync(user, repo);
		if (repoObj == null) return NotFound();

		var userId = userManager.GetUserId(User);
		if (!await repos.CanReadAsync(repoObj, userId)) return Forbid();

		var repoPath = repos.GetRepoPath(user, repo);
		Detail = await git.GetCommitDetail(repoPath, sha);

		var tagsByCommit = await git.GetTagsByCommit(repoPath);
		Tags = tagsByCommit.GetValueOrDefault(Detail.Info.Sha) ?? [];

		return Page();
	}
}
