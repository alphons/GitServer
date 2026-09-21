using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GitServer.Pages.Admin;

public class UsersModel(UserManager<AppUser> userManager, AccountService accounts, AuditService audit, LocalizationService L, ReservedNames reserved) : PageModel
{
	public string? CurrentUserId { get; set; }
	public string? Message { get; set; }
	public string? ErrorMessage { get; set; }
	public string Filter { get; set; } = "";
	public new int Page { get; set; }

	// The user list itself is loaded by the page from GET /api/admin/users; here only the filter and page are kept.
	private void LoadUsers(string? filter, int page)
	{
		Filter = (filter ?? "").Trim();
		Page = Math.Max(page, 0);
	}

	public async Task<IActionResult> OnGetAsync(string? q, int p = 0)
	{
		var currentUser = await userManager.GetUserAsync(User);
		if (!AccessPolicy.IsSiteAdmin(currentUser)) return Forbid();

		CurrentUserId = currentUser.Id;
		LoadUsers(q, p);
		return Page();
	}

	public async Task<IActionResult> OnPostSaveAsync(
		string? userId, string? userName, string? displayName, string? email,
		bool isDisabled, bool isAdmin, string? newPassword, string? confirmPassword, string? q, int p = 0)
	{
		var currentUser = await userManager.GetUserAsync(User);
		if (!AccessPolicy.IsSiteAdmin(currentUser)) return Forbid();

		userName = (userName ?? "").Trim();
		email = (email ?? "").Trim();
		displayName = (displayName ?? "").Trim();

		if (string.IsNullOrEmpty(userId))
		{
			var result = await CreateUserAsync(userName, displayName, email, isDisabled, isAdmin, newPassword, confirmPassword);
			if (result != null) { ErrorMessage = result; }
		}
		else
		{
			var result = await UpdateUserAsync(currentUser, userId, userName, displayName, email, isDisabled, isAdmin, newPassword, confirmPassword);
			if (result != null) { ErrorMessage = result; }
		}

		CurrentUserId = currentUser.Id;
		LoadUsers(q, p);
		return Page();
	}

	private async Task<string?> CreateUserAsync(
		string userName, string displayName, string email,
		bool isDisabled, bool isAdmin, string? newPassword, string? confirmPassword)
	{
		if (string.IsNullOrEmpty(newPassword))
			return L["error_new_password_required"];
		if (newPassword != confirmPassword)
			return L["error_passwords_do_not_match"];

		if (await reserved.IsReservedAsync(userName))
			return L["error_name_reserved"];

		var existing = await userManager.FindByNameAsync(userName);
		if (existing != null)
			return L["error_username_taken"];

		var user = new AppUser
		{
			UserName = userName,
			Email = email,
			DisplayName = string.IsNullOrWhiteSpace(displayName) ? userName : displayName,
			EmailConfirmed = true,
			IsAdmin = isAdmin,
		};

		var createResult = await userManager.CreateAsync(user, newPassword);
		if (!createResult.Succeeded)
			return string.Join(" ", createResult.Errors.Select(e => e.Description));

		if (isDisabled)
		{
			await userManager.SetLockoutEnabledAsync(user, true);
			await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
		}

		Message = L.Format("admin_user_created", user.UserName!);
		await audit.WriteAsync("user.create", user.UserName, isAdmin ? "admin" : null);
		return null;
	}

	private async Task<string?> UpdateUserAsync(
		AppUser currentUser, string userId, string userName, string displayName, string email,
		bool isDisabled, bool isAdmin, string? newPassword, string? confirmPassword)
	{
		var target = await userManager.FindByIdAsync(userId);
		if (target == null) return L["error_invalid_or_expired_link"];

		var wasPending = !target.EmailConfirmed;
		if (wasPending && string.IsNullOrEmpty(newPassword))
			return L["admin_password_required_to_validate"];

		if (!string.IsNullOrEmpty(newPassword) && newPassword != confirmPassword)
			return L["error_passwords_do_not_match"];

		if (!string.Equals(target.UserName, userName, StringComparison.Ordinal))
		{
			if (await reserved.IsReservedAsync(userName))
				return L["error_name_reserved"];

			var existing = await userManager.FindByNameAsync(userName);
			if (existing != null && existing.Id != target.Id)
				return L["error_username_taken"];

			var usernameResult = await userManager.SetUserNameAsync(target, userName);
			if (!usernameResult.Succeeded)
				return string.Join(" ", usernameResult.Errors.Select(e => e.Description));
		}

		if (!string.Equals(target.Email, email, StringComparison.OrdinalIgnoreCase))
		{
			var emailResult = await userManager.SetEmailAsync(target, email);
			if (!emailResult.Succeeded)
				return string.Join(" ", emailResult.Errors.Select(e => e.Description));
		}

		target.DisplayName = string.IsNullOrWhiteSpace(displayName) ? target.UserName! : displayName;
		target.IsAdmin = isAdmin;
		if (wasPending) target.EmailConfirmed = true;

		if (target.Id != currentUser.Id)
		{
			if (isDisabled && !target.IsDisabled)
			{
				await userManager.SetLockoutEnabledAsync(target, true);
				await userManager.SetLockoutEndDateAsync(target, DateTimeOffset.MaxValue);
			}
			else if (!isDisabled && target.LockoutEnd.HasValue)
			{
				// Re-enables a disabled account and also lifts a temporary lockout after failed logins.
				await userManager.SetLockoutEndDateAsync(target, null);
				await userManager.ResetAccessFailedCountAsync(target);
			}
		}

		await userManager.UpdateAsync(target);

		if (!string.IsNullOrEmpty(newPassword))
		{
			if (await userManager.HasPasswordAsync(target))
				await userManager.RemovePasswordAsync(target);

			var passwordResult = await userManager.AddPasswordAsync(target, newPassword);
			if (!passwordResult.Succeeded)
				return string.Join(" ", passwordResult.Errors.Select(e => e.Description));
		}

		Message = L.Format(wasPending ? "admin_user_validated" : "admin_user_saved", target.UserName!);
		await audit.WriteAsync(wasPending ? "user.validate" : "user.update", target.UserName, string.IsNullOrEmpty(newPassword) ? null : "password changed");
		return null;
	}

	public async Task<IActionResult> OnPostDeleteAsync(string userId, string? q, int p = 0)
	{
		var currentUser = await userManager.GetUserAsync(User);
		if (!AccessPolicy.IsSiteAdmin(currentUser)) return Forbid();
		if (userId == currentUser.Id) return BadRequest(L["admin_cannot_delete_self"]);

		var target = await userManager.FindByIdAsync(userId);
		if (target == null) return NotFound();

		var label = target.EmailConfirmed ? target.UserName! : target.Email!;
		var failure = await accounts.DeleteAsync(target);
		if (failure == null)
		{
			Message = L.Format("admin_user_deleted", label);
			await audit.WriteAsync("user.delete", label);
		}
		else ErrorMessage = failure;

		CurrentUserId = currentUser.Id;
		LoadUsers(q, p);
		return Page();
	}
}
