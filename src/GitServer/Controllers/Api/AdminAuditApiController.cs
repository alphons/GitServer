using System.Text;
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
		var query = Filtered(q);

		var fetched = await query
			.OrderByDescending(a => a.At).ThenByDescending(a => a.Id)
			.Skip(page * PageSize).Take(PageSize + 1)
			.ToListAsync();

		return new AuditLogResponse(
			page, fetched.Count > PageSize,
			fetched.Take(PageSize).Select(a => new AuditEntryDto(
				a.Id, tz.FormatDateTime(a.At), a.ActorName, a.Action, a.Target, a.Details, a.Via, a.IpAddress)).ToList());
	}

	/// <summary>The same filtered log as a CSV download, newest first, with no page limit.</summary>
	/// <param name="q">Optional filter on action, user, target or details.</param>
	[HttpGet("export")]
	[ProducesResponseType(StatusCodes.Status200OK)]
	public async Task<IActionResult> Export(string? q)
	{
		if (!AccessPolicy.IsSiteAdmin(await userManager.GetUserAsync(User))) return Forbid();

		var entries = await Filtered(q).OrderByDescending(a => a.At).ThenByDescending(a => a.Id).ToListAsync();

		var csv = new StringBuilder();
		csv.AppendLine("Time,Actor,Action,Target,Details,Via,IP address");
		foreach (var a in entries)
			csv.AppendLine(string.Join(",",
				Field(tz.FormatDateTime(a.At)), Field(a.ActorName), Field(a.Action),
				Field(a.Target), Field(a.Details), Field(a.Via), Field(a.IpAddress)));

		var fileName = $"audit-log-{DateTime.UtcNow:yyyy-MM-dd}.csv";
		return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv", fileName);
	}

	private IQueryable<AuditEntry> Filtered(string? q)
	{
		var query = db.AuditEntries.AsQueryable();
		var filter = (q ?? "").Trim().ToLower();
		if (filter.Length == 0) return query;

		return query.Where(a =>
			a.Action.ToLower().Contains(filter) || a.ActorName.ToLower().Contains(filter) ||
			(a.Target != null && a.Target.ToLower().Contains(filter)) ||
			(a.Details != null && a.Details.ToLower().Contains(filter)));
	}

	private static string Field(string? value) =>
		"\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
}
