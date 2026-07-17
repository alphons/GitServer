namespace GitServer.Models;

public class Issue
{
    public int Id { get; set; }
    public int RepositoryId { get; set; }
    public Repository Repository { get; set; } = null!;
    public string AuthorId { get; set; } = "";
    public AppUser Author { get; set; } = null!;
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public bool IsClosed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<IssueComment> Comments { get; set; } = new List<IssueComment>();
}
