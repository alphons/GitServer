using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GitServer.Controllers.Api;

/// <summary>Repositories on a user's profile page: their own, plus (for the owner) those they reach through groups.</summary>
[ApiController]
[Route("api/users/{username}/repos")]
public class UserReposApiController(
	UserManager<AppUser> userManager, RepositoryService repos,
	IOptions<GitServerOptions> options, TimeZoneService tz) : ControllerBase
{
	/// <summary>Lists a user's repositories. Private repositories and group repositories are only included for the user themself.</summary>
	/// <param name="username">The profile's user name.</param>
	/// <param name="q">Optional search text.</param>
	/// <param name="p">Page of the user's own repositories, starting at 0.</param>
	/// <param name="gp">Page of the group repositories, starting at 0.</param>
	[HttpGet]
	[ProducesResponseType(StatusCodes.Status401Unauthorized)]
	[ProducesResponseType<UserReposResponse>(StatusCodes.Status200OK)]
	public async Task<ActionResult<UserReposResponse>> Get(string username, string? q, int p = 0, int gp = 0)
	{
		var profileUser = await userManager.FindByNameAsync(username);
		if (profileUser == null) return NotFound();

		p = Math.Max(p, 0);
		gp = Math.Max(gp, 0);
		var query = q ?? "";
		var pageSize = options.Value.ProfileRepoPageSize;
		var isOwner = userManager.GetUserId(User) == profileUser.Id;

		var fetched = await repos.GetUserReposAsync(profileUser.Id, includePrivate: isOwner, query: query,
			skip: p * pageSize, take: pageSize + 1);
		var own = fetched.Take(pageSize).ToList();
		var totalCount = await repos.GetUserRepoCountAsync(profileUser.Id, includePrivate: isOwner, query: query);

		var groupRepos = new List<Repository>();
		var groupTotal = 0;
		var groupHasNext = false;
		if (isOwner)
		{
			var fetchedGroup = await repos.GetAccessibleGroupReposAsync(profileUser.Id, query,
				skip: gp * pageSize, take: pageSize + 1);
			groupHasNext = fetchedGroup.Count > pageSize;
			groupRepos = fetchedGroup.Take(pageSize).ToList();
			groupTotal = await repos.GetAccessibleGroupRepoCountAsync(profileUser.Id, query);
		}

		return new UserReposResponse(
			query, isOwner, own.Count, totalCount, p, fetched.Count > pageSize,
			own.Select(r => Card(r, $"/{profileUser.UserName}/{r.Name}", r.Name, withCollaborators: true)).ToList(),
			groupTotal, gp, groupHasNext,
			groupRepos.Select(r => Card(r, $"/{r.GroupOwner!.Name}/{r.Name}", $"{r.GroupOwner!.Name} / {r.Name}", withCollaborators: false)).ToList());
	}

	private RepoCardDto Card(Repository r, string href, string displayName, bool withCollaborators) => new(
		displayName, href, r.IsPrivate, r.IsReadOnly, r.Description,
		tz.FormatDateTime(r.UpdatedAt), tz.FormatDateTime(r.CreatedAt),
		withCollaborators && r.IsPrivate
			? r.Accesses
				.Select(a => new CollaboratorDto(a.User?.UserName, a.User == null ? a.Group?.Name : null))
				.Where(a => a.UserName != null || a.GroupName != null)
				.ToList()
			: null);
}
