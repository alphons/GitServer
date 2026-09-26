using System.Diagnostics.CodeAnalysis;
using GitServer.Data;
using GitServer.Models;
using Microsoft.EntityFrameworkCore;

namespace GitServer.Services;

/// <summary>What the git smart-HTTP layer should do with a request once the repository (if any)
/// has been resolved. Maps 1:1 to an HTTP status: Allow = continue, Unauthorized = 401 + Basic
/// challenge, Forbidden = 403, NotFound = 404.</summary>
public enum GitAccessDecision { Allow, Unauthorized, Forbidden, NotFound }

/// <summary>
/// The one place that answers "may this user do this?" — for repositories, groups, the site-wide
/// admin role and the git push/pull decision. Pages and middleware must ask this class instead of
/// comparing OwnerId / IsAdmin themselves, so a rule (e.g. read-only repositories) can't be
/// forgotten in one of twenty call sites.
/// </summary>
public class AccessPolicy(AppDbContext db)
{
	// ---- Site ---------------------------------------------------------------------------------

	/// <summary>True if the user is flagged as a site administrator. Anonymous (null) is never an admin.</summary>
	public static bool IsSiteAdmin([NotNullWhen(true)] AppUser? user) => user is { IsAdmin: true };

	// ---- Groups -------------------------------------------------------------------------------

	public static bool IsGroupOwner(Group group, string? userId) =>
		userId != null && group.OwnerId == userId;

	public async Task<bool> IsGroupMemberAsync(int groupId, string userId) =>
		await db.GroupMembers.AnyAsync(m => m.GroupId == groupId && m.UserId == userId);

	/// <summary>The user's role in the group: <see cref="GroupRole.Admin"/> for its owner, the member's role for a member,
	/// null for anyone else.</summary>
	public async Task<GroupRole?> GetGroupRoleAsync(int groupId, string? userId)
	{
		if (userId == null) return null;
		if (await db.Groups.AnyAsync(g => g.Id == groupId && g.OwnerId == userId)) return GroupRole.Admin;
		return await db.GroupMembers
			.Where(m => m.GroupId == groupId && m.UserId == userId)
			.Select(m => (GroupRole?)m.Role)
			.FirstOrDefaultAsync();
	}

	/// <summary>The group with this id, but only if the user owns it (deleting the group).</summary>
	public async Task<Group?> GetOwnedGroupAsync(int groupId, string userId) =>
		await db.Groups.FirstOrDefaultAsync(g => g.Id == groupId && g.OwnerId == userId);

	public async Task<List<Group>> GetOwnedGroupsAsync(string userId, bool includeMembers = false)
	{
		var q = db.Groups.AsQueryable();
		if (includeMembers) q = q.Include(g => g.Members);
		return await q.Where(g => g.OwnerId == userId).OrderBy(g => g.Name).ToListAsync();
	}

	/// <summary>The group with this id, but only if the user may manage it: its owner or an admin member (the group management pages).</summary>
	public async Task<Group?> GetManagedGroupAsync(int groupId, string userId) =>
		(await ManagedGroups(userId, GroupRole.Admin).Where(g => g.Id == groupId).ToListAsync()).FirstOrDefault();

	/// <summary>Groups the user owns or is an admin of.</summary>
	public async Task<List<Group>> GetManagedGroupsAsync(string userId, bool includeMembers = false)
	{
		var q = ManagedGroups(userId, GroupRole.Admin);
		if (includeMembers) q = q.Include(g => g.Members);
		return await q.OrderBy(g => g.Name).ToListAsync();
	}

	/// <summary>Groups the user may create or fork repositories into: owned, or member with at least write.</summary>
	public async Task<List<Group>> GetGroupsForRepoCreationAsync(string userId) =>
		await ManagedGroups(userId, GroupRole.Write).OrderBy(g => g.Name).ToListAsync();

	private IQueryable<Group> ManagedGroups(string userId, GroupRole minimum) =>
		db.Groups.Where(g => g.OwnerId == userId || g.Members.Any(m => m.UserId == userId && m.Role >= minimum));

	/// <summary>May this user create a repository in the group's namespace (owner, or member with at least write)?</summary>
	public async Task<bool> CanCreateRepoInGroupAsync(Group group, string userId) =>
		await GetGroupRoleAsync(group.Id, userId) >= GroupRole.Write;

	// ---- Repositories -------------------------------------------------------------------------

	/// <summary>True if the user owns the repository outright, or owns or is an admin of the group that owns it.
	/// Owners administer a repository (settings, collaborators, deletion).</summary>
	public async Task<bool> IsOwnerAsync(Repository repo, string? userId)
	{
		if (userId == null) return false;
		if (repo.OwnerId == userId) return true;
		if (repo.GroupOwnerId == null) return false;
		return await GetGroupRoleAsync(repo.GroupOwnerId.Value, userId) == GroupRole.Admin;
	}

	public Task<bool> CanAdministerAsync(Repository repo, string? userId) => IsOwnerAsync(repo, userId);

	/// <summary>Owner may delete; so may a site admin (used to clean up repos whose git data is gone).</summary>
	public async Task<bool> CanDeleteAsync(Repository repo, AppUser? user) =>
		user != null && (IsSiteAdmin(user) || await IsOwnerAsync(repo, user.Id));

	public async Task<bool> CanReadAsync(Repository repo, string? userId)
	{
		if (!repo.IsPrivate) return true;
		if (userId == null) return false;
		if (await IsOwnerAsync(repo, userId)) return true;
		if (repo.GroupOwnerId != null && await IsGroupMemberAsync(repo.GroupOwnerId.Value, userId)) return true;
		return await db.RepositoryAccesses
			.AnyAsync(a => a.RepositoryId == repo.Id &&
				(a.UserId == userId || (a.GroupId != null && a.Group!.Members.Any(m => m.UserId == userId))));
	}

	/// <summary>A read-only repository can never be written to — by anyone, owner included.</summary>
	public async Task<bool> CanWriteAsync(Repository repo, string? userId)
	{
		if (repo.IsReadOnly) return false;
		if (userId == null) return false;
		if (repo.OwnerId == userId) return true;
		if (repo.GroupOwnerId != null && await GetGroupRoleAsync(repo.GroupOwnerId.Value, userId) >= GroupRole.Write) return true;
		// Write access granted to a group reaches its members with at least the write role; read members stay readers.
		return await db.RepositoryAccesses
			.AnyAsync(a => a.RepositoryId == repo.Id && a.Level == AccessLevel.Write &&
				(a.UserId == userId || (a.GroupId != null && a.Group!.Members.Any(m => m.UserId == userId && m.Role >= GroupRole.Write))));
	}

	/// <summary>Issue authors can manage (close/reopen) their own issue; anyone with write access can
	/// manage any issue.</summary>
	public async Task<bool> CanManageIssueAsync(Repository repo, Issue? issue, string? userId) =>
		userId != null && (issue?.AuthorId == userId || await CanWriteAsync(repo, userId));

	// ---- Git smart-HTTP -----------------------------------------------------------------------

	/// <summary>Decision for a clone/fetch (<paramref name="isPush"/> false) or push of an existing
	/// repository. <paramref name="user"/> is the credentials-authenticated user, or null.</summary>
	public async Task<GitAccessDecision> DecideGitAccessAsync(
		Repository repo, AppUser? user, bool isPush, bool allowAnonymousPush)
	{
		// Read-only trumps everything, including "allow anonymous push".
		if (isPush && repo.IsReadOnly) return GitAccessDecision.Forbidden;

		// Plain reads of a public repository need no credentials.
		if (!repo.IsPrivate && !isPush) return GitAccessDecision.Allow;

		var anonymousPushAllowed = isPush && !repo.IsPrivate && allowAnonymousPush;
		if (user == null && !anonymousPushAllowed) return GitAccessDecision.Unauthorized;

		if (isPush)
			return anonymousPushAllowed || await CanWriteAsync(repo, user?.Id)
				? GitAccessDecision.Allow
				: GitAccessDecision.Forbidden;

		return await CanReadAsync(repo, user?.Id) ? GitAccessDecision.Allow : GitAccessDecision.Forbidden;
	}

	/// <summary>Decision for a push to a repository name that doesn't exist yet: may it be created on
	/// the fly? Exactly one of <paramref name="namespaceUser"/> / <paramref name="namespaceGroup"/> is the
	/// owner of the URL's first segment.</summary>
	public async Task<GitAccessDecision> DecideAutoCreateAsync(
		AppUser? user, AppUser? namespaceUser, Group? namespaceGroup, bool allowPushToCreate)
	{
		if (!allowPushToCreate) return GitAccessDecision.NotFound;
		if (user == null) return GitAccessDecision.Unauthorized;

		if (namespaceUser != null)
			return user.Id == namespaceUser.Id ? GitAccessDecision.Allow : GitAccessDecision.Forbidden;

		if (namespaceGroup != null)
			return await CanCreateRepoInGroupAsync(namespaceGroup, user.Id)
				? GitAccessDecision.Allow
				: GitAccessDecision.Forbidden;

		return GitAccessDecision.NotFound;
	}
}
