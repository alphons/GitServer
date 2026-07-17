namespace GitServer.Models;

public class IssueComment
{
    public int Id { get; set; }
    public int IssueId { get; set; }
    public Issue Issue { get; set; } = null!;
    public string AuthorId { get; set; } = "";
    public AppUser Author { get; set; } = null!;
    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
