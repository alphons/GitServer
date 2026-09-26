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

	/// <summary>When true, no one (including the owner) can push to this repository — reads only.</summary>
	public bool IsReadOnly { get; set; }

	public string DefaultBranch { get; set; } = "main";
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

	/// <summary>True for a repository created as a fork, even after its source is deleted (then <see cref="ForkedFromId"/> is null).</summary>
	public bool IsFork { get; set; }

	/// <summary>The repository this one was forked from, or null when it is not a fork or its source was deleted.</summary>
	public int? ForkedFromId { get; set; }
	public Repository? ForkedFrom { get; set; }
	public ICollection<Repository> Forks { get; set; } = new List<Repository>();

	public ICollection<RepositoryAccess> Accesses { get; set; } = new List<RepositoryAccess>();
	public ICollection<Issue> Issues { get; set; } = new List<Issue>();

	/// <summary>The URL/path segment this repository lives under: the owning user's or group's name.</summary>
	public string OwnerName => Owner?.UserName ?? GroupOwner?.Name ?? "";
}
