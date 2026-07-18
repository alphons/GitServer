namespace GitServer.Models;

public class BlockedEmailPattern
{
    public int Id { get; set; }

    /// <summary>Wildcard pattern, e.g. "*hacker*.com" or "*@spam.net". "*" = any run of
    /// characters, "?" = single character. Matched case-insensitively against the full email.</summary>
    public string Pattern { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
