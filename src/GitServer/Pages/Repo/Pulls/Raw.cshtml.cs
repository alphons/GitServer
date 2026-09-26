using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GitServer.Pages.Repo.Pulls;

/// <summary>The Markdown source of a pull request's description or of one of its comments, rendered in the browser.
/// Serves both Raw.cshtml and CommentRaw.cshtml.</summary>
public class PullRawModel(RepositoryService repos, AccessPolicy access, AppDbContext db, UserManager<AppUser> userManager) : PageModel
{
	public async Task<IActionResult> OnGetAsync(string user, string repo, int id, int? commentId)
	{
		var repoObj = await repos.GetAsync(user, repo);
		if (repoObj == null) return NotFound();
		if (!await access.CanReadAsync(repoObj, userManager.GetUserId(User))) return Forbid();

		var body = commentId == null
			? await db.PullRequests.Where(p => p.RepositoryId == repoObj.Id && p.Id == id).Select(p => p.Body).FirstOrDefaultAsync()
			: await db.PullRequestComments.Where(c => c.Id == commentId && c.PullRequestId == id && c.PullRequest.RepositoryId == repoObj.Id)
				.Select(c => c.Body).FirstOrDefaultAsync();
		return body == null ? NotFound() : Content(body, "text/plain; charset=utf-8");
	}
}
