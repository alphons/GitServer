using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.Pages.Repo;

public class CommitModel : PageModel
{
    private readonly RepositoryService _repos;
    private readonly GitProcessService _git;
    private readonly UserManager<AppUser> _userManager;

    public CommitModel(RepositoryService repos, GitProcessService git, UserManager<AppUser> userManager)
    {
        _repos = repos;
        _git = git;
        _userManager = userManager;
    }

    public string UserName { get; set; } = "";
    public string RepoName { get; set; } = "";
    public CommitDetail? Detail { get; set; }

    public async Task<IActionResult> OnGetAsync(string user, string repo, string sha)
    {
        UserName = user;
        RepoName = repo;

        var repoObj = await _repos.GetAsync(user, repo);
        if (repoObj == null) return NotFound();

        var userId = _userManager.GetUserId(User);
        if (!await _repos.CanReadAsync(repoObj, userId)) return Forbid();

        var repoPath = _repos.GetRepoPath(user, repo);
        Detail = await _git.GetCommitDetail(repoPath, sha);

        return Page();
    }
}
