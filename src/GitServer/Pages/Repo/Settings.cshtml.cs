using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.Pages.Repo;

[Authorize]
public class SettingsModel(
	RepositoryService repos, AccessPolicy access,
	UserManager<AppUser> userManager,
	AppDbContext db,
	GitProcessService git,
	LocalizationService L) : PageModel
{

	public string UserName { get; set; } = "";
	public string RepoName { get; set; } = "";
	public Repository? Repo { get; set; }
	public string? Message { get; set; }
	public bool IsError { get; set; }

	[BindProperty] public string? Description { get; set; }
	[BindProperty] public bool IsPrivate { get; set; }
	[BindProperty] public bool IsReadOnly { get; set; }
	[BindProperty] public string DefaultBranch { get; set; } = "main";

	private async Task<(Repository? repo, bool isOwner)> LoadAsync(string user, string repo)
	{
		UserName = user;
		RepoName = repo;
		var repoObj = await repos.GetAsync(user, repo);
		if (repoObj == null) return (null, false);
		UserName = repoObj.OwnerName;
		RepoName = repoObj.Name;

		var userId = userManager.GetUserId(User);
		var isOwner = await access.CanAdministerAsync(repoObj, userId);
		Repo = repoObj;
		return (repoObj, isOwner);
	}

	public async Task<IActionResult> OnGetAsync(string user, string repo)
	{
		var (repoObj, isOwner) = await LoadAsync(user, repo);
		if (repoObj == null) return NotFound();
		if (!isOwner) return Forbid();

		Description = repoObj.Description;
		IsPrivate = repoObj.IsPrivate;
		IsReadOnly = repoObj.IsReadOnly;
		DefaultBranch = repoObj.DefaultBranch;

		return Page();
	}

	public async Task<IActionResult> OnPostUpdateAsync(string user, string repo)
	{
		var (repoObj, isOwner) = await LoadAsync(user, repo);
		if (repoObj == null) return NotFound();
		if (!isOwner) return Forbid();

		// Making the fork public would publish the private source's code.
		if (!IsPrivate && repoObj.ForkedFrom is { IsPrivate: true })
		{
			IsPrivate = true;
			Message = L["settings_fork_must_stay_private"];
			IsError = true;
			return Page();
		}

		var branch = string.IsNullOrWhiteSpace(DefaultBranch) ? "main" : DefaultBranch.Trim();
		if (!GitProcessService.IsValidBranchName(branch))
		{
			Message = L["settings_error_branch_name"];
			IsError = true;
			return Page();
		}

		repoObj.Description = Description;
		repoObj.IsPrivate = IsPrivate;
		repoObj.IsReadOnly = IsReadOnly;
		repoObj.DefaultBranch = branch;
		repoObj.UpdatedAt = DateTime.UtcNow;

		await db.SaveChangesAsync();
		// The default branch is what a clone checks out, so git's HEAD follows it (also before the branch is first pushed).
		await git.SetHead(repos.GetRepoPath(repoObj.OwnerName, repoObj.Name), branch);

		Message = L["success_settings_saved"];
		return Page();
	}

	public async Task<IActionResult> OnPostDeleteAsync(string user, string repo)
	{
		var (repoObj, isOwner) = await LoadAsync(user, repo);
		if (repoObj == null) return NotFound();
		if (!isOwner) return Forbid();

		await repos.DeleteAsync(repoObj, repoObj.OwnerName);
		return Redirect("/");
	}
}
