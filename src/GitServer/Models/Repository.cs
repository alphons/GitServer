namespace GitServer.Models;

public class Repository
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    // Exactly one of (OwnerId, Owner) / (GroupOwnerId, GroupOwner) is set: a repository
    // belongs either to a single user or to a group, never both.
    public string? OwnerId { get; set; }
    public AppUser? Owner { get; set; }
    public int? GroupOwnerId { get; set; }
    public Group? GroupOwner { get; set; }

    public bool IsPrivate { get; set; }
    public string DefaultBranch { get; set; } = "main";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<RepositoryAccess> Accesses { get; set; } = new List<RepositoryAccess>();
    public ICollection<Issue> Issues { get; set; } = new List<Issue>();

    /// <summary>The URL/path segment this repository lives under: the owning user's or group's name.</summary>
    public string OwnerName => Owner?.UserName ?? GroupOwner?.Name ?? "";
}
