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
	public bool IsOwner { get; set; }
	public string Query { get; set; } = "";
	public int CurrentPage { get; set; }
	public int PageSize { get; } = options.Value.ProfileRepoPageSize;
	public bool HasNextPage { get; set; }

	public async Task<IActionResult> OnGetAsync(string username, string? q, int p = 0)
	{
		ProfileUser = await userManager.FindByNameAsync(username);
		if (ProfileUser == null) return NotFound();

		var currentUserId = userManager.GetUserId(User);
		IsOwner = currentUserId == ProfileUser.Id;
		Query = q ?? "";
		CurrentPage = p;

		var fetched = await repos.GetUserReposAsync(ProfileUser.Id, includePrivate: IsOwner, query: Query,
			skip: p * PageSize, take: PageSize + 1);
		HasNextPage = fetched.Count > PageSize;
		Repos = fetched.Take(PageSize).ToList();
		return Page();
	}
}
