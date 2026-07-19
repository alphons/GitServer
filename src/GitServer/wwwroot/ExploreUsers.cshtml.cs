using GitServer.Data;
using GitServer.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GitServer.wwwroot;

public class ExploreUsersModel(AppDbContext db) : PageModel
{
	public string Query { get; set; } = "";
	public new int Page { get; set; }
	public List<AppUser> Users { get; set; } = [];

	public async Task OnGetAsync(string? q, int page = 0)
	{
		Query = q ?? "";
		Page = page;

		var query = db.Users.Where(u => u.EmailConfirmed);

		if (!string.IsNullOrWhiteSpace(Query))
		{
			var lower = Query.Trim().ToLower();
			query = query.Where(u =>
				u.UserName!.ToLower().Contains(lower) ||
				u.DisplayName.ToLower().Contains(lower) ||
				(u.Bio != null && u.Bio.ToLower().Contains(lower)));
		}

		Users = await query
			.OrderBy(u => u.UserName)
			.Skip(page * 20)
			.Take(20)
			.ToListAsync();
	}
}
