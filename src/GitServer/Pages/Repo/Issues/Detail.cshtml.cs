using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GitServer.Pages.Repo.Issues;

public class DetailModel(
	RepositoryService repos, AccessPolicy access,
	AppDbContext db,
	UserManager<AppUser> userManager) : PageModel
{


	public string UserName { get; set; } = "";
	public string RepoName { get; set; } = "";
	public bool IsGroupOwner { get; set; }
	public Issue? Issue { get; set; }
	public bool CanManage { get; set; }
	[BindProperty] public string CommentBody { get; set; } = "";
	public string? ErrorMessage { get; set; }

	private async Task<(Repository? repo, Issue? issue)> LoadAsync(string user, string repo, int id)
	{
		UserName = user; RepoName = repo;
		var repoObj = await repos.GetAsync(user, repo);
		if (repoObj == null) return (null, null);
		IsGroupOwner = repoObj.GroupOwnerId != null;
		UserName = repoObj.OwnerName;
		RepoName = repoObj.Name;

		Issue = await db.Issues
			.Include(i => i.Author)
			.Include(i => i.Comments).ThenInclude(c => c.Author)
			.FirstOrDefaultAsync(i => i.RepositoryId == repoObj.Id && i.Id == id);

		var userId = userManager.GetUserId(User);
		CanManage = await access.CanManageIssueAsync(repoObj, Issue, userId);

		return (repoObj, Issue);
	}

	public async Task<IActionResult> OnGetAsync(string user, string repo, int id)
	{
		var (repoObj, _) = await LoadAsync(user, repo, id);
		if (repoObj == null) return NotFound();
		var userId = userManager.GetUserId(User);
		if (!await access.CanReadAsync(repoObj, userId)) return Forbid();
		if (Issue == null) return NotFound();
		return Page();
	}

	public async Task<IActionResult> OnPostCommentAsync(string user, string repo, int id)
	{
		var (repoObj, issue) = await LoadAsync(user, repo, id);
		if (repoObj == null || issue == null) return NotFound();
		if (!User.Identity!.IsAuthenticated) return Challenge();
		if (!await access.CanReadAsync(repoObj, userManager.GetUserId(User))) return Forbid();

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
