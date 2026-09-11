using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GitServer.Pages.Repo.Issues;

public class RawModel(
	RepositoryService repos,
	AppDbContext db,
	UserManager<AppUser> userManager) : PageModel
{
	public async Task<IActionResult> OnGetAsync(string user, string repo, int id)
	{
		var repoObj = await repos.GetAsync(user, repo);
		if (repoObj == null) return NotFound();

		var userId = userManager.GetUserId(User);
		if (!await repos.CanReadAsync(repoObj, userId)) return Forbid();

		var body = await db.Issues
			.Where(i => i.RepositoryId == repoObj.Id && i.Id == id)
			.Select(i => i.Body)
			.FirstOrDefaultAsync();
		if (body == null) return NotFound();

		return Content(body, "text/plain; charset=utf-8");
	}
}
