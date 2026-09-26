using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.Pages.Repo.Pulls;

public class PullIndexModel(
	RepositoryService repos, AccessPolicy access, PullRequestService pulls, UserManager<AppUser> userManager) : PageModel
{
	public string UserName { get; set; } = "";
	public string RepoName { get; set; } = "";
	public bool IsGroupOwner { get; set; }
	public bool ShowClosed { get; set; }
	public List<PullRequest> PullRequests { get; set; } = new();

	public async Task<IActionResult> OnGetAsync(string user, string repo, int closed = 0)
	{
		var repoObj = await repos.GetAsync(user, repo);
		if (repoObj == null) return NotFound();
		if (!await access.CanReadAsync(repoObj, userManager.GetUserId(User))) return Forbid();
		UserName = repoObj.OwnerName;
		RepoName = repoObj.Name;
		IsGroupOwner = repoObj.GroupOwnerId != null;
		ShowClosed = closed == 1;

		PullRequests = await pulls.ListAsync(repoObj, open: !ShowClosed);
		return Page();
	}
}
