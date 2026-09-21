using GitServer.Data;
using GitServer.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace GitServer.Services;

/// <summary>Ends an account. "Deleting" a user anonymizes them: everything they made (repositories, issues,
/// comments, group memberships) stays visible, but under an anonymous-NNNN name, without password, email or
/// profile, and with the account locked, so nobody can sign in as them again.</summary>
public class AccountService(UserManager<AppUser> userManager, AppDbContext db, AccessTokenService tokens, IOptions<GitServerOptions> options)
{
	/// <summary>Returns null on success, otherwise an error message; on failure nothing has changed.</summary>
	public async Task<string?> DeleteAsync(AppUser user)
	{
		// A registration that was never completed has produced nothing; remove it for real.
		if (!user.EmailConfirmed)
		{
			var removed = await userManager.DeleteAsync(user);
			return removed.Succeeded ? null : Describe(removed);
		}

		var oldFolder = Path.Combine(options.Value.RepositoriesPath, user.UserName!);
		var newName = await NextAnonymousNameAsync();
		var newFolder = Path.Combine(options.Value.RepositoriesPath, newName);

		await using var transaction = await db.Database.BeginTransactionAsync();
		var failure = await AnonymizeAsync(user, newName);
		var moved = false;
		try
		{
			if (failure == null && Directory.Exists(oldFolder))
			{
				Directory.Move(oldFolder, newFolder);
				moved = true;
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			failure = ex.Message;
		}

		if (failure != null)
		{
			await transaction.RollbackAsync();
			if (moved) Directory.Move(newFolder, oldFolder);
			return failure;
		}

		await transaction.CommitAsync();
		return null;
	}

	private async Task<string> NextAnonymousNameAsync()
	{
		while (true)
		{
			var name = $"anonymous-{Random.Shared.Next(1000, 100000)}";
			if (await userManager.FindByNameAsync(name) == null &&
				!Directory.Exists(Path.Combine(options.Value.RepositoriesPath, name)))
				return name;
		}
	}

	private async Task<string?> AnonymizeAsync(AppUser user, string anonymousName)
	{
		var renamed = await userManager.SetUserNameAsync(user, anonymousName);
		if (!renamed.Succeeded) return Describe(renamed);
		var emailed = await userManager.SetEmailAsync(user, anonymousName + "@anonymous.invalid");
		if (!emailed.Succeeded) return Describe(emailed);

		if (await userManager.HasPasswordAsync(user))
		{
			var removed = await userManager.RemovePasswordAsync(user);
			if (!removed.Succeeded) return Describe(removed);
		}

		user.DisplayName = anonymousName;
		user.Bio = null;
		user.AvatarUrl = null;
		user.Country = null;
		user.CompanyName = null;
		user.PreferredLanguage = null;
		user.TimeZoneId = null;
		user.IsAdmin = false;
		user.EmailConfirmed = true;
		user.LockoutEnabled = true;
		user.LockoutEnd = DateTimeOffset.MaxValue;
		var updated = await userManager.UpdateAsync(user);
		if (!updated.Succeeded) return Describe(updated);

		await userManager.UpdateSecurityStampAsync(user);
		await tokens.RevokeAllAsync(user.Id);
		return null;
	}

	private static string Describe(IdentityResult result) => string.Join(" ", result.Errors.Select(e => e.Description));
}
