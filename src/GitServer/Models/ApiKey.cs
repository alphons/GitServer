namespace GitServer.Models;

/// <summary>A key a user can send in the X-Api-Key header to call the JSON API as themselves.
/// Only a hash is stored; the key itself is shown once, when it is created.</summary>
public class ApiKey
{
	public int Id { get; set; }
	public string UserId { get; set; } = "";
	public AppUser User { get; set; } = null!;
	public string Name { get; set; } = "";

	/// <summary>The first characters of the key, so the owner can recognise it in the list.</summary>
	public string KeyPrefix { get; set; } = "";
	public string KeyHash { get; set; } = "";

	public bool IsEnabled { get; set; } = true;
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public DateTime ExpiresAt { get; set; }
	public DateTime? LastUsedAt { get; set; }
}
