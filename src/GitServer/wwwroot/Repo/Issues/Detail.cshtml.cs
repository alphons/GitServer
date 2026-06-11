using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GitServer.wwwroot.Repo.Issues;

public class DetailModel(
	RepositoryService repos,
	AppDbContext db,
	UserManager<AppUser> userManager) : PageModel
{


	public string UserName { get; set; } = "";
	public string RepoName { get; set; } = "";
	public Issue? Issue { get; set; }
	public bool CanManage { get; set; }
	[BindProperty] public string CommentBody { get; set; } = "";
	public string? ErrorMessage { get; set; }

	private async Task<(Repository? repo, Issue? issue)> LoadAsync(string user, string repo, int id)
	{
		UserName = user; RepoName = repo;
		var repoObj = await repos.GetAsync(user, repo);
		if (repoObj == null) return (null, null);

		Issue = await db.Issues
			.Include(i => i.Author)
			.Include(i => i.Comments).ThenInclude(c => c.Author)
			.FirstOrDefaultAsync(i => i.RepositoryId == repoObj.Id && i.Id == id);

		var userId = userManager.GetUserId(User);
		CanManage = userId != null && (repoObj.OwnerId == userId || Issue?.AuthorId == userId ||
			await db.RepositoryAccesses.AnyAsync(a => a.RepositoryId == repoObj.Id && a.UserId == userId && a.Level == AccessLevel.Write));

		return (repoObj, Issue);
	}

	public async Task<IActionResult> OnGetAsync(string user, string repo, int id)
	{
		var (repoObj, _) = await LoadAsync(user, repo, id);
		if (repoObj == null) return NotFound();
		var userId = userManager.GetUserId(User);
		if (!await repos.CanReadAsync(repoObj, userId)) return Forbid();
		if (Issue == null) return NotFound();
		return Page();
	}

	public async Task<IActionResult> OnPostCommentAsync(string user, string repo, int id)
	{
		var (repoObj, issue) = await LoadAsync(user, repo, id);
		if (repoObj == null || issue == null) return NotFound();
		if (!User.Identity!.IsAuthenticated) return Challenge();

		if (!string.IsNullOrWhiteSpace(CommentBody))
		{
			db.IssueComments.Add(new IssueComment
			{
				IssueId = id,
				AuthorId = userManager.GetUserId(User)!,
				Body = CommentBody,
			});
			issue.UpdatedAt = DateTime.UtcNow;
			await db.SaveChangesAsync();
		}

		return RedirectToPage(new { user, repo, id });
	}

	public async Task<IActionResult> OnPostCloseAsync(string user, string repo, int id)
	{
		var (repoObj, issue) = await LoadAsync(user, repo, id);
		if (repoObj == null || issue == null) return NotFound();
		if (!CanManage) return Forbid();

		issue.IsClosed = true;
		issue.UpdatedAt = DateTime.UtcNow;
		await db.SaveChangesAsync();

		return RedirectToPage(new { user, repo, id });
	}

	public async Task<IActionResult> OnPostReopenAsync(string user, string repo, int id)
	{
		var (repoObj, issue) = await LoadAsync(user, repo, id);
		if (repoObj == null || issue == null) return NotFound();
		if (!CanManage) return Forbid();

		issue.IsClosed = false;
		issue.UpdatedAt = DateTime.UtcNow;
		await db.SaveChangesAsync();

		return RedirectToPage(new { user, repo, id });
	}
}
