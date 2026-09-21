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
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class AdminUsersApiController(
	UserManager<AppUser> userManager, IOptions<GitServerOptions> options,
	TimeZoneService tz, LocalizationService L) : ControllerBase
{
	/// <summary>Lists all users for site administrators: confirmed accounts first, then by user name.</summary>
	/// <param name="q">Optional filter on user name, display name or e-mail address.</param>
	/// <param name="p">Page, starting at 0.</param>
	[HttpGet]
	[ProducesResponseType<AdminUsersResponse>(StatusCodes.Status200OK)]
	public async Task<ActionResult<AdminUsersResponse>> Get(string? q, int p = 0)
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

		return new AdminUsersResponse(
			page, fetched.Count > pageSize,
			Math.Max(pageSize > 0 ? (int)Math.Ceiling(totalCount / (double)pageSize) : 1, 1),
			fetched.Take(pageSize).Select(u => new AdminUserDto(
				u.Id, u.UserName, u.DisplayName, u.Email,
				tz.ToLocal(u.CreatedAt).ToString("d MMM yyyy", L.CurrentCulture),
				u.LastLoginAt.HasValue ? tz.ToLocal(u.LastLoginAt.Value).ToString("d MMM yyyy HH:mm", L.CurrentCulture) : null,
				u.IsDisabled, u.IsAdmin, !u.EmailConfirmed, u.Id == currentUser.Id)).ToList());
	}
}
