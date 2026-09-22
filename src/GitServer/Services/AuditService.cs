using System.Security.Claims;
using GitServer.Data;
using GitServer.Extensions;
using GitServer.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GitServer.Services;

/// <summary>Writes the audit log. The actor, the way they signed in and their IP address are taken from the current request.</summary>
public class AuditService(AppDbContext db, IHttpContextAccessor httpContextAccessor, IOptions<GitServerOptions> options)
{
	/// <summary>Records an action by the signed-in user of the current request.</summary>
	/// <param name="action">Machine-readable action such as "user.delete".</param>
	/// <param name="target">What it was done to (a user name, a key name, ...).</param>
	/// <param name="details">Extra context, e.g. the changed values.</param>
	public async Task WriteAsync(string action, string? target = null, string? details = null)
	{
		var http = httpContextAccessor.HttpContext;
		var user = http?.User;
		db.AuditEntries.Add(new AuditEntry
		{
			ActorUserId = user?.FindFirstValue(ClaimTypes.NameIdentifier),
			ActorName = user?.Identity?.Name ?? "",
			Action = action,
			Target = Trim(target, 200),
			Details = Trim(details, 500),
			Via = user != null && user.IsApiKeyRequest() ? "api-key" : "web",
			IpAddress = http?.Connection.RemoteIpAddress?.ToString(),
		});
		await db.SaveChangesAsync();
		await PruneOccasionallyAsync();
	}

	/// <summary>Records an action by a known user when there is no authenticated request yet (e.g. while an API key is being checked).</summary>
	public async Task WriteAsAsync(AppUser actor, string via, string action, string? target = null, string? details = null)
	{
		db.AuditEntries.Add(new AuditEntry
		{
			ActorUserId = actor.Id,
			ActorName = actor.UserName ?? "",
			Action = action,
			Target = Trim(target, 200),
			Details = Trim(details, 500),
			Via = via,
			IpAddress = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
		});
		await db.SaveChangesAsync();
		await PruneOccasionallyAsync();
	}

	private static string? Trim(string? value, int max) => value is { Length: > 0 } && value.Length > max ? value[..max] : value;

	/// <summary>Runs <see cref="PruneExpiredAsync"/> on roughly 1% of writes instead of a background timer,
	/// so there is no extra process to host and the table still never grows unbounded in practice.</summary>
	private Task PruneOccasionallyAsync() => Random.Shared.Next(100) == 0 ? PruneExpiredAsync() : Task.CompletedTask;

	/// <summary>Deletes entries older than <see cref="GitServerOptions.AuditLogRetentionDays"/> (a no-op when that is 0).</summary>
	public async Task PruneExpiredAsync()
	{
		var days = options.Value.AuditLogRetentionDays;
		if (days <= 0) return;

		var cutoff = DateTime.UtcNow.AddDays(-days);
		await db.AuditEntries.Where(a => a.At < cutoff).ExecuteDeleteAsync();
	}
}
