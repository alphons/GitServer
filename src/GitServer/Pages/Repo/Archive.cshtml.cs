using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.Pages.Repo;

public class ArchiveModel : PageModel
{
    private readonly RepositoryService _repos;
    private readonly GitProcessService _git;
    private readonly UserManager<AppUser> _userManager;

    public ArchiveModel(RepositoryService repos, GitProcessService git, UserManager<AppUser> userManager)
    {
        _repos = repos;
        _git = git;
        _userManager = userManager;
    }

    public async Task<IActionResult> OnGetAsync(string user, string repo, string treeish)
    {
        var repoObj = await _repos.GetAsync(user, repo);
        if (repoObj == null) return NotFound();

        var userId = _userManager.GetUserId(User);
        if (!await _repos.CanReadAsync(repoObj, userId)) return Forbid();

        var repoPath = _repos.GetRepoPath(user, repo);

        Response.ContentType = "application/zip";
        Response.Headers.ContentDisposition = $"attachment; filename=\"{repo}-{treeish}.zip\"";

        await _git.StreamArchive(repoPath, treeish, Response.Body);
        return new EmptyResult();
    }
}
