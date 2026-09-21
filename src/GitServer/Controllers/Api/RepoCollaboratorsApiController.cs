using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GitServer.Controllers.Api;

[ApiController]
[Authorize]
[Route("api/repos/{user}/{repo}")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public class RepoCollaboratorsApiController(
	RepositoryService repos, AccessPolicy access, UserManager<AppUser> userManager, AppDbContext db) : ControllerBase
{
	/// <summary>Finds users that can be granted access to the repository (everyone but its owner), for the collaborator autocomplete.
	/// Needs at least 2 characters. Only the repository's owner may call this.</summary>
	/// <param name="user">The repository's owner.</param>
	/// <param name="repo">The repository's name.</param>
	/// <param name="q">Part of a user name, display name or e-mail address.</param>
	[HttpGet("user-search")]
	[ProducesResponseType(StatusCodes.Status403Forbidden)]
	[ProducesResponseType<IReadOnlyList<UserSearchResult>>(StatusCodes.Status200OK)]
	public async Task<ActionResult<IReadOnlyList<UserSearchResult>>> SearchUsers(string user, string repo, string? q)
	{
		var repoObj = await repos.GetAsync(user, repo);
		if (repoObj == null) return NotFound();
		if (!await access.CanAdministerAsync(repoObj, userManager.GetUserId(User))) return Forbid();

		q = q?.Trim();
		if (string.IsNullOrEmpty(q) || q.Length < 2) return Array.Empty<UserSearchResult>();

		var lower = q.ToLower();
		return await db.Users
			.Where(u => u.Id != repoObj.OwnerId && (
				u.UserName!.ToLower().Contains(lower) ||
				u.Email!.ToLower().Contains(lower) ||
				u.DisplayName.ToLower().Contains(lower)))
			.OrderBy(u => u.UserName)
			.Take(10)
			.Select(u => new UserSearchResult(u.UserName, u.DisplayName, u.Email))
			.ToListAsync();
	}
}
