using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GitServer.Controllers.Api;

/// <summary>A repository's pull requests. Reading needs read access to the repository (a private one the caller can't read
/// answers 404); opening and commenting need a signed-in caller; merging needs write access.</summary>
[ApiController]
[Route("api/repos/{user}/{repo}/pulls")]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public class RepoPullRequestsApiController(
	RepositoryService repos, AccessPolicy access, PullRequestService pulls,
	UserManager<AppUser> userManager, TimeZoneService tz, LocalizationService L) : ControllerBase
{
	private async Task<Repository?> ReadableRepoAsync(string user, string repo)
	{
		var repoObj = await repos.GetAsync(user, repo);
		return repoObj != null && await access.CanReadAsync(repoObj, userManager.GetUserId(User)) ? repoObj : null;
	}

	private ObjectResult Refused(PullRequestError error) => StatusCode(error.Status, new ErrorResponse(L[error.Key]));

	/// <summary>Lists the open pull requests, or the closed and merged ones with <c>state=closed</c>.</summary>
	[HttpGet]
	[ProducesResponseType<IReadOnlyList<PullRequestDto>>(StatusCodes.Status200OK)]
	public async Task<ActionResult<IReadOnlyList<PullRequestDto>>> List(string user, string repo, string? state)
	{
		var target = await ReadableRepoAsync(user, repo);
		if (target == null) return NotFound();
		return (await pulls.ListAsync(target, open: state != "closed")).Select(p => ToDto(target, p)).ToList();
	}

	/// <summary>One pull request with its commits ("shortsha subject") and changed files.</summary>
	[HttpGet("{id:int}")]
	[ProducesResponseType<PullRequestDetailDto>(StatusCodes.Status200OK)]
	public async Task<ActionResult<PullRequestDetailDto>> Get(string user, string repo, int id)
	{
		var target = await ReadableRepoAsync(user, repo);
		var pr = target == null ? null : await pulls.GetAsync(target, id);
		if (pr == null) return NotFound();

		await pulls.RefreshAsync(pr, target!);
		var cmp = pr.State == PullRequestState.Merged && pr.MergeCommitSha != null
			? await pulls.CompareAsync(target!, pr.MergeCommitSha + "^1", pr.HeadRef, isBaseRev: true)
			: await pulls.CompareAsync(target!, pr.TargetBranch, pr.HeadRef);
		return new PullRequestDetailDto(ToDto(target!, pr), pr.State == PullRequestState.Open && cmp.Mergeable,
			cmp.Commits.Select(c => $"{c.ShortSha} {c.Message}").ToList(), cmp.Files);
	}

	/// <summary>Opens a pull request.</summary>
	[HttpPost]
	[Authorize]
	[ProducesResponseType<PullRequestDto>(StatusCodes.Status201Created)]
	[ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
	public async Task<ActionResult<PullRequestDto>> Create(string user, string repo, CreatePullRequestRequest request)
	{
		var me = await userManager.GetUserAsync(User);
		var target = await ReadableRepoAsync(user, repo);
		if (me == null || target == null) return NotFound();

		var source = target;
		if (!string.IsNullOrWhiteSpace(request.HeadRepo))
		{
			var parts = request.HeadRepo.Split('/', 2);
			source = parts.Length == 2 ? await repos.GetAsync(parts[0], parts[1]) : null;
			if (source == null) return NotFound(new ErrorResponse(L["error_repo_not_found"]));
		}

		var (pr, error) = await pulls.CreateAsync(target, me, source, request.Head ?? "", request.Base ?? target.DefaultBranch, request.Title, request.Body);
		if (error != null) return Refused(error);
		pr = await pulls.GetAsync(target, pr!.Id);
		return Created($"/{target.OwnerName}/{target.Name}/pulls/{pr!.Id}", ToDto(target, pr));
	}

	/// <summary>Adds a comment.</summary>
	[HttpPost("{id:int}/comments")]
	[Authorize]
	[ProducesResponseType<PullRequestCommentDto>(StatusCodes.Status201Created)]
	[ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
	public async Task<ActionResult<PullRequestCommentDto>> Comment(string user, string repo, int id, PullRequestCommentRequest request)
	{
		var me = await userManager.GetUserAsync(User);
		var target = await ReadableRepoAsync(user, repo);
		var pr = target == null ? null : await pulls.GetAsync(target, id);
		if (me == null || pr == null) return NotFound();

		var (comment, error) = await pulls.CommentAsync(target!, pr, me, request.Body);
		if (error != null) return Refused(error);
		return Created($"/{target!.OwnerName}/{target.Name}/pulls/{pr.Id}",
			new PullRequestCommentDto(comment!.Id, me.UserName!, comment.Body, tz.FormatDateTime(comment.CreatedAt) ?? ""));
	}

	/// <summary>Merges the pull request: a merge commit, or one squashed commit with <c>method=squash</c>.
	/// 409 when it conflicts, is no longer open, or the target branch moved at that very moment.</summary>
	[HttpPost("{id:int}/merge")]
	[Authorize]
	[ProducesResponseType<PullRequestDto>(StatusCodes.Status200OK)]
	[ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
	[ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
	public async Task<ActionResult<PullRequestDto>> Merge(string user, string repo, int id, MergePullRequestRequest? request)
	{
		var me = await userManager.GetUserAsync(User);
		var target = await ReadableRepoAsync(user, repo);
		var pr = target == null ? null : await pulls.GetAsync(target, id);
		if (me == null || pr == null) return NotFound();

		if (await pulls.MergeAsync(target!, pr, me, squash: request?.Method == "squash") is { } error) return Refused(error);
		return ToDto(target!, (await pulls.GetAsync(target!, id))!);
	}

	/// <summary>Closes the pull request without merging.</summary>
	[HttpPost("{id:int}/close")]
	[Authorize]
	[ProducesResponseType<PullRequestDto>(StatusCodes.Status200OK)]
	public Task<ActionResult<PullRequestDto>> Close(string user, string repo, int id) => SetClosedAsync(user, repo, id, true);

	/// <summary>Reopens a closed (not merged) pull request.</summary>
	[HttpPost("{id:int}/reopen")]
	[Authorize]
	[ProducesResponseType<PullRequestDto>(StatusCodes.Status200OK)]
	public Task<ActionResult<PullRequestDto>> Reopen(string user, string repo, int id) => SetClosedAsync(user, repo, id, false);

	private async Task<ActionResult<PullRequestDto>> SetClosedAsync(string user, string repo, int id, bool close)
	{
		var me = await userManager.GetUserAsync(User);
		var target = await ReadableRepoAsync(user, repo);
		var pr = target == null ? null : await pulls.GetAsync(target, id);
		if (me == null || pr == null) return NotFound();

		if (await pulls.SetClosedAsync(target!, pr, me, close) is { } error) return Refused(error);
		return ToDto(target!, pr);
	}

	private PullRequestDto ToDto(Repository target, PullRequest p) => new(
		p.Id, p.Title, p.Body, p.State.ToString().ToLowerInvariant(), p.Author.UserName!,
		p.SourceRepository == null ? p.SourceDisplayName : $"{p.SourceRepository.OwnerName}/{p.SourceRepository.Name}",
		p.SourceBranch, p.TargetBranch, tz.FormatDateTime(p.CreatedAt) ?? "", p.MergedBy?.UserName, p.MergeCommitSha,
		p.Comments.Count, $"/{target.OwnerName}/{target.Name}/pulls/{p.Id}");
}
