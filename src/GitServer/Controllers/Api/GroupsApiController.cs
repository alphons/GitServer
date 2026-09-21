using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GitServer.Controllers.Api;

/// <summary>Group management data (owner only): the group's repositories and member look-up.</summary>
[ApiController]
[Authorize]
[Route("api/groups/{id:int}")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public class GroupsApiController(
	UserSearchService userSearch, AccessPolicy access, UserManager<AppUser> userManager,
	RepositoryService repos, IOptions<GitServerOptions> options) : ControllerBase
{
	/// <summary>Lists the group's repositories. Not found unless the caller owns the group.</summary>
	/// <param name="id">The group's id.</param>
	/// <param name="rp">Page, starting at 0.</param>
	[HttpGet("repos")]
	[ProducesResponseType<GroupReposResponse>(StatusCodes.Status200OK)]
	public async Task<ActionResult<GroupReposResponse>> Repos(int id, int rp = 0)
	{
		var group = await access.GetOwnedGroupAsync(id, userManager.GetUserId(User)!);
		if (group == null) return NotFound();

		rp = Math.Max(rp, 0);
		var pageSize = options.Value.ProfileRepoPageSize;
		var total = await repos.GetGroupRepoCountAsync(id);
		var items = await repos.GetGroupReposAsync(id, skip: rp * pageSize, take: pageSize);

		return new GroupReposResponse(
			rp, total > (rp + 1) * pageSize,
			items.Select(r => new RepoCardDto(
				$"{group.Name} / {r.Name}", $"/{group.Name}/{r.Name}", r.IsPrivate, r.IsReadOnly, r.Description,
				null, null, null)).ToList());
	}

	/// <summary>Finds users to add as a member (autocomplete). Needs at least 2 characters; the caller is never returned.</summary>
	/// <param name="id">The group's id.</param>
	/// <param name="q">Part of a user name, display name or e-mail address.</param>
	[HttpGet("user-search")]
	[ProducesResponseType<IReadOnlyList<UserSearchResult>>(StatusCodes.Status200OK)]
	public async Task<ActionResult<IReadOnlyList<UserSearchResult>>> SearchUsers(int id, string? q)
	{
		var userId = userManager.GetUserId(User)!;
		if (await access.GetOwnedGroupAsync(id, userId) == null) return NotFound();

		return Ok(await userSearch.SearchAsync(q, userId));
	}
}
