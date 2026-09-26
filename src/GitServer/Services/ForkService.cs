using GitServer.Models;

namespace GitServer.Services;

public enum ForkError { None, CreationDisabled, NotReadable, InvalidName, GroupNotFound, NameTaken, Failed }

/// <summary>Outcome of <see cref="ForkService.ForkAsync"/>: the new fork, or why there is none. <see cref="Message"/> is localized.</summary>
public record ForkResult(Repository? Fork, ForkError Error, string? Message)
{
	public bool Succeeded => Error == ForkError.None;
}

/// <summary>The rules for forking, shared by the fork page and the API: anyone who can read a repository may fork it,
/// into their own namespace or a group where they may create repositories, under a name that is free there.</summary>
public class ForkService(
	RepositoryService repos, AccessPolicy access, SiteSettingsService siteSettings, AuditService audit, LocalizationService L)
{
	/// <param name="groupId">The group to fork into, or null for the user's own namespace.</param>
	/// <param name="name">The fork's name; null or empty keeps the source's name.</param>
	public async Task<ForkResult> ForkAsync(Repository source, AppUser user, int? groupId, string? name)
	{
		if (!(await siteSettings.GetAsync()).AllowUserRepoCreation)
			return Fail(ForkError.CreationDisabled, "new_repo_creation_disabled");
		if (!await access.CanReadAsync(source, user.Id))
			return Fail(ForkError.NotReadable, "error_repo_not_found");

		name = string.IsNullOrWhiteSpace(name) ? source.Name : name.Trim();
		if (!RepositoryService.IsValidName(name))
			return Fail(ForkError.InvalidName, "error_invalid_repo_name");

		Group? group = null;
		if (groupId.HasValue)
		{
			group = (await access.GetGroupsForRepoCreationAsync(user.Id)).FirstOrDefault(g => g.Id == groupId.Value);
			if (group == null) return Fail(ForkError.GroupNotFound, "error_group_not_found");
		}

		var ownerName = group?.Name ?? user.UserName!;
		if (await repos.GetAsync(ownerName, name) != null)
			return Fail(ForkError.NameTaken, "error_repo_name_taken");

		Repository fork;
		try
		{
			fork = await repos.ForkAsync(source, user, group, name);
		}
		catch (Exception ex)
		{
			return new(null, ForkError.Failed, L["error_create_repo"] + ex.Message);
		}

		await audit.WriteAsync("repo.fork", $"{ownerName}/{fork.Name}", $"from {source.OwnerName}/{source.Name}");
		return new(fork, ForkError.None, null);
	}

	private ForkResult Fail(ForkError error, string key) => new(null, error, L[key]);
}
