using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.wwwroot.Repo;

public class BlobModel(
	RepositoryService repos, 
	GitProcessService git, 
	UserManager<AppUser> userManager) : PageModel
{
	public string UserName { get; set; } = "";
	public string RepoName { get; set; } = "";
	public string Branch { get; set; } = "";
	public string FilePath { get; set; } = "";
	public new string Content { get; set; } = "";
	public long FileSize { get; set; }
	public bool IsBinary { get; set; }

	public async Task<IActionResult> OnGetAsync(string user, string repo, string branch, string path)
	{
		UserName = user;
		RepoName = repo;
		Branch = branch;
		FilePath = path;

		var repoObj = await repos.GetAsync(user, repo);
		if (repoObj == null) return NotFound();

		var userId = userManager.GetUserId(User);
		if (!await repos.CanReadAsync(repoObj, userId)) return Forbid();

		var repoPath = repos.GetRepoPath(user, repo);
		FileSize = await git.GetFileSize(repoPath, branch, path);

		// Treat files >1MB or detected binary as binary
		if (FileSize > 1_048_576)
		{
			IsBinary = true;
			return Page();
		}

		Content = await git.GetFileContent(repoPath, branch, path);

		// Simple binary detection: check for null bytes
		if (Content.Contains('\0'))
		{
			IsBinary = true;
			Content = "";
		}

		return Page();
	}
}
