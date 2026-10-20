using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace GitServer.Pages.Repo;

public partial class ViewModel(
	RepositoryService repos, AccessPolicy access,
	GitProcessService git,
	UserManager<AppUser> userManager,
	IOptions<GitServerOptions> options) : PageModel
{
	private readonly GitServerOptions _options = options.Value;

	public string UserName { get; set; } = "";
	public string RepoName { get; set; } = "";
	public Repository? Repo { get; set; }
	public bool IsEmpty { get; set; }
	public string CurrentBranch { get; set; } = "main";
	public string CurrentPath { get; set; } = "";
	public List<TreeEntry> Tree { get; set; } = new();
	public List<string> Branches { get; set; } = new();
	public string CloneUrl { get; set; } = "";
	public string DefaultBranch { get; set; } = "main";
	public string ReadmePath { get; set; } = "";
	/// <summary>Base names of the markdown files directly under the repo root (e.g. "README", "Test"),
	/// README first when present, then alphabetically.</summary>
	public List<string> MdBases { get; set; } = new();
	/// <summary>Language variants for each base name in <see cref="MdBases"/>.</summary>
	public Dictionary<string, List<MdVariant>> MdGroups { get; set; } = new(StringComparer.OrdinalIgnoreCase);
	public string SelectedMdBase { get; set; } = "";
	public int ForkCount { get; set; }
	/// <summary>Link the source of a fork only for viewers who may read it: a private source's name is not for everyone.</summary>
	public bool ShowForkSource { get; set; }

	public int TagCount { get; set; }
	public int CommitCount { get; set; }
	/// <summary>Newest commit touching the folder being viewed; drives the bar above the file list.</summary>
	public CommitInfo? LatestCommit { get; set; }
	/// <summary>Per entry path, the last commit that touched it.</summary>
	public Dictionary<string, CommitInfo> LastCommits { get; set; } = new();
	/// <summary>Author of <see cref="LatestCommit"/> followed by its "Co-Authored-By" people.</summary>
	public List<CommitPerson> CommitPeople { get; set; } = new();

	public record CommitPerson(string Name, string AvatarUrl, string? ProfileUser);

	public async Task<IActionResult> OnGetAsync(string user, string repo, string? branch, string? path, string? file)
	{
		UserName = user;
		RepoName = repo;

		Repo = await repos.GetAsync(user, repo);
		if (Repo == null) return NotFound();
		UserName = Repo.OwnerName;
		RepoName = Repo.Name;

		var userId = userManager.GetUserId(User);
		if (!await access.CanReadAsync(Repo, userId)) return Forbid();

		ForkCount = await repos.GetForkCountAsync(Repo.Id);
		ShowForkSource = Repo.ForkedFrom != null && await access.CanReadAsync(Repo.ForkedFrom, userId);

		var repoPath = repos.GetRepoPath(Repo.OwnerName, Repo.Name);
		CloneUrl = $"{Request.Scheme}://{Request.Host}{_options.NormalizedGitPathPrefix}/{Repo.OwnerName}/{Repo.Name}.git";

		IsEmpty = await git.IsEmpty(repoPath);
		if (IsEmpty) { DefaultBranch = Repo.DefaultBranch; return Page(); }

		DefaultBranch = await git.GetDefaultBranch(repoPath);
		Branches = await git.GetBranches(repoPath);
		CurrentBranch = branch ?? DefaultBranch;
		CurrentPath = path ?? "";

		Tree = await git.GetTree(repoPath, CurrentBranch, CurrentPath);
		TagCount = (await git.GetTags(repoPath)).Count;
		CommitCount = await git.GetCommitCount(repoPath, CurrentBranch);
		LatestCommit = await git.GetLastCommit(repoPath, CurrentBranch, CurrentPath);
		LastCommits = await git.GetLastCommits(repoPath, CurrentBranch, Tree);
		await LoadCommitPeopleAsync();

		if (string.IsNullOrEmpty(CurrentPath))
		{
			var baseNames = Tree
				.Where(e => e.Type == "blob")
				.Select(e => MarkdownVariants.Parse(e.Name)?.Base)
				.Where(b => b != null)
				.Distinct(StringComparer.OrdinalIgnoreCase);

			foreach (var baseName in baseNames)
				MdGroups[baseName!] = MarkdownVariants.FindVariants(Tree, baseName!);

			MdBases = MdGroups.Keys
				.OrderBy(b => b.Equals("README", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
				.ThenBy(b => b, StringComparer.OrdinalIgnoreCase)
				.ToList();

			if (MdBases.Count > 0)
			{
				// A link from a rendered markdown file to another one in the same folder (e.g. the
				// README's own "README.nl.md") asks for that exact file via ?file=; otherwise README wins.
				var requested = file == null ? null : MdGroups.Values
					.SelectMany(variants => variants)
					.FirstOrDefault(v => v.FileName.Equals(file, StringComparison.OrdinalIgnoreCase));

				var selectedBase = requested != null
					? MdGroups.First(kv => kv.Value.Contains(requested)).Key
					: MdBases[0];

				SelectedMdBase = selectedBase;
				ReadmePath = requested?.FileName ?? MdGroups[selectedBase][0].FileName;
			}
		}

		return Page();
	}

	private async Task LoadCommitPeopleAsync()
	{
		if (LatestCommit == null) return;

		var people = new List<(string Name, string Email)> { (LatestCommit.Author, LatestCommit.Email) };
		foreach (Match m in CoAuthorLine().Matches(LatestCommit.Body))
			if (!people.Any(p => p.Email.Equals(m.Groups[2].Value, StringComparison.OrdinalIgnoreCase)))
				people.Add((m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim()));

		var emails = people.Select(p => p.Email).ToList();
		var users = await userManager.Users
			.Where(u => emails.Contains(u.Email!))
			.ToDictionaryAsync(u => u.Email!, u => u, StringComparer.OrdinalIgnoreCase);

		CommitPeople = people.Select(p => users.TryGetValue(p.Email, out var u)
			? new CommitPerson(u.DisplayName, u.EffectiveAvatarUrl ?? GravatarService.GetUrl(p.Email), u.UserName)
			: new CommitPerson(p.Name, GravatarService.GetUrl(p.Email), null)).ToList();
	}

	[GeneratedRegex(@"^\s*Co-authored-by:\s*(.+?)\s*<([^>]+)>\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
	private static partial Regex CoAuthorLine();
}
