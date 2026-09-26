using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GitServer.Pages.User;

[Authorize]
public class GroupDetailModel(AppDbContext db, AccessPolicy access, UserManager<AppUser> userManager, RepositoryService repos, LocalizationService L) : PageModel
{
	public AppUser? CurrentUser { get; set; }
	public Group? Group { get; set; }
	public List<GroupMember> Members { get; set; } = new();
	public int RepoTotalCount { get; set; }
	public string? Message { get; set; }
	public bool IsError { get; set; }
	/// <summary>Only the owner may delete the group; admin members manage everything else.</summary>
	public bool IsOwner => Group != null && AccessPolicy.IsGroupOwner(Group, CurrentUser?.Id);

	[BindProperty] public string? MemberName { get; set; }
	[BindProperty] public GroupRole MemberRole { get; set; } = GroupRole.Write;

	private async Task<bool> LoadAsync(int id)
	{
		CurrentUser = await userManager.GetUserAsync(User);
		if (CurrentUser == null) return false;

		Group = await access.GetManagedGroupAsync(id, CurrentUser.Id);
		if (Group == null) return false;

		Members = await db.GroupMembers
			.Include(m => m.User)
			.Where(m => m.GroupId == id)
			.OrderBy(m => m.User.UserName)
			.ToListAsync();

		RepoTotalCount = await repos.GetGroupRepoCountAsync(id);

		return true;
	}

	public async Task<IActionResult> OnGetAsync(int id)
	{
		if (!await LoadAsync(id)) return NotFound();
		return Page();
	}

	public async Task<IActionResult> OnPostAddMemberAsync(int id)
	{
		if (!await LoadAsync(id)) return NotFound();

		var name = MemberName?.Trim();
		if (string.IsNullOrEmpty(name))
		{
			Message = L["error_collaborator_name_required"];
			IsError = true;
			return Page();
		}

		var member = await userManager.FindByNameAsync(name);
		if (member == null)
		{
			Message = L["error_user_not_found"];
			IsError = true;
			return Page();
		}

		if (member.Id == Group!.OwnerId)
		{
			Message = L["error_group_member_is_owner"];
			IsError = true;
			return Page();
		}

		if (!Members.Any(m => m.UserId == member.Id))
		{
			db.GroupMembers.Add(new GroupMember { GroupId = Group!.Id, UserId = member.Id, Role = MemberRole });
			await db.SaveChangesAsync();
		}

		Message = L["success_group_member_added"];
		return RedirectToPage(new { id });
	}

	public async Task<IActionResult> OnPostRemoveMemberAsync(int id, int memberId)
	{
		if (!await LoadAsync(id)) return NotFound();

		var member = await db.GroupMembers.FirstOrDefaultAsync(m => m.Id == memberId && m.GroupId == id);
		if (member != null)
		{
			db.GroupMembers.Remove(member);
			await db.SaveChangesAsync();
		}

		return RedirectToPage(new { id });
	}

	public async Task<IActionResult> OnPostChangeRoleAsync(int id, int memberId, GroupRole role)
	{
		if (!await LoadAsync(id)) return NotFound();
		if (!Enum.IsDefined(role)) return BadRequest();

		var member = Members.FirstOrDefault(m => m.Id == memberId);
		if (member != null)
		{
			member.Role = role;
			await db.SaveChangesAsync();
		}

		return RedirectToPage(new { id });
	}

	public async Task<IActionResult> OnPostDeleteAsync(int id)
	{
		if (!await LoadAsync(id)) return NotFound();
		if (!IsOwner) return Forbid();

		// Removed here instead of by database cascades so that every provider behaves the same: SQL Server does not allow the
		// several cascade paths (group -> repositories -> access rows, group -> access rows) that SQLite follows implicitly.
		await using var transaction = await db.Database.BeginTransactionAsync();
		await db.RepositoryAccesses.Where(a => a.GroupId == id).ExecuteDeleteAsync();
		await repos.DetachForksAsync(db.Repositories.Where(r => r.GroupOwnerId == id).Select(r => r.Id));   // forks elsewhere survive
		await db.Repositories.Where(r => r.GroupOwnerId == id).ExecuteDeleteAsync();   // issues, comments and their access rows go with them
		db.Groups.Remove(Group!);
		await db.SaveChangesAsync();
		await transaction.CommitAsync();

		return RedirectToPage("/User/Groups");
	}
}
