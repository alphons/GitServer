using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GitServer.wwwroot.User;

[Authorize]
public class GroupDetailModel(AppDbContext db, UserManager<AppUser> userManager, LocalizationService L) : PageModel
{
	public AppUser? CurrentUser { get; set; }
	public Group? Group { get; set; }
	public List<GroupMember> Members { get; set; } = new();
	public string? Message { get; set; }
	public bool IsError { get; set; }

	[BindProperty] public string? MemberName { get; set; }

	private async Task<bool> LoadAsync(int id)
	{
		CurrentUser = await userManager.GetUserAsync(User);
		if (CurrentUser == null) return false;

		Group = await db.Groups.FirstOrDefaultAsync(g => g.Id == id && g.OwnerId == CurrentUser.Id);
		if (Group == null) return false;

		Members = await db.GroupMembers
			.Include(m => m.User)
			.Where(m => m.GroupId == id)
			.OrderBy(m => m.User.UserName)
			.ToListAsync();

		return true;
	}

	public async Task<IActionResult> OnGetAsync(int id)
	{
		if (!await LoadAsync(id)) return NotFound();
		return Page();
	}

	public async Task<IActionResult> OnGetSearchUsersAsync(int id, string? q)
	{
		if (!await LoadAsync(id)) return NotFound();

		q = q?.Trim();
		if (string.IsNullOrEmpty(q) || q.Length < 2) return new JsonResult(Array.Empty<object>());

		var lower = q.ToLower();
		var memberIds = Members.Select(m => m.UserId).ToHashSet();
		var results = await db.Users
			.Where(u => u.Id != CurrentUser!.Id && (
				u.UserName!.ToLower().Contains(lower) ||
				u.Email!.ToLower().Contains(lower) ||
				u.DisplayName.ToLower().Contains(lower)))
			.OrderBy(u => u.UserName)
			.Take(10)
			.Select(u => new { userName = u.UserName, displayName = u.DisplayName, email = u.Email })
			.ToListAsync();

		return new JsonResult(results);
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

		if (member.Id == CurrentUser!.Id)
		{
			Message = L["error_group_member_is_owner"];
			IsError = true;
			return Page();
		}

		if (!Members.Any(m => m.UserId == member.Id))
		{
			db.GroupMembers.Add(new GroupMember { GroupId = Group!.Id, UserId = member.Id });
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

	public async Task<IActionResult> OnPostDeleteAsync(int id)
	{
		if (!await LoadAsync(id)) return NotFound();

		db.Groups.Remove(Group!);
		await db.SaveChangesAsync();

		return RedirectToPage("/User/Groups");
	}
}
