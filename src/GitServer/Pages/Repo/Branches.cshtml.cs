using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.Pages.Repo;

public class BranchesModel : PageModel
{
    private readonly RepositoryService _repos;
    private readonly GitProcessService _git;
    private readonly UserManager<AppUser> _userManager;

    public BranchesModel(RepositoryService repos, GitProcessService git, UserManager<AppUser> userManager)
    {
        _repos = repos;
        _git = git;
        _userManager = userManager;
    }

    public string UserName { get; set; } = "";
    public string RepoName { get; set; } = "";
    public string DefaultBranch { get; set; } = "main";
    public List<string> Branches { get; set; } = new();
    public List<string> Tags { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(string user, string repo)
    {
        UserName = user;
        RepoName = repo;

        var repoObj = await _repos.GetAsync(user, repo);
        if (repoObj == null) return NotFound();

        var userId = _userManager.GetUserId(User);
        if (!await _repos.CanReadAsync(repoObj, userId)) return Forbid();

        var repoPath = _repos.GetRepoPath(user, repo);
        if (await _git.IsEmpty(repoPath)) return Page();

        DefaultBranch = await _git.GetDefaultBranch(repoPath);
        Branches = await _git.GetBranches(repoPath);
        Tags = await _git.GetTags(repoPath);

        return Page();
    }
}
