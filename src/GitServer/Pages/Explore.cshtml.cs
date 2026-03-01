using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.Pages;

public class ExploreModel : PageModel
{
    private readonly RepositoryService _repos;

    public ExploreModel(RepositoryService repos) => _repos = repos;

    public string Query { get; set; } = "";
    public new int Page { get; set; }
    public List<Repository> Repos { get; set; } = new();

    public async Task OnGetAsync(string? q, int page = 0)
    {
        Query = q ?? "";
        Page = page;

        if (!string.IsNullOrWhiteSpace(Query))
            Repos = await _repos.SearchAsync(Query, page * 20, 20);
        else
            Repos = await _repos.GetPublicReposAsync(page * 20, 20);
    }
}
