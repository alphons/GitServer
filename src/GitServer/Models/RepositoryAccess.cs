namespace GitServer.Models;

public enum AccessLevel { Read, Write }

public class RepositoryAccess
{
    public int Id { get; set; }
    public int RepositoryId { get; set; }
    public Repository Repository { get; set; } = null!;
    public string? UserId { get; set; }
    public AppUser? User { get; set; }
    public int? GroupId { get; set; }
    public Group? Group { get; set; }
    public AccessLevel Level { get; set; }
}
