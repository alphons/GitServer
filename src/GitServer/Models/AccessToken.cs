namespace GitServer.Models;

/// <summary>A personal access token: used instead of the account password for git over HTTPS.
/// Only a hash is stored; the token itself is shown once, when it is created.</summary>
public class AccessToken
{
	public int Id { get; set; }
	public string UserId { get; set; } = "";
	public AppUser User { get; set; } = null!;
	public string Name { get; set; } = "";
	public string TokenHash { get; set; } = "";
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public DateTime? ExpiresAt { get; set; }
	public DateTime? LastUsedAt { get; set; }
}
