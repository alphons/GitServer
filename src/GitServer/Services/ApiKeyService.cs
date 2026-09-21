using System.Security.Cryptography;
using System.Text;
using GitServer.Data;
using GitServer.Models;
using Microsoft.EntityFrameworkCore;

namespace GitServer.Services;

public class ApiKeyService(AppDbContext db, SiteSettingsService siteSettings)
{
	public const string Prefix = "gsk_";
	public const string HeaderName = "X-Api-Key";

	private static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

	/// <summary>Creates a key valid for the site-wide lifetime and returns it in plain text; this is the only time it can be seen.</summary>
	public async Task<(ApiKey Entity, string Key)> CreateAsync(AppUser user, string name)
	{
		var key = Prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
			.Replace('+', '-').Replace('/', '_').TrimEnd('=');
		var lifetimeDays = (await siteSettings.GetAsync()).ApiKeyLifetimeDays;

		var entity = new ApiKey
		{
			UserId = user.Id,
			Name = name.Trim(),
			KeyPrefix = key[..8],
			KeyHash = Hash(key),
			ExpiresAt = DateTime.UtcNow.AddDays(lifetimeDays),
		};
		db.ApiKeys.Add(entity);
		await db.SaveChangesAsync();
		return (entity, key);
	}

	public Task<List<ApiKey>> ListAsync(string userId) =>
		db.ApiKeys.Where(k => k.UserId == userId).OrderByDescending(k => k.CreatedAt).ToListAsync();

	public async Task<bool> SetEnabledAsync(string userId, int id, bool enabled)
	{
		var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id && k.UserId == userId);
		if (key == null) return false;
		key.IsEnabled = enabled;
		await db.SaveChangesAsync();
		return true;
	}

	public async Task<bool> DeleteAsync(string userId, int id)
	{
		var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id && k.UserId == userId);
		if (key == null) return false;
		db.ApiKeys.Remove(key);
		await db.SaveChangesAsync();
		return true;
	}

	/// <summary>The user a live key belongs to, or null if the key is unknown, disabled, expired, or its owner
	/// can no longer sign in (disabled, locked out or not yet registered). Marks the key as used.</summary>
	public async Task<AppUser?> AuthenticateAsync(string key)
	{
		if (!key.StartsWith(Prefix, StringComparison.Ordinal)) return null;

		var hash = Hash(key);
		var stored = await db.ApiKeys.Include(k => k.User).FirstOrDefaultAsync(k => k.KeyHash == hash);
		if (stored == null || !stored.IsEnabled || stored.ExpiresAt <= DateTime.UtcNow) return null;

		var user = stored.User;
		if (!user.EmailConfirmed || (user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow)) return null;

		if (stored.LastUsedAt == null || stored.LastUsedAt < DateTime.UtcNow.AddMinutes(-1))
		{
			stored.LastUsedAt = DateTime.UtcNow;
			await db.SaveChangesAsync();
		}
		return user;
	}
}
