using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Regex = System.Text.RegularExpressions.Regex;

namespace GitServer.Pages.User;

[Authorize]
public class GroupsModel(AppDbContext db, AccessPolicy access, UserManager<AppUser> userManager, LocalizationService L, ReservedNames reserved) : PageModel
{
	public AppUser? CurrentUser { get; set; }
	public List<Group> Groups { get; set; } = new();
	public string? Message { get; set; }
	public bool IsError { get; set; }

	[BindProperty] public string NewGroupName { get; set; } = "";

	private async Task LoadAsync()
	{
		CurrentUser = await userManager.GetUserAsync(User);
		if (CurrentUser == null) return;

		Groups = await access.GetOwnedGroupsAsync(CurrentUser.Id, includeMembers: true);
	}

	public async Task OnGetAsync()
	{
		await LoadAsync();
	}

	public async Task<IActionResult> OnPostCreateAsync()
	{
		await LoadAsync();
		if (CurrentUser == null) return Challenge();

		var name = NewGroupName?.Trim();
		if (string.IsNullOrEmpty(name))
		{
			Message = L["error_group_name_required"];
			IsError = true;
			return Page();
		}

		// Group names double as a URL namespace segment alongside usernames, so they share the
		// same character set and must be unique across both groups and users, not just per-owner.
		if (name.Length > 100 || !Regex.IsMatch(name, @"^[a-zA-Z0-9_\-]+$"))
		{
			Message = L["error_invalid_group_name"];
			IsError = true;
			return Page();
		}

		if (await reserved.IsReservedAsync(name))
		{
			Message = L["error_name_reserved"];
			IsError = true;
			return Page();
		}

		var lower = name.ToLower();
		var nameTaken = await db.Groups.AnyAsync(g => g.Name.ToLower() == lower)
			|| await db.Users.AnyAsync(u => u.UserName!.ToLower() == lower);
		if (nameTaken)
		{
			Message = L["error_group_name_taken"];
			IsError = true;
			return Page();
		}

		var group = new Group { Name = name, OwnerId = CurrentUser.Id };
		db.Groups.Add(group);
		await db.SaveChangesAsync();

		return RedirectToPage("/User/GroupDetail", new { id = group.Id });
	}
}
