namespace GitServer.Models;

public class Repository
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string OwnerId { get; set; } = "";
    public AppUser Owner { get; set; } = null!;
    public bool IsPrivate { get; set; }
    public string DefaultBranch { get; set; } = "main";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<RepositoryAccess> Accesses { get; set; } = new List<RepositoryAccess>();
    public ICollection<Issue> Issues { get; set; } = new List<Issue>();
}
