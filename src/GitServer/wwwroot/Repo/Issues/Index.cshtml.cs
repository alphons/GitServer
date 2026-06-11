using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GitServer.wwwroot.Repo.Issues;

public class IssueIndexModel(
	RepositoryService repos,
	AppDbContext db,
	UserManager<AppUser> userManager) : PageModel
{

	public string UserName { get; set; } = "";
	public string RepoName { get; set; } = "";
	public bool ShowClosed { get; set; }
	public List<Issue> Issues { get; set; } = new();

	public async Task<IActionResult> OnGetAsync(string user, string repo, int closed = 0)
	{
		UserName = user;
		RepoName = repo;
		ShowClosed = closed == 1;

		var repoObj = await repos.GetAsync(user, repo);
		if (repoObj == null) return NotFound();

		var userId = userManager.GetUserId(User);
		if (!await repos.CanReadAsync(repoObj, userId)) return Forbid();

		Issues = await db.Issues
			.Include(i => i.Author)
			.Include(i => i.Comments)
			.Where(i => i.RepositoryId == repoObj.Id && i.IsClosed == ShowClosed)
			.OrderByDescending(i => i.CreatedAt)
			.ToListAsync();

		return Page();
	}
}
