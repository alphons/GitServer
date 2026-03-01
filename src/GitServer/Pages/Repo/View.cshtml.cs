using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace GitServer.Pages.Repo;

public class ViewModel : PageModel
{
    private readonly RepositoryService _repos;
    private readonly GitProcessService _git;
    private readonly MarkdownService _markdown;
    private readonly UserManager<AppUser> _userManager;
    private readonly GitServerOptions _options;

    public ViewModel(RepositoryService repos, GitProcessService git, MarkdownService markdown,
        UserManager<AppUser> userManager, IOptions<GitServerOptions> options)
    {
        _repos = repos;
        _git = git;
        _markdown = markdown;
        _userManager = userManager;
        _options = options.Value;
    }

    public string UserName { get; set; } = "";
    public string RepoName { get; set; } = "";
    public Repository? Repo { get; set; }
    public bool IsEmpty { get; set; }
    public string CurrentBranch { get; set; } = "main";
    public string CurrentPath { get; set; } = "";
    public List<TreeEntry> Tree { get; set; } = new();
    public List<string> Branches { get; set; } = new();
    public string CloneUrl { get; set; } = "";
    public string DefaultBranch { get; set; } = "main";
    public string ReadmeHtml { get; set; } = "";

    public async Task<IActionResult> OnGetAsync(string user, string repo, string? branch, string? path)
    {
        UserName = user;
        RepoName = repo;

        Repo = await _repos.GetAsync(user, repo);
        if (Repo == null) return NotFound();

        var userId = _userManager.GetUserId(User);
        if (!await _repos.CanReadAsync(Repo, userId)) return Forbid();

        var repoPath = _repos.GetRepoPath(user, repo);
        CloneUrl = $"{Request.Scheme}://{Request.Host}/git/{user}/{repo}.git";

        IsEmpty = await _git.IsEmpty(repoPath);
        if (IsEmpty) { DefaultBranch = Repo.DefaultBranch; return Page(); }

        DefaultBranch = await _git.GetDefaultBranch(repoPath);
        Branches = await _git.GetBranches(repoPath);
        CurrentBranch = branch ?? DefaultBranch;
        CurrentPath = path ?? "";

        Tree = await _git.GetTree(repoPath, CurrentBranch, CurrentPath);

        if (string.IsNullOrEmpty(CurrentPath))
        {
            var readmeContent = await _git.GetReadme(repoPath, CurrentBranch);
            if (!string.IsNullOrEmpty(readmeContent))
                ReadmeHtml = _markdown.Render(readmeContent);
        }

        return Page();
    }
}
