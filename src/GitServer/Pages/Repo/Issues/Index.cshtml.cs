using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GitServer.Pages.Repo.Issues;

public class IssueIndexModel : PageModel
{
    private readonly RepositoryService _repos;
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _userManager;

    public IssueIndexModel(RepositoryService repos, AppDbContext db, UserManager<AppUser> userManager)
    {
        _repos = repos;
        _db = db;
        _userManager = userManager;
    }

    public string UserName { get; set; } = "";
    public string RepoName { get; set; } = "";
    public bool ShowClosed { get; set; }
    public List<Issue> Issues { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(string user, string repo, int closed = 0)
    {
        UserName = user;
        RepoName = repo;
        ShowClosed = closed == 1;

        var repoObj = await _repos.GetAsync(user, repo);
        if (repoObj == null) return NotFound();

        var userId = _userManager.GetUserId(User);
        if (!await _repos.CanReadAsync(repoObj, userId)) return Forbid();

        Issues = await _db.Issues
            .Include(i => i.Author)
            .Include(i => i.Comments)
            .Where(i => i.RepositoryId == repoObj.Id && i.IsClosed == ShowClosed)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();

        return Page();
    }
}
