using System.Text.RegularExpressions;
using GitServer.Models;
using GitServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace GitServer.Pages.Repo;

/// <summary>A README variant available in the current folder, e.g. README.nl.md.</summary>
public record ReadmeOption(string FileName, string? Lang, string Flag);

public class ViewModel(
	RepositoryService repos, AccessPolicy access,
	GitProcessService git,
	UserManager<AppUser> userManager,
	IOptions<GitServerOptions> options) : PageModel
{
	private readonly GitServerOptions _options = options.Value;

	private static readonly Regex ReadmeLangPattern = new(@"^README\.([A-Za-z]{2,3})\.md$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Dictionary<string, string> LangToFlag = new(StringComparer.OrdinalIgnoreCase)
	{
		["nl"] = "🇳🇱", ["en"] = "🇬🇧", ["de"] = "🇩🇪", ["fr"] = "🇫🇷", ["es"] = "🇪🇸",
		["it"] = "🇮🇹", ["pt"] = "🇵🇹", ["pl"] = "🇵🇱", ["ru"] = "🇷🇺", ["tr"] = "🇹🇷",
		["ja"] = "🇯🇵", ["zh"] = "🇨🇳", ["ko"] = "🇰🇷", ["ar"] = "🇸🇦", ["sv"] = "🇸🇪",
		["da"] = "🇩🇰", ["no"] = "🇳🇴", ["fi"] = "🇫🇮", ["uk"] = "🇺🇦", ["cs"] = "🇨🇿",
		["ro"] = "🇷🇴", ["hu"] = "🇭🇺", ["el"] = "🇬🇷", ["he"] = "🇮🇱", ["hi"] = "🇮🇳",
	};

	private static string FlagFor(string? lang) =>
		lang != null && LangToFlag.TryGetValue(lang, out var flag) ? flag : "🏳️";

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
	public List<ReadmeOption> ReadmeOptions { get; set; } = new();
	public int ForkCount { get; set; }
	/// <summary>Link the source of a fork only for viewers who may read it: a private source's name is not for everyone.</summary>
	public bool ShowForkSource { get; set; }

	public async Task<IActionResult> OnGetAsync(string user, string repo, string? branch, string? path)
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

		if (string.IsNullOrEmpty(CurrentPath))
		{
			var readmes = Tree.Where(e => e.Type == "blob" &&
				e.Name.StartsWith("README", StringComparison.OrdinalIgnoreCase));

			ReadmeOptions = readmes
				.Select(e =>
				{
					var match = ReadmeLangPattern.Match(e.Name);
					var lang = match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
					return new ReadmeOption(e.Name, lang, FlagFor(lang));
				})
				.OrderBy(o => o.Lang != null)
				.ThenBy(o => o.Lang, StringComparer.OrdinalIgnoreCase)
				.ToList();

			if (ReadmeOptions.Count > 0)
				ReadmePath = ReadmeOptions[0].FileName;
		}

		return Page();
	}
}
