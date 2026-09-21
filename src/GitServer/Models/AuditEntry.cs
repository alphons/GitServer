namespace GitServer.Models;

/// <summary>One recorded administrative or security-relevant action: who did what, to what, from where.</summary>
public class AuditEntry
{
	public int Id { get; set; }
	public DateTime At { get; set; } = DateTime.UtcNow;

	/// <summary>The acting user's id, or null for actions without a signed-in user. Not a foreign key, so the entry outlives the account.</summary>
	public string? ActorUserId { get; set; }
	public string ActorName { get; set; } = "";

	/// <summary>Machine-readable action, e.g. "user.delete" or "apikey.create".</summary>
	public string Action { get; set; } = "";
	public string? Target { get; set; }
	public string? Details { get; set; }

	/// <summary>"web" (sign-in cookie) or "api-key".</summary>
	public string Via { get; set; } = "web";
	public string? IpAddress { get; set; }
}
