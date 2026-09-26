using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.Pages.Repo.Pulls;

public class PullDetailModel(
	RepositoryService repos, AccessPolicy access, PullRequestService pulls, UserManager<AppUser> userManager, LocalizationService L) : PageModel
{
	public string UserName { get; set; } = "";
	public string RepoName { get; set; } = "";
	public bool IsGroupOwner { get; set; }
	public Repository? Target { get; set; }
	public PullRequest? Pr { get; set; }
	public PullRequestComparison? Comparison { get; set; }
	public string Tab { get; set; } = "conversation";
	public bool CanMerge { get; set; }
	public bool CanClose { get; set; }
	public string? Message { get; set; }
	public bool IsError { get; set; }

	[BindProperty] public string? CommentBody { get; set; }

	private async Task<IActionResult?> LoadAsync(string user, string repo, int id)
	{
		Target = await repos.GetAsync(user, repo);
		if (Target == null) return NotFound();
		var userId = userManager.GetUserId(User);
		if (!await access.CanReadAsync(Target, userId)) return Forbid();
		UserName = Target.OwnerName;
		RepoName = Target.Name;
		IsGroupOwner = Target.GroupOwnerId != null;

		Pr = await pulls.GetAsync(Target, id);
		if (Pr == null) return NotFound();
		CanMerge = await pulls.CanMergeAsync(Target, userId);
		CanClose = await pulls.CanCloseAsync(Target, Pr, userId);
		return null;
	}

	private async Task<IActionResult> ShowAsync(string? tab = null)
	{
		Tab = tab is "commits" or "files" ? tab : "conversation";
		await pulls.RefreshAsync(Pr!, Target!);
		// A merged pull request is shown as it was merged: its commits against the parent the merge commit replaced.
		var head = Pr!.HeadRef;
		Comparison = Pr.State == PullRequestState.Merged && Pr.MergeCommitSha != null
			? await pulls.CompareAsync(Target!, Pr.MergeCommitSha + "^1", head, isBaseRev: true)
			: await pulls.CompareAsync(Target!, Pr.TargetBranch, head);
		return Page();
	}

	public async Task<IActionResult> OnGetAsync(string user, string repo, int id, string? tab)
	{
		if (await LoadAsync(user, repo, id) is { } failure) return failure;
		return await ShowAsync(tab);
	}

	private async Task<IActionResult> AfterAsync(PullRequestError? error)
	{
		if (error == null) return Redirect($"/{UserName}/{RepoName}/pulls/{Pr!.Id}");
		Message = L[error.Key];
		IsError = true;
		return await ShowAsync();
	}

	public async Task<IActionResult> OnPostCommentAsync(string user, string repo, int id)
	{
		if (await LoadAsync(user, repo, id) is { } failure) return failure;
		var me = await userManager.GetUserAsync(User);
		if (me == null) return Challenge();
		var (_, error) = await pulls.CommentAsync(Target!, Pr!, me, CommentBody);
		return await AfterAsync(error);
	}

	public async Task<IActionResult> OnPostMergeAsync(string user, string repo, int id, bool squash)
	{
		if (await LoadAsync(user, repo, id) is { } failure) return failure;
		var me = await userManager.GetUserAsync(User);
		if (me == null) return Challenge();
		return await AfterAsync(await pulls.MergeAsync(Target!, Pr!, me, squash));
	}

	public async Task<IActionResult> OnPostCloseAsync(string user, string repo, int id) => await SetClosedAsync(user, repo, id, true);

	public async Task<IActionResult> OnPostReopenAsync(string user, string repo, int id) => await SetClosedAsync(user, repo, id, false);

	private async Task<IActionResult> SetClosedAsync(string user, string repo, int id, bool close)
	{
		if (await LoadAsync(user, repo, id) is { } failure) return failure;
		var me = await userManager.GetUserAsync(User);
		if (me == null) return Challenge();
		return await AfterAsync(await pulls.SetClosedAsync(Target!, Pr!, me, close));
	}
}
