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
	public int TotalCount { get; set; }
	public List<Repository> GroupRepos { get; set; } = new();
	public int GroupTotalCount { get; set; }
	public bool GroupHasNextPage { get; set; }
	public int GroupCurrentPage { get; set; }
	public bool IsOwner { get; set; }
	public string Query { get; set; } = "";
	public int CurrentPage { get; set; }
	public int PageSize { get; } = options.Value.ProfileRepoPageSize;
	public bool HasNextPage { get; set; }

	private async Task<bool> LoadAsync(string username, string? q, int p, int gp)
	{
		ProfileUser = await userManager.FindByNameAsync(username);
		if (ProfileUser == null) return false;

		var currentUserId = userManager.GetUserId(User);
		IsOwner = currentUserId == ProfileUser.Id;
		Query = q ?? "";
		CurrentPage = p;
		GroupCurrentPage = gp;

		var fetched = await repos.GetUserReposAsync(ProfileUser.Id, includePrivate: IsOwner, query: Query,
			skip: p * PageSize, take: PageSize + 1);
		HasNextPage = fetched.Count > PageSize;
		Repos = fetched.Take(PageSize).ToList();
		TotalCount = await repos.GetUserRepoCountAsync(ProfileUser.Id, includePrivate: IsOwner, query: Query);

		if (IsOwner)
		{
			var fetchedGroup = await repos.GetAccessibleGroupReposAsync(ProfileUser.Id, Query,
				skip: gp * PageSize, take: PageSize + 1);
			GroupHasNextPage = fetchedGroup.Count > PageSize;
			GroupRepos = fetchedGroup.Take(PageSize).ToList();
			GroupTotalCount = await repos.GetAccessibleGroupRepoCountAsync(ProfileUser.Id, Query);
		}

		return true;
	}

	public async Task<IActionResult> OnGetAsync(string username, string? q, int p = 0, int gp = 0)
	{
		if (!await LoadAsync(username, q, p, gp)) return NotFound();
		return Page();
	}

	public async Task<IActionResult> OnGetSearchAsync(string username, string? q, int p = 0, int gp = 0)
	{
		if (!await LoadAsync(username, q, p, gp)) return NotFound();
		return Partial("_ProfileRepos", this);
	}
}
