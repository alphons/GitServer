using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GitServer.Controllers.Api;

/// <summary>Group management data (owner only): the group's repositories and member look-up.</summary>
[ApiController]
[Authorize]
[Route("api/groups/{id:int}")]
public class GroupsApiController(
	AppDbContext db, AccessPolicy access, UserManager<AppUser> userManager,
	RepositoryService repos, IOptions<GitServerOptions> options) : ControllerBase
{
	[HttpGet("repos")]
	public async Task<IActionResult> Repos(int id, int rp = 0)
	{
		var group = await access.GetOwnedGroupAsync(id, userManager.GetUserId(User)!);
		if (group == null) return NotFound();

		rp = Math.Max(rp, 0);
		var pageSize = options.Value.ProfileRepoPageSize;
		var total = await repos.GetGroupRepoCountAsync(id);
		var items = await repos.GetGroupReposAsync(id, skip: rp * pageSize, take: pageSize);

		return Ok(new
		{
			page = rp,
			hasNext = total > (rp + 1) * pageSize,
			repos = items.Select(r => new
			{
				displayName = $"{group.Name} / {r.Name}",
				href = $"/{group.Name}/{r.Name}",
				isPrivate = r.IsPrivate,
				isReadOnly = r.IsReadOnly,
				description = r.Description,
			}),
		});
	}

	[HttpGet("user-search")]
	public async Task<IActionResult> SearchUsers(int id, string? q)
	{
		var userId = userManager.GetUserId(User)!;
		if (await access.GetOwnedGroupAsync(id, userId) == null) return NotFound();

		q = q?.Trim();
		if (string.IsNullOrEmpty(q) || q.Length < 2) return Ok(Array.Empty<object>());

		var lower = q.ToLower();
		var results = await db.Users
			.Where(u => u.Id != userId && (
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
