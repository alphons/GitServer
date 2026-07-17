using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.RegularExpressions;

namespace GitServer.wwwroot.Repo;

[Authorize]
public class NewModel(
	RepositoryService repos,
	UserManager<AppUser> userManager,
	LocalizationService L) : PageModel
{

	[BindProperty] public string Name { get; set; } = "";
	[BindProperty] public string? Description { get; set; }
	[BindProperty] public bool IsPrivate { get; set; }
	public string? ErrorMessage { get; set; }

	public void OnGet() { }

	public async Task<IActionResult> OnPostAsync()
	{
		if (!Regex.IsMatch(Name, @"^[a-zA-Z0-9_\-\.]+$"))
		{
			ErrorMessage = L["error_invalid_repo_name"];
			return Page();
		}

		var user = await userManager.GetUserAsync(User);
		if (user == null) return Challenge();

		try
		{
			var repo = await repos.CreateAsync(user.Id, user.UserName!, Name, Description, IsPrivate);
			return RedirectToPage("/Repo/View", new { user = user.UserName, repo = Name });
		}
		catch (Exception ex)
		{
			ErrorMessage = L["error_create_repo"] + ex.Message;
			return Page();
		}
	}
}
