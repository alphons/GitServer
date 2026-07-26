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
public class GroupsModel(AppDbContext db, UserManager<AppUser> userManager, LocalizationService L) : PageModel
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

		Groups = await db.Groups
			.Include(g => g.Members)
			.Where(g => g.OwnerId == CurrentUser.Id)
			.OrderBy(g => g.Name)
			.ToListAsync();
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

		if (Groups.Any(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
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
