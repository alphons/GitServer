using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace GitServer.Pages;

public class ExploreModel(RepositoryService repos, UserManager<AppUser> userManager, IOptions<GitServerOptions> options) : PageModel
{
	public string Query { get; set; } = "";
	public new int Page { get; set; }
	public int PageSize { get; } = options.Value.ExploreRepoPageSize;
	public List<Repository> Repos { get; set; } = [];
	public bool HasNextPage { get; set; }

	public async Task OnGetAsync(string? q, int p = 0)
	{
		Query = q ?? "";
		Page = p;

		var userId = userManager.GetUserId(User);
		var fetched = !string.IsNullOrWhiteSpace(Query)
			? await repos.SearchAsync(Query, p * PageSize, PageSize + 1, userId)
			: await repos.GetPublicReposAsync(p * PageSize, PageSize + 1);

		HasNextPage = fetched.Count > PageSize;
		Repos = fetched.Take(PageSize).ToList();
	}
}
