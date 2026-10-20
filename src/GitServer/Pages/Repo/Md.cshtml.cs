using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.Pages.Repo;

/// <summary>Renders a markdown file that lives directly under the repo root (no subfolder), with a flag
/// picker when other language variants of the same file (e.g. Test.nl.md next to Test.md) exist.</summary>
public class MdModel(
	RepositoryService repos, AccessPolicy access,
	GitProcessService git,
	UserManager<AppUser> userManager) : PageModel
{
	public string UserName { get; set; } = "";
	public string RepoName { get; set; } = "";
	public bool IsGroupOwner { get; set; }
	public string CurrentBranch { get; set; } = "";
	public string FileName { get; set; } = "";
	public List<MdVariant> Options { get; set; } = new();

	public async Task<IActionResult> OnGetAsync(string user, string repo, string file, string? branch)
	{
		UserName = user;
		RepoName = repo;

		var repoObj = await repos.GetAsync(user, repo);
		if (repoObj == null) return NotFound();
		IsGroupOwner = repoObj.GroupOwnerId != null;
		UserName = repoObj.OwnerName;
		RepoName = repoObj.Name;

		var userId = userManager.GetUserId(User);
		if (!await access.CanReadAsync(repoObj, userId)) return Forbid();

		var parsed = MarkdownVariants.Parse(file);
		if (parsed == null) return NotFound();

		var repoPath = repos.GetRepoPath(repoObj.OwnerName, repoObj.Name);
		if (await git.IsEmpty(repoPath)) return NotFound();

		CurrentBranch = branch ?? await git.GetDefaultBranch(repoPath);
		var tree = await git.GetTree(repoPath, CurrentBranch, "");

		Options = MarkdownVariants.FindVariants(tree, parsed.Value.Base);
		if (Options.Count == 0) return NotFound();

		var requested = Options.FirstOrDefault(o => o.FileName.Equals(file, StringComparison.OrdinalIgnoreCase));
		FileName = requested?.FileName ?? Options[0].FileName;

		return Page();
	}
}
