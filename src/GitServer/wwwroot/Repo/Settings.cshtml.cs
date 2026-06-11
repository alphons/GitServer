using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.wwwroot.Repo;

[Authorize]
public class SettingsModel(
	RepositoryService repos, 
	UserManager<AppUser> userManager, 
	LocalizationService L) : PageModel
{

	public string UserName { get; set; } = "";
	public string RepoName { get; set; } = "";
	public Repository? Repo { get; set; }
	public string? Message { get; set; }
	public bool IsError { get; set; }

	[BindProperty] public string? Description { get; set; }
	[BindProperty] public bool IsPrivate { get; set; }
	[BindProperty] public string DefaultBranch { get; set; } = "main";

	private async Task<(Repository? repo, bool isOwner)> LoadAsync(string user, string repo)
	{
		UserName = user;
		RepoName = repo;
		var repoObj = await repos.GetAsync(user, repo);
		if (repoObj == null) return (null, false);

		var userId = userManager.GetUserId(User);
		var isOwner = repoObj.OwnerId == userId;
		Repo = repoObj;
		return (repoObj, isOwner);
	}

	public async Task<IActionResult> OnGetAsync(string user, string repo)
	{
		var (repoObj, isOwner) = await LoadAsync(user, repo);
		if (repoObj == null) return NotFound();
		if (!isOwner) return Forbid();
		return Page();
	}

	public async Task<IActionResult> OnPostUpdateAsync(string user, string repo)
	{
		var (repoObj, isOwner) = await LoadAsync(user, repo);
		if (repoObj == null) return NotFound();
		if (!isOwner) return Forbid();

		repoObj.Description = Description;
		repoObj.IsPrivate = IsPrivate;
		repoObj.DefaultBranch = string.IsNullOrEmpty(DefaultBranch) ? "main" : DefaultBranch;
		repoObj.UpdatedAt = DateTime.UtcNow;

		var db = HttpContext.RequestServices.GetRequiredService<GitServer.Data.AppDbContext>();
		await db.SaveChangesAsync();

		Message = L["success_settings_saved"];
		return Page();
	}

	public async Task<IActionResult> OnPostDeleteAsync(string user, string repo)
	{
		var (repoObj, isOwner) = await LoadAsync(user, repo);
		if (repoObj == null) return NotFound();
		if (!isOwner) return Forbid();

		await repos.DeleteAsync(repoObj, user);
		return Redirect("/");
	}
}
