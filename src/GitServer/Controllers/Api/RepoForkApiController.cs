using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GitServer.Controllers.Api;

[ApiController]
[Authorize]
[Route("api/repos/{user}/{repo}/fork")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public class RepoForkApiController(
	RepositoryService repos, AccessPolicy access, ForkService forks, UserManager<AppUser> userManager) : ControllerBase
{
	/// <summary>Forks a repository: a full copy of its branches and tags, private exactly when the source is.
	/// Anyone who can read the repository may fork it, into their own namespace or a group where they have at least the write role.</summary>
	/// <param name="user">The source repository's owner.</param>
	/// <param name="repo">The source repository's name.</param>
	/// <param name="request">Optional target name and group.</param>
	[HttpPost]
	[ProducesResponseType<ForkResponse>(StatusCodes.Status201Created)]
	[ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
	[ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
	[ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
	[ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
	public async Task<ActionResult<ForkResponse>> Fork(string user, string repo, ForkRequest? request)
	{
		var me = await userManager.GetUserAsync(User);
		if (me == null) return Unauthorized();

		// A repository the caller can't read looks the same as one that doesn't exist.
		var source = await repos.GetAsync(user, repo);
		if (source == null || !await access.CanReadAsync(source, me.Id)) return NotFound();

		int? groupId = null;
		if (!string.IsNullOrWhiteSpace(request?.Group))
		{
			var group = (await access.GetGroupsForRepoCreationAsync(me.Id))
				.FirstOrDefault(g => string.Equals(g.Name, request.Group.Trim(), StringComparison.OrdinalIgnoreCase));
			groupId = group?.Id ?? -1;   // unknown, or no write role there: ForkService reports it as "group not found"
		}

		var result = await forks.ForkAsync(source, me, groupId, request?.Name);
		var error = new ErrorResponse(result.Message ?? "");
		return result.Error switch
		{
			ForkError.None => Created($"/{result.Fork!.OwnerName}/{result.Fork.Name}",
				new ForkResponse(result.Fork.OwnerName, result.Fork.Name, $"/{result.Fork.OwnerName}/{result.Fork.Name}", result.Fork.IsPrivate)),
			ForkError.CreationDisabled => StatusCode(StatusCodes.Status403Forbidden, error),
			ForkError.NotReadable => NotFound(error),
			ForkError.GroupNotFound => NotFound(error),
			ForkError.NameTaken => Conflict(error),
			ForkError.InvalidName => BadRequest(error),
			_ => StatusCode(StatusCodes.Status500InternalServerError, error),
		};
	}
}
