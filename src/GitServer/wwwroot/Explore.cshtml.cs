using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.wwwroot;

public class ExploreModel(RepositoryService repos) : PageModel
{
	public string Query { get; set; } = "";
	public new int Page { get; set; }
	public List<Repository> Repos { get; set; } = [];

	public async Task OnGetAsync(string? q, int page = 0)
	{
		Query = q ?? "";
		Page = page;

		if (!string.IsNullOrWhiteSpace(Query))
			Repos = await repos.SearchAsync(Query, page * 20, 20);
		else
			Repos = await repos.GetPublicReposAsync(page * 20, 20);
	}
}
