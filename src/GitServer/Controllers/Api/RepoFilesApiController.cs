using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using GitServer.Extensions;

namespace GitServer.Controllers.Api;

/// <summary>Edits a text file of a repository on one of its branches. Everything here needs write access to the repository;
/// a repository the caller can't read answers 404.</summary>
[ApiController]
[Authorize]
[ApiAntiforgery]
[Route("api/repos/{user}/{repo}/files")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
public class RepoFilesApiController(FileEditService files, UserManager<AppUser> userManager, LocalizationService L) : ControllerBase
{
	private ObjectResult Refused(FileEditError error) => StatusCode(error.Status, new ErrorResponse(L[error.Key]));

	/// <summary>The text of a file on a branch, to start editing it.</summary>
	/// <param name="user">The repository's owner.</param>
	/// <param name="repo">The repository's name.</param>
	/// <param name="branch">The branch (not a tag or commit: only branches can be committed to).</param>
	/// <param name="path">The file's path in the repository.</param>
	[HttpGet]
	[ProducesResponseType<EditableFileResponse>(StatusCodes.Status200OK)]
	public async Task<ActionResult<EditableFileResponse>> Read(string user, string repo, string? branch, string? path)
	{
		var me = await userManager.GetUserAsync(User);
		if (me == null) return Unauthorized();
		var (result, error) = await files.ReadAsync(user, repo, branch, path, me);
		return error != null ? Refused(error) : result!;
	}

	/// <summary>The difference between the edited text and the file as it was when editing began.</summary>
	[HttpPost("diff")]
	[RequestSizeLimit(4_194_304)]
	[ProducesResponseType<FileDiffResponse>(StatusCodes.Status200OK)]
	public async Task<ActionResult<FileDiffResponse>> Diff(string user, string repo, FileDiffRequest request)
	{
		var me = await userManager.GetUserAsync(User);
		if (me == null) return Unauthorized();
		var (result, error) = await files.DiffAsync(user, repo, request, me);
		return error != null ? Refused(error) : result!;
	}

	/// <summary>Commits the edited file as a new commit on the branch. Answers 409 when the branch has moved since
	/// <c>baseSha</c>; the edit is then not saved and has to be redone on the new version.</summary>
	[HttpPost("commit")]
	[RequestSizeLimit(4_194_304)]
	[ProducesResponseType<FileCommitResponse>(StatusCodes.Status200OK)]
	[ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
	public async Task<ActionResult<FileCommitResponse>> Commit(string user, string repo, FileCommitRequest request)
	{
		var me = await userManager.GetUserAsync(User);
		if (me == null) return Unauthorized();
		var (result, error) = await files.CommitAsync(user, repo, request, me);
		return error != null ? Refused(error) : result!;
	}
}
