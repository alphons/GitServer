using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.Pages.Repo;

public class CommitsModel : PageModel
{
    private readonly RepositoryService _repos;
    private readonly GitProcessService _git;
    private readonly UserManager<AppUser> _userManager;

    public CommitsModel(RepositoryService repos, GitProcessService git, UserManager<AppUser> userManager)
    {
        _repos = repos;
        _git = git;
        _userManager = userManager;
    }

    public string UserName { get; set; } = "";
    public string RepoName { get; set; } = "";
    public string Branch { get; set; } = "main";
    public new int Page { get; set; }
    public int TotalCount { get; set; }
    public List<CommitInfo> Commits { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(string user, string repo, string? branch, int page = 0)
    {
        UserName = user;
        RepoName = repo;
        Page = page;

        var repoObj = await _repos.GetAsync(user, repo);
        if (repoObj == null) return NotFound();

        var userId = _userManager.GetUserId(User);
        if (!await _repos.CanReadAsync(repoObj, userId)) return Forbid();

        var repoPath = _repos.GetRepoPath(user, repo);
        if (await _git.IsEmpty(repoPath)) return Page();

        var defaultBranch = await _git.GetDefaultBranch(repoPath);
        Branch = branch ?? defaultBranch;

        TotalCount = await _git.GetCommitCount(repoPath, Branch);
        Commits = await _git.GetCommitLog(repoPath, Branch, page * 25, 25);

        return Page();
    }
}
