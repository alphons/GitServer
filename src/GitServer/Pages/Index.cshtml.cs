using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.Pages;

public class IndexModel : PageModel
{
    private readonly RepositoryService _repos;
    private readonly UserManager<AppUser> _userManager;

    public IndexModel(RepositoryService repos, UserManager<AppUser> userManager)
    {
        _repos = repos;
        _userManager = userManager;
    }

    public List<Repository> Repos { get; set; } = new();

    public async Task OnGetAsync()
    {
        var userId = _userManager.GetUserId(User);
        if (userId != null)
        {
            // Show own repos + public repos
            var myRepos = await _repos.GetUserReposAsync(userId, includePrivate: true);
            var publicRepos = await _repos.GetPublicReposAsync(0, 30);
            Repos = myRepos.Concat(publicRepos)
                .DistinctBy(r => r.Id)
                .OrderByDescending(r => r.UpdatedAt)
                .Take(20)
                .ToList();
        }
        else
        {
            Repos = await _repos.GetPublicReposAsync(0, 20);
        }
    }
}
