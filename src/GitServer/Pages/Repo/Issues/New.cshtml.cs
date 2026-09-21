using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.Pages.Repo.Issues;

[Authorize]
public class NewIssueModel(
	RepositoryService repos, AccessPolicy access, 
	AppDbContext db, 
	UserManager<AppUser> userManager) : PageModel
{

	public string UserName { get; set; } = "";
	public string RepoName { get; set; } = "";
	public bool IsGroupOwner { get; set; }
	[BindProperty] public string Title { get; set; } = "";
	[BindProperty] public string Body { get; set; } = "";
	public string? ErrorMessage { get; set; }

	public async Task<IActionResult> OnGetAsync(string user, string repo)
	{
		UserName = user; RepoName = repo;
		var repoObj = await repos.GetAsync(user, repo);
		if (repoObj == null) return NotFound();
		IsGroupOwner = repoObj.GroupOwnerId != null;
		UserName = repoObj.OwnerName;
		RepoName = repoObj.Name;
		var userId = userManager.GetUserId(User);
		if (!await access.CanReadAsync(repoObj, userId)) return Forbid();
		return Page();
	}

	public async Task<IActionResult> OnPostAsync(string user, string repo)
	{
		UserName = user; RepoName = repo;
		var repoObj = await repos.GetAsync(user, repo);
		if (repoObj == null) return NotFound();
		var userId = userManager.GetUserId(User)!;
		if (!await access.CanReadAsync(repoObj, userId)) return Forbid();   // same rule as opening the form

		var issue = new Issue
		{
			RepositoryId = repoObj.Id,
			AuthorId = userId,
			Title = Title,
			Body = Body,
		};
		db.Issues.Add(issue);
		await db.SaveChangesAsync();

		return RedirectToPage("/Repo/Issues/Detail", new { user, repo, id = issue.Id });
	}
}
