using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.wwwroot.Repo;

public class ArchiveModel(
	RepositoryService repos, 
	GitProcessService git, 
	UserManager<AppUser> userManager) : PageModel
{

	public async Task<IActionResult> OnGetAsync(string user, string repo, string treeish)
	{
		var repoObj = await repos.GetAsync(user, repo);
		if (repoObj == null) return NotFound();

		var userId = userManager.GetUserId(User);
		if (!await repos.CanReadAsync(repoObj, userId)) return Forbid();

		var repoPath = repos.GetRepoPath(user, repo);

		Response.ContentType = "application/zip";
		Response.Headers.ContentDisposition = $"attachment; filename=\"{repo}-{treeish}.zip\"";

		await git.StreamArchive(repoPath, treeish, Response.Body);
		return new EmptyResult();
	}
}
