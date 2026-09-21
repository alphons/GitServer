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
public class RepoCollaboratorsApiController(
	RepositoryService repos, AccessPolicy access, UserManager<AppUser> userManager, AppDbContext db) : ControllerBase
{
	/// <summary>Users that can be granted access to the repository (everyone but its owner), for the collaborator autocomplete.</summary>
	[HttpGet("user-search")]
	public async Task<IActionResult> SearchUsers(string user, string repo, string? q)
	{
		var repoObj = await repos.GetAsync(user, repo);
		if (repoObj == null) return NotFound();
		if (!await access.CanAdministerAsync(repoObj, userManager.GetUserId(User))) return Forbid();

		q = q?.Trim();
		if (string.IsNullOrEmpty(q) || q.Length < 2) return Ok(Array.Empty<object>());

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

		return Ok(results);
	}
}
