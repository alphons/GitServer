namespace GitServer.Models;

public class ReservedNamePattern
{
    public int Id { get; set; }

    /// <summary>Wildcard pattern for user and group names, e.g. "admin*" or "www". "*" = any run of
    /// characters, "?" = single character. Matched case-insensitively against the whole name.</summary>
    public string Pattern { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
