using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GitServer.Controllers.Api;

[ApiController]
[Authorize]
[Route("api/admin/users")]
public class AdminUsersApiController(
	UserManager<AppUser> userManager, IOptions<GitServerOptions> options,
	TimeZoneService tz, LocalizationService L) : ControllerBase
{
	[HttpGet]
	public async Task<IActionResult> Get(string? q, int p = 0)
	{
		var currentUser = await userManager.GetUserAsync(User);
		if (!AccessPolicy.IsSiteAdmin(currentUser)) return Forbid();

		var filter = (q ?? "").Trim();
		var page = Math.Max(p, 0);
		var pageSize = options.Value.AdminUsersPageSize;

		var query = userManager.Users.AsQueryable();
		if (filter.Length > 0)
		{
			var lower = filter.ToLower();
			query = query.Where(u =>
				u.UserName!.ToLower().Contains(lower) ||
				u.DisplayName.ToLower().Contains(lower) ||
				u.Email!.ToLower().Contains(lower));
		}

		var totalCount = await query.CountAsync();
		var fetched = await query
			.OrderByDescending(u => u.EmailConfirmed)
			.ThenBy(u => u.UserName)
			.Skip(page * pageSize)
			.Take(pageSize + 1)
			.ToListAsync();

		return Ok(new
		{
			page,
			hasNext = fetched.Count > pageSize,
			totalPages = Math.Max(pageSize > 0 ? (int)Math.Ceiling(totalCount / (double)pageSize) : 1, 1),
			users = fetched.Take(pageSize).Select(u => new
			{
				id = u.Id,
				userName = u.UserName,
				displayName = u.DisplayName,
				email = u.Email,
				created = tz.ToLocal(u.CreatedAt).ToString("d MMM yyyy", L.CurrentCulture),
				lastLogin = u.LastLoginAt.HasValue
					? tz.ToLocal(u.LastLoginAt.Value).ToString("d MMM yyyy HH:mm", L.CurrentCulture)
					: null,
				isDisabled = u.IsDisabled,
				isAdmin = u.IsAdmin,
				isPending = !u.EmailConfirmed,
				isSelf = u.Id == currentUser.Id,
			}),
		});
	}
}
