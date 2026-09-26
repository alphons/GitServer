namespace GitServer.Models;

/// <summary>What a member may do with the group's repositories. The group's owner is not a member row and can always do everything.</summary>
public enum GroupRole
{
	/// <summary>Clone, browse and open issues.</summary>
	Read = 0,
	/// <summary>Also push, and create or fork repositories into the group.</summary>
	Write = 1,
	/// <summary>Also administer the group's repositories and manage the group's members (but not delete the group).</summary>
	Admin = 2,
}

public class GroupMember
{
	public int Id { get; set; }
	public int GroupId { get; set; }
	public Group Group { get; set; } = null!;
	public string UserId { get; set; } = "";
	public AppUser User { get; set; } = null!;
	public GroupRole Role { get; set; } = GroupRole.Write;
}
