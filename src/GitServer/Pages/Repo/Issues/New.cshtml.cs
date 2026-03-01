using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.Pages.Repo.Issues;

[Authorize]
public class NewIssueModel : PageModel
{
    private readonly RepositoryService _repos;
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _userManager;

    public NewIssueModel(RepositoryService repos, AppDbContext db, UserManager<AppUser> userManager)
    {
        _repos = repos;
        _db = db;
        _userManager = userManager;
    }

    public string UserName { get; set; } = "";
    public string RepoName { get; set; } = "";
    [BindProperty] public string Title { get; set; } = "";
    [BindProperty] public string Body { get; set; } = "";
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(string user, string repo)
    {
        UserName = user; RepoName = repo;
        var repoObj = await _repos.GetAsync(user, repo);
        if (repoObj == null) return NotFound();
        var userId = _userManager.GetUserId(User);
        if (!await _repos.CanReadAsync(repoObj, userId)) return Forbid();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string user, string repo)
    {
        UserName = user; RepoName = repo;
        var repoObj = await _repos.GetAsync(user, repo);
        if (repoObj == null) return NotFound();
        var userId = _userManager.GetUserId(User)!;

        var issue = new Issue
        {
            RepositoryId = repoObj.Id,
            AuthorId = userId,
            Title = Title,
            Body = Body,
        };
        _db.Issues.Add(issue);
        await _db.SaveChangesAsync();

        return RedirectToPage("/Repo/Issues/Detail", new { user, repo, id = issue.Id });
    }
}
