using GitServer.Data;
using Microsoft.EntityFrameworkCore;

namespace GitServer.Services;

/// <summary>A user that matched an autocomplete search.</summary>
public record UserSearchResult(string? UserName, string DisplayName, string? Email);

/// <summary>The user look-up behind the "add a member" and "add a collaborator" autocompletes.</summary>
public class UserSearchService(AppDbContext db)
{
	public const int MinimumLength = 2;
	public const int MaxResults = 10;

	/// <summary>Users whose name, display name or e-mail address contains <paramref name="query"/>, ordered by user name.
	/// Empty when the query is shorter than <see cref="MinimumLength"/> characters. <paramref name="excludeUserId"/> (if any) is never returned.</summary>
	public async Task<IReadOnlyList<UserSearchResult>> SearchAsync(string? query, string? excludeUserId)
	{
		query = query?.Trim();
		if (string.IsNullOrEmpty(query) || query.Length < MinimumLength) return [];

		var lower = query.ToLower();
		return await db.Users
			.Where(u => u.Id != excludeUserId && (
				u.UserName!.ToLower().Contains(lower) ||
				u.Email!.ToLower().Contains(lower) ||
				u.DisplayName.ToLower().Contains(lower)))
			.OrderBy(u => u.UserName)
			.Take(MaxResults)
			.Select(u => new UserSearchResult(u.UserName, u.DisplayName, u.Email))
			.ToListAsync();
	}
}
