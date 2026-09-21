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
	IOptions<GitServerOptions> options, TimeZoneService tz, LocalizationService L) : ControllerBase
{
	[HttpGet]
	public async Task<IActionResult> Get(string username, string? q, int p = 0, int gp = 0)
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

		return Ok(new
		{
			query,
			isOwner,
			resultCount = own.Count,
			totalCount,
			page = p,
			hasNext = fetched.Count > pageSize,
			repos = own.Select(r => new
			{
				displayName = r.Name,
				href = $"/{profileUser.UserName}/{r.Name}",
				isPrivate = r.IsPrivate,
				isReadOnly = r.IsReadOnly,
				description = r.Description,
				collaborators = r.IsPrivate
					? r.Accesses
						.Select(a => new { userName = a.User?.UserName, groupName = a.User == null ? a.Group?.Name : null })
						.Where(a => a.userName != null || a.groupName != null)
						.ToList()
					: null,
				updated = Format(r.UpdatedAt),
				created = Format(r.CreatedAt),
			}),
			groupTotalCount = groupTotal,
			groupPage = gp,
			groupHasNext,
			groupRepos = groupRepos.Select(r => new
			{
				displayName = $"{r.GroupOwner!.Name} / {r.Name}",
				href = $"/{r.GroupOwner!.Name}/{r.Name}",
				isPrivate = r.IsPrivate,
				isReadOnly = r.IsReadOnly,
				description = r.Description,
				updated = Format(r.UpdatedAt),
				created = Format(r.CreatedAt),
			}),
		});
	}

	private string Format(DateTime utc) => tz.ToLocal(utc).ToString("d MMM yyyy HH:mm", L.CurrentCulture);
}
