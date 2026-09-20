using System.Security.Cryptography;
using System.Text;
using GitServer.Data;
using GitServer.Models;
using Microsoft.EntityFrameworkCore;

namespace GitServer.Services;

public class AccessTokenService(AppDbContext db)
{
	public const string Prefix = "gsp_";

	public static bool LooksLikeToken(string? value) => value != null && value.StartsWith(Prefix, StringComparison.Ordinal);

	private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

	/// <summary>Creates a token and returns it in plain text; this is the only time it can be seen.</summary>
	public async Task<string> CreateAsync(AppUser user, string name, int? validDays)
	{
		var token = Prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
			.Replace('+', '-').Replace('/', '_').TrimEnd('=');
		db.AccessTokens.Add(new AccessToken
		{
			UserId = user.Id,
			Name = name.Trim(),
			TokenHash = Hash(token),
			ExpiresAt = validDays is > 0 ? DateTime.UtcNow.AddDays(validDays.Value) : null,
		});
		await db.SaveChangesAsync();
		return token;
	}

	public Task<List<AccessToken>> ListAsync(string userId) =>
		db.AccessTokens.Where(t => t.UserId == userId).OrderByDescending(t => t.CreatedAt).ToListAsync();

	public async Task<bool> RevokeAsync(string userId, int tokenId)
	{
		var token = await db.AccessTokens.FirstOrDefaultAsync(t => t.Id == tokenId && t.UserId == userId);
		if (token == null) return false;
		db.AccessTokens.Remove(token);
		await db.SaveChangesAsync();
		return true;
	}

	/// <summary>True if <paramref name="token"/> is a live token of <paramref name="userId"/>. Marks it as used.</summary>
	public async Task<bool> ValidateAsync(string userId, string token)
	{
		var hash = Hash(token);
		var stored = await db.AccessTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.UserId == userId);
		if (stored == null || (stored.ExpiresAt.HasValue && stored.ExpiresAt <= DateTime.UtcNow)) return false;

		if (stored.LastUsedAt == null || stored.LastUsedAt < DateTime.UtcNow.AddMinutes(-1))
		{
			stored.LastUsedAt = DateTime.UtcNow;
			await db.SaveChangesAsync();
		}
		return true;
	}

	public Task RevokeAllAsync(string userId) =>
		db.AccessTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync();
}
