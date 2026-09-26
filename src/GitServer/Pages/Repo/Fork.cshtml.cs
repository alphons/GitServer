using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.Pages.Repo;

[Authorize]
public class ForkModel(
	RepositoryService repos,
	AccessPolicy access,
	ForkService forks,
	UserManager<AppUser> userManager) : PageModel
{
	public string UserName { get; set; } = "";
	public string RepoName { get; set; } = "";
	public Repository? Repo { get; set; }
	public AppUser? CurrentUser { get; set; }
	public List<Group> OwnGroups { get; set; } = new();
	public string? ErrorMessage { get; set; }

	[BindProperty] public string Name { get; set; } = "";
	[BindProperty] public int? GroupOwnerId { get; set; }

	private async Task<IActionResult?> LoadAsync(string user, string repo)
	{
		Repo = await repos.GetAsync(user, repo);
		if (Repo == null) return NotFound();
		UserName = Repo.OwnerName;
		RepoName = Repo.Name;

		CurrentUser = await userManager.GetUserAsync(User);
		if (CurrentUser == null) return Challenge();
		if (!await access.CanReadAsync(Repo, CurrentUser.Id)) return Forbid();

		OwnGroups = await access.GetOwnedGroupsAsync(CurrentUser.Id);
		return null;
	}

	public async Task<IActionResult> OnGetAsync(string user, string repo)
	{
		if (await LoadAsync(user, repo) is { } failure) return failure;
		Name = Repo!.Name;
		return Page();
	}

	public async Task<IActionResult> OnPostAsync(string user, string repo)
	{
		if (await LoadAsync(user, repo) is { } failure) return failure;

		var result = await forks.ForkAsync(Repo!, CurrentUser!, GroupOwnerId, Name);
		if (!result.Succeeded)
		{
			ErrorMessage = result.Message;
			return Page();
		}

		return RedirectToPage("/Repo/View", new { user = result.Fork!.OwnerName, repo = result.Fork.Name });
	}
}
