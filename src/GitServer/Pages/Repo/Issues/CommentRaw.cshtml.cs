using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GitServer.Pages.Repo.Issues;

public class CommentRawModel(
	RepositoryService repos,
	AppDbContext db,
	UserManager<AppUser> userManager) : PageModel
{
	public async Task<IActionResult> OnGetAsync(string user, string repo, int id, int commentId)
	{
		var repoObj = await repos.GetAsync(user, repo);
		if (repoObj == null) return NotFound();

		var userId = userManager.GetUserId(User);
		if (!await repos.CanReadAsync(repoObj, userId)) return Forbid();

		var body = await db.IssueComments
			.Where(c => c.Id == commentId && c.IssueId == id && c.Issue.RepositoryId == repoObj.Id)
			.Select(c => c.Body)
			.FirstOrDefaultAsync();
		if (body == null) return NotFound();

		return Content(body, "text/plain; charset=utf-8");
	}
}
