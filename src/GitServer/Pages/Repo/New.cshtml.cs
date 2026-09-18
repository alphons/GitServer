using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Regex = System.Text.RegularExpressions.Regex;

namespace GitServer.Pages.Repo;

[Authorize]
public class NewModel(
	RepositoryService repos,
	UserManager<AppUser> userManager,
	SiteSettingsService siteSettings,
	AppDbContext db,
	LocalizationService L) : PageModel
{

	[BindProperty] public string Name { get; set; } = "";
	[BindProperty] public string? Description { get; set; }
	[BindProperty] public bool IsPrivate { get; set; }
	[BindProperty] public int? GroupOwnerId { get; set; }
	public string? ErrorMessage { get; set; }
	public AppUser? CurrentUser { get; set; }
	public bool CreationDisabled { get; set; }
	public List<Group> OwnGroups { get; set; } = new();

	public async Task<IActionResult> OnGetAsync()
	{
		CurrentUser = await userManager.GetUserAsync(User);
		CreationDisabled = !(await siteSettings.GetAsync()).AllowUserRepoCreation;
		if (CurrentUser != null)
			OwnGroups = await db.Groups.Where(g => g.OwnerId == CurrentUser.Id).OrderBy(g => g.Name).ToListAsync();
		return Page();
	}

	public async Task<IActionResult> OnPostAsync()
	{
		if (!(await siteSettings.GetAsync()).AllowUserRepoCreation)
		{
			CurrentUser = await userManager.GetUserAsync(User);
			CreationDisabled = true;
			return Page();
		}

		var user = await userManager.GetUserAsync(User);
		if (user == null) return Challenge();
		CurrentUser = user;
		OwnGroups = await db.Groups.Where(g => g.OwnerId == user.Id).OrderBy(g => g.Name).ToListAsync();

		if (!Regex.IsMatch(Name, @"^[a-zA-Z0-9_\-\.]+$"))
		{
			ErrorMessage = L["error_invalid_repo_name"];
			return Page();
		}

		Group? group = null;
		if (GroupOwnerId.HasValue)
		{
			group = OwnGroups.FirstOrDefault(g => g.Id == GroupOwnerId.Value);
			if (group == null)
			{
				ErrorMessage = L["error_group_not_found"];
				return Page();
			}
		}

		try
		{
			if (group != null)
			{
				await repos.CreateForGroupAsync(group.Id, group.Name, Name, Description, IsPrivate);
				return RedirectToPage("/Repo/View", new { user = group.Name, repo = Name });
			}

			await repos.CreateAsync(user.Id, user.UserName!, Name, Description, IsPrivate);
			return RedirectToPage("/Repo/View", new { user = user.UserName, repo = Name });
		}
		catch (Exception ex)
		{
			ErrorMessage = L["error_create_repo"] + ex.Message;
			return Page();
		}
	}
}
