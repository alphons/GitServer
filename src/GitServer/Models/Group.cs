namespace GitServer.Models;

public class Group
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public AppUser Owner { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<GroupMember> Members { get; set; } = new List<GroupMember>();
    public ICollection<RepositoryAccess> Accesses { get; set; } = new List<RepositoryAccess>();
}
