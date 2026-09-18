using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace GitServer.Pages.User;

public class ProfileModel(UserManager<AppUser> userManager, RepositoryService repos, IOptions<GitServerOptions> options) : PageModel
{
	public AppUser? ProfileUser { get; set; }
	public List<Repository> Repos { get; set; } = new();
	public List<Repository> GroupRepos { get; set; } = new();
	public bool IsOwner { get; set; }
	public string Query { get; set; } = "";
	public int CurrentPage { get; set; }
	public int PageSize { get; } = options.Value.ProfileRepoPageSize;
	public bool HasNextPage { get; set; }

	private async Task<bool> LoadAsync(string username, string? q, int p)
	{
		ProfileUser = await userManager.FindByNameAsync(username);
		if (ProfileUser == null) return false;

		var currentUserId = userManager.GetUserId(User);
		IsOwner = currentUserId == ProfileUser.Id;
		Query = q ?? "";
		CurrentPage = p;

		var fetched = await repos.GetUserReposAsync(ProfileUser.Id, includePrivate: IsOwner, query: Query,
			skip: p * PageSize, take: PageSize + 1);
		HasNextPage = fetched.Count > PageSize;
		Repos = fetched.Take(PageSize).ToList();

		if (IsOwner)
			GroupRepos = await repos.GetAccessibleGroupReposAsync(ProfileUser.Id, Query);

		return true;
	}

	public async Task<IActionResult> OnGetAsync(string username, string? q, int p = 0)
	{
		if (!await LoadAsync(username, q, p)) return NotFound();
		return Page();
	}

	public async Task<IActionResult> OnGetSearchAsync(string username, string? q, int p = 0)
	{
		if (!await LoadAsync(username, q, p)) return NotFound();
		return Partial("_ProfileRepos", this);
	}
}
