namespace GitServer.Models;

public enum AccessLevel { Read, Write }

public class RepositoryAccess
{
    public int Id { get; set; }
    public int RepositoryId { get; set; }
    public Repository Repository { get; set; } = null!;
    public string UserId { get; set; } = "";
    public AppUser User { get; set; } = null!;
    public AccessLevel Level { get; set; }
}
