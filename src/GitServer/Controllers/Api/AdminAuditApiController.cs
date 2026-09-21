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
[Route("api/admin/audit")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class AdminAuditApiController(UserManager<AppUser> userManager, AppDbContext db, TimeZoneService tz) : ControllerBase
{
	public const int PageSize = 50;

	/// <summary>The audit log, newest first: administrative actions, API key changes and (once an hour per key) API key use.</summary>
	/// <param name="q">Optional filter on action, user, target or details.</param>
	/// <param name="p">Page, starting at 0.</param>
	[HttpGet]
	[ProducesResponseType<AuditLogResponse>(StatusCodes.Status200OK)]
	public async Task<ActionResult<AuditLogResponse>> Get(string? q, int p = 0)
	{
		if (!AccessPolicy.IsSiteAdmin(await userManager.GetUserAsync(User))) return Forbid();

		var page = Math.Max(p, 0);
		var query = db.AuditEntries.AsQueryable();
		var filter = (q ?? "").Trim().ToLower();
		if (filter.Length > 0)
			query = query.Where(a =>
				a.Action.ToLower().Contains(filter) || a.ActorName.ToLower().Contains(filter) ||
				(a.Target != null && a.Target.ToLower().Contains(filter)) ||
				(a.Details != null && a.Details.ToLower().Contains(filter)));

		var fetched = await query
			.OrderByDescending(a => a.At).ThenByDescending(a => a.Id)
			.Skip(page * PageSize).Take(PageSize + 1)
			.ToListAsync();

		return new AuditLogResponse(
			page, fetched.Count > PageSize,
			fetched.Take(PageSize).Select(a => new AuditEntryDto(
				a.Id, tz.FormatDateTime(a.At), a.ActorName, a.Action, a.Target, a.Details, a.Via, a.IpAddress)).ToList());
	}
}
