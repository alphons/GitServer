using GitServer.Data;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GitServer.Pages;

public class ExploreUsersModel(AppDbContext db, IOptions<GitServerOptions> options) : PageModel
{
	public string Query { get; set; } = "";
	public new int Page { get; set; }
	public int PageSize { get; } = options.Value.ExploreUserPageSize;
	public List<AppUser> Users { get; set; } = [];
	public bool HasNextPage { get; set; }

	public async Task OnGetAsync(string? q, int p = 0)
	{
		Query = q ?? "";
		Page = p;

		var query = db.Users.Where(u => u.EmailConfirmed);

		if (!string.IsNullOrWhiteSpace(Query))
		{
			var lower = Query.Trim().ToLower();
			query = query.Where(u =>
				u.UserName!.ToLower().Contains(lower) ||
				u.DisplayName.ToLower().Contains(lower) ||
				(u.Bio != null && u.Bio.ToLower().Contains(lower)) ||
				(u.CompanyName != null && u.CompanyName.ToLower().Contains(lower)) ||
				(u.Country != null && u.Country.ToLower().Contains(lower)));
		}

		var fetched = await query
			.OrderBy(u => u.UserName)
			.Skip(p * PageSize)
			.Take(PageSize + 1)
			.ToListAsync();

		HasNextPage = fetched.Count > PageSize;
		Users = fetched.Take(PageSize).ToList();
	}
}
