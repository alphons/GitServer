using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace GitServer.Pages;

public class IndexModel(RepositoryService repos, UserManager<AppUser> userManager, IOptions<GitServerOptions> options) : PageModel
{
	private readonly int _recentCount = options.Value.IndexRecentReposCount;

	public List<Repository> Repos { get; set; } = [];
	public string? CurrentUserName { get; set; }

	public async Task OnGetAsync()
	{
		var currentUser = await userManager.GetUserAsync(User);
		if (currentUser != null)
		{
			// Show own repos + public repos
			CurrentUserName = currentUser.UserName;
			var myRepos = await repos.GetUserReposAsync(currentUser.Id, includePrivate: true);
			var publicRepos = await repos.GetPublicReposAsync(0, _recentCount + 10);
			Repos = [.. myRepos.Concat(publicRepos)
				.DistinctBy(r => r.Id)
				.OrderByDescending(r => r.UpdatedAt)
				.Take(_recentCount)];
		}
		else
		{
			Repos = await repos.GetPublicReposAsync(0, _recentCount);
		}
	}
}
