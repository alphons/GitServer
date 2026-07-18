using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GitServer.wwwroot.Repo;

[Authorize]
public class CollaboratorsModel(
	RepositoryService repos,
	UserManager<AppUser> userManager,
	AppDbContext db,
	LocalizationService L) : PageModel
{
	public string UserName { get; set; } = "";
	public string RepoName { get; set; } = "";
	public Repository? Repo { get; set; }
	public string? Message { get; set; }
	public bool IsError { get; set; }
	public List<RepositoryAccess> Collaborators { get; set; } = new();

	[BindProperty] public string? CollaboratorName { get; set; }
	[BindProperty] public AccessLevel CollaboratorLevel { get; set; } = AccessLevel.Write;

	private async Task<(Repository? repo, bool isOwner)> LoadAsync(string user, string repo)
	{
		UserName = user;
		RepoName = repo;
		var repoObj = await repos.GetAsync(user, repo);
		if (repoObj == null) return (null, false);

		var userId = userManager.GetUserId(User);
		var isOwner = repoObj.OwnerId == userId;
		Repo = repoObj;
		return (repoObj, isOwner);
	}

	private async Task LoadCollaboratorsAsync(int repositoryId)
	{
		Collaborators = await db.RepositoryAccesses
			.Include(a => a.User)
			.Where(a => a.RepositoryId == repositoryId)
			.OrderBy(a => a.User.UserName)
			.ToListAsync();
	}

	public async Task<IActionResult> OnGetAsync(string user, string repo)
	{
		var (repoObj, isOwner) = await LoadAsync(user, repo);
		if (repoObj == null) return NotFound();
		if (!isOwner) return Forbid();

		await LoadCollaboratorsAsync(repoObj.Id);
		return Page();
	}

	public async Task<IActionResult> OnGetSearchUsersAsync(string user, string repo, string? q)
	{
		var (repoObj, isOwner) = await LoadAsync(user, repo);
		if (repoObj == null) return NotFound();
		if (!isOwner) return Forbid();

		q = q?.Trim();
		if (string.IsNullOrEmpty(q) || q.Length < 2) return new JsonResult(Array.Empty<object>());

		var lower = q.ToLower();
		var results = await db.Users
			.Where(u => u.Id != repoObj.OwnerId && (
				u.UserName!.ToLower().Contains(lower) ||
				u.Email!.ToLower().Contains(lower) ||
				u.DisplayName.ToLower().Contains(lower)))
			.OrderBy(u => u.UserName)
			.Take(10)
			.Select(u => new { userName = u.UserName, displayName = u.DisplayName, email = u.Email })
			.ToListAsync();

		return new JsonResult(results);
	}

	public async Task<IActionResult> OnPostAddCollaboratorAsync(string user, string repo)
	{
		var (repoObj, isOwner) = await LoadAsync(user, repo);
		if (repoObj == null) return NotFound();
		if (!isOwner) return Forbid();

		await LoadCollaboratorsAsync(repoObj.Id);

		var name = CollaboratorName?.Trim();
		if (string.IsNullOrEmpty(name))
		{
			Message = L["error_collaborator_name_required"];
			IsError = true;
			return Page();
		}

		var collaborator = await userManager.FindByNameAsync(name);
		if (collaborator == null)
		{
			Message = L["error_user_not_found"];
			IsError = true;
			return Page();
		}

		if (collaborator.Id == repoObj.OwnerId)
		{
			Message = L["error_collaborator_is_owner"];
			IsError = true;
			return Page();
		}

		var existing = Collaborators.FirstOrDefault(a => a.UserId == collaborator.Id);
		if (existing != null)
		{
			existing.Level = CollaboratorLevel;
		}
		else
		{
			db.RepositoryAccesses.Add(new RepositoryAccess
			{
				RepositoryId = repoObj.Id,
				UserId = collaborator.Id,
				Level = CollaboratorLevel,
			});
		}

		await db.SaveChangesAsync();
		Message = L["success_collaborator_added"];
		return RedirectToPage(new { user, repo });
	}

	public async Task<IActionResult> OnPostRemoveCollaboratorAsync(string user, string repo, int accessId)
	{
		var (repoObj, isOwner) = await LoadAsync(user, repo);
		if (repoObj == null) return NotFound();
		if (!isOwner) return Forbid();

		var access = await db.RepositoryAccesses
			.FirstOrDefaultAsync(a => a.Id == accessId && a.RepositoryId == repoObj.Id);
		if (access != null)
		{
			db.RepositoryAccesses.Remove(access);
			await db.SaveChangesAsync();
		}

		return RedirectToPage(new { user, repo });
	}
}
