using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.wwwroot;

public class IndexModel(RepositoryService repos, UserManager<AppUser> userManager) : PageModel
{
	public List<Repository> Repos { get; set; } = [];

	public async Task OnGetAsync()
	{
		var userId = userManager.GetUserId(User);
		if (userId != null)
		{
			// Show own repos + public repos
			var myRepos = await repos.GetUserReposAsync(userId, includePrivate: true);
			var publicRepos = await repos.GetPublicReposAsync(0, 30);
			Repos = [.. myRepos.Concat(publicRepos)
				.DistinctBy(r => r.Id)
				.OrderByDescending(r => r.UpdatedAt)
				.Take(20)];
		}
		else
		{
			Repos = await repos.GetPublicReposAsync(0, 20);
		}
	}
}
