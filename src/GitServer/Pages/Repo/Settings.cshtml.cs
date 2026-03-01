using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.Pages.Repo;

[Authorize]
public class SettingsModel : PageModel
{
    private readonly RepositoryService _repos;
    private readonly UserManager<AppUser> _userManager;

    public SettingsModel(RepositoryService repos, UserManager<AppUser> userManager)
    {
        _repos = repos;
        _userManager = userManager;
    }

    public string UserName { get; set; } = "";
    public string RepoName { get; set; } = "";
    public Repository? Repo { get; set; }
    public string? Message { get; set; }
    public bool IsError { get; set; }

    [BindProperty] public string? Description { get; set; }
    [BindProperty] public bool IsPrivate { get; set; }
    [BindProperty] public string DefaultBranch { get; set; } = "main";

    private async Task<(Repository? repo, bool isOwner)> LoadAsync(string user, string repo)
    {
        UserName = user;
        RepoName = repo;
        var repoObj = await _repos.GetAsync(user, repo);
        if (repoObj == null) return (null, false);

        var userId = _userManager.GetUserId(User);
        var isOwner = repoObj.OwnerId == userId;
        Repo = repoObj;
        return (repoObj, isOwner);
    }

    public async Task<IActionResult> OnGetAsync(string user, string repo)
    {
        var (repoObj, isOwner) = await LoadAsync(user, repo);
        if (repoObj == null) return NotFound();
        if (!isOwner) return Forbid();
        return Page();
    }

    public async Task<IActionResult> OnPostUpdateAsync(string user, string repo)
    {
        var (repoObj, isOwner) = await LoadAsync(user, repo);
        if (repoObj == null) return NotFound();
        if (!isOwner) return Forbid();

        repoObj.Description = Description;
        repoObj.IsPrivate = IsPrivate;
        repoObj.DefaultBranch = string.IsNullOrEmpty(DefaultBranch) ? "main" : DefaultBranch;
        repoObj.UpdatedAt = DateTime.UtcNow;

        // EF tracks the entity, just save
        var db = HttpContext.RequestServices.GetRequiredService<GitServer.Data.AppDbContext>();
        await db.SaveChangesAsync();

        Message = "Instellingen opgeslagen.";
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string user, string repo)
    {
        var (repoObj, isOwner) = await LoadAsync(user, repo);
        if (repoObj == null) return NotFound();
        if (!isOwner) return Forbid();

        await _repos.DeleteAsync(repoObj, user);
        return RedirectToPage("/Index");
    }
}
