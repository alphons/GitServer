using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GitServer.Pages.Repo.Pulls;

/// <summary>Choose where to merge from and into (a GET form, so changing the source repository reloads its branches),
/// preview the commits and changes, then give the pull request a title.</summary>
[Authorize]
public class PullNewModel(
	RepositoryService repos, AccessPolicy access, PullRequestService pulls, UserManager<AppUser> userManager, LocalizationService L) : PageModel
{
	public string UserName { get; set; } = "";
	public string RepoName { get; set; } = "";
	public bool IsGroupOwner { get; set; }
	public Repository? Target { get; set; }
	public List<Repository> Sources { get; set; } = new();
	public List<string> SourceBranches { get; set; } = new();
	public List<string> TargetBranches { get; set; } = new();
	public PullRequestComparison? Preview { get; set; }
	public string? ErrorMessage { get; set; }

	[BindProperty(SupportsGet = true)] public int? Source { get; set; }
	[BindProperty(SupportsGet = true)] public string? Head { get; set; }
	[BindProperty(SupportsGet = true)] public string? Base { get; set; }
	[BindProperty] public string? Title { get; set; }
	[BindProperty] public string? Body { get; set; }

	public Repository? SourceRepo => Sources.FirstOrDefault(s => s.Id == Source) ?? Sources.FirstOrDefault();

	private async Task<IActionResult?> LoadAsync(string user, string repo)
	{
		Target = await repos.GetAsync(user, repo);
		if (Target == null) return NotFound();
		var userId = userManager.GetUserId(User);
		if (!await access.CanReadAsync(Target, userId)) return Forbid();
		UserName = Target.OwnerName;
		RepoName = Target.Name;
		IsGroupOwner = Target.GroupOwnerId != null;

		Sources = await pulls.SourceCandidatesAsync(Target, userId);
		Source = SourceRepo!.Id;
		TargetBranches = await pulls.BranchesAsync(Target);
		SourceBranches = await pulls.BranchesAsync(SourceRepo);
		Base ??= TargetBranches.Contains(Target.DefaultBranch) ? Target.DefaultBranch : TargetBranches.FirstOrDefault();
		Head ??= SourceBranches.FirstOrDefault(b => SourceRepo.Id != Target.Id || b != Base);
		return null;
	}

	public async Task<IActionResult> OnGetAsync(string user, string repo)
	{
		if (await LoadAsync(user, repo) is { } failure) return failure;
		if (Head != null && Base != null && !(SourceRepo!.Id == Target!.Id && Head == Base))
			Preview = await pulls.PreviewAsync(Target, Base, SourceRepo, Head);
		return Page();
	}

	public async Task<IActionResult> OnPostAsync(string user, string repo)
	{
		if (await LoadAsync(user, repo) is { } failure) return failure;

		var (pr, error) = await pulls.CreateAsync(Target!, (await userManager.GetUserAsync(User))!, SourceRepo!, Head ?? "", Base ?? "", Title, Body);
		if (error != null)
		{
			ErrorMessage = L[error.Key];
			if (Head != null && Base != null) Preview = await pulls.PreviewAsync(Target!, Base, SourceRepo!, Head);
			return Page();
		}
		return Redirect($"/{UserName}/{RepoName}/pulls/{pr!.Id}");
	}
}
