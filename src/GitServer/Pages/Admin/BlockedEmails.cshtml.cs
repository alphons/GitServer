using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GitServer.Pages.Admin;

public class BlockedEmailsModel(
	UserManager<AppUser> userManager,
	AppDbContext db,
	LocalizationService L) : PageModel
{
	public List<BlockedEmailPattern> Patterns { get; set; } = new();
	public string? Message { get; set; }
	public bool IsError { get; set; }

	[BindProperty] public string NewPattern { get; set; } = "";

	private async Task<bool> RequireAdminAsync()
	{
		var currentUser = await userManager.GetUserAsync(User);
		return currentUser != null && currentUser.IsAdmin;
	}

	private async Task ReloadAsync()
	{
		Patterns = await db.BlockedEmailPatterns.OrderBy(p => p.Pattern).ToListAsync();
	}

	public async Task<IActionResult> OnGetAsync()
	{
		if (!await RequireAdminAsync()) return Forbid();

		await ReloadAsync();
		return Page();
	}

	public async Task<IActionResult> OnPostAddAsync()
	{
		if (!await RequireAdminAsync()) return Forbid();

		var pattern = NewPattern.Trim();
		if (string.IsNullOrEmpty(pattern))
		{
			Message = L["error_pattern_required"];
			IsError = true;
			await ReloadAsync();
			return Page();
		}

		if (!await db.BlockedEmailPatterns.AnyAsync(p => p.Pattern == pattern))
		{
			db.BlockedEmailPatterns.Add(new BlockedEmailPattern { Pattern = pattern });
			await db.SaveChangesAsync();
		}

		Message = L.Format("success_pattern_added", pattern);
		return RedirectToPage();
	}

	public async Task<IActionResult> OnPostDeleteAsync(int id)
	{
		if (!await RequireAdminAsync()) return Forbid();

		var pattern = await db.BlockedEmailPatterns.FindAsync(id);
		if (pattern != null)
		{
			db.BlockedEmailPatterns.Remove(pattern);
			await db.SaveChangesAsync();
		}

		return RedirectToPage();
	}
}
