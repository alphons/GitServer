using GitServer.Data;
using GitServer.Models;
using Microsoft.EntityFrameworkCore;

namespace GitServer.Services;

/// <summary>Why a pull request action was refused: a localization key, and the HTTP status the API answers with.</summary>
public record PullRequestError(string Key, int Status);

/// <summary>What a pull request would bring in: its commits (oldest first), the diff against the merge base, and whether the
/// server can merge it without conflicts.</summary>
public record PullRequestComparison(List<CommitInfo> Commits, string Diff, List<string> Files, bool Mergeable, string? BaseSha, string? HeadSha);

/// <summary>The rules and git work behind pull requests, shared by the pages and the API.
/// Opening needs read access to the target and the source; merging needs write access to the target (so never on a
/// read-only repository); closing and reopening are for the author and anyone who may write.</summary>
public class PullRequestService(
	AppDbContext db, RepositoryService repos, GitProcessService git, AccessPolicy access, WebhookService webhooks, AuditService audit)
{
	public const int MaxCommitsShown = 250;

	private static PullRequestError Error(string key, int status = 400) => new(key, status);

	/// <summary>The repositories a pull request into <paramref name="target"/> can come from: the target itself and its direct
	/// forks, as far as the user can read them.</summary>
	public async Task<List<Repository>> SourceCandidatesAsync(Repository target, string? userId)
	{
		var forks = await db.Repositories
			.Include(r => r.Owner).Include(r => r.GroupOwner)
			.Where(r => r.ForkedFromId == target.Id)
			.ToListAsync();
		var result = new List<Repository> { target };
		foreach (var fork in forks.OrderBy(f => f.OwnerName))
			if (await access.CanReadAsync(fork, userId)) result.Add(fork);
		return result;
	}

	public async Task<List<string>> BranchesAsync(Repository repo)
	{
		try { return await git.GetBranches(repos.GetRepoPath(repo.OwnerName, repo.Name)); }
		catch (RepositoryDataMissingException) { return []; }
	}

	/// <summary>Compares <paramref name="head"/> (a commit or ref inside the target repository) with the target branch, or with
	/// any revision when <paramref name="isBaseRev"/> is set (a merged pull request compares with the state before its merge).</summary>
	public async Task<PullRequestComparison> CompareAsync(Repository target, string targetBranch, string head, bool isBaseRev = false)
	{
		var path = repos.GetRepoPath(target.OwnerName, target.Name);
		var baseSha = await git.ResolveCommit(path, isBaseRev ? targetBranch : $"refs/heads/{targetBranch}");
		var headSha = await git.ResolveCommit(path, head);
		if (baseSha == null || headSha == null) return new([], "", [], false, baseSha, headSha);

		var mergeBase = await git.MergeBase(path, baseSha, headSha);
		var commits = await git.GetCommitsBetween(path, mergeBase ?? baseSha, headSha, MaxCommitsShown);
		var (diff, files) = await git.GetDiffBetween(path, mergeBase ?? baseSha, headSha);
		var mergeable = await git.MergeTree(path, baseSha, headSha) != null;
		return new(commits, diff, files, mergeable, baseSha, headSha);
	}

	/// <summary>Compares a branch that is not a pull request yet, for the "new pull request" preview. The source branch is
	/// fetched into a scratch ref of the target first when it lives in a fork.</summary>
	public async Task<PullRequestComparison?> PreviewAsync(Repository target, string targetBranch, Repository source, string sourceBranch)
	{
		if (!GitProcessService.IsValidBranchName(sourceBranch) || !GitProcessService.IsValidBranchName(targetBranch)) return null;
		var targetPath = repos.GetRepoPath(target.OwnerName, target.Name);
		string? head = source.Id == target.Id
			? await git.ResolveCommit(targetPath, $"refs/heads/{sourceBranch}")
			: await git.FetchBranchAs(targetPath, repos.GetRepoPath(source.OwnerName, source.Name), sourceBranch, "refs/pull/preview");
		return head == null ? null : await CompareAsync(target, targetBranch, head);
	}

	public async Task<(PullRequest? Pr, PullRequestError? Error)> CreateAsync(
		Repository target, AppUser author, Repository source, string sourceBranch, string targetBranch, string? title, string? body)
	{
		if (!await access.CanReadAsync(target, author.Id) || !await access.CanReadAsync(source, author.Id)) return (null, Error("error_repo_not_found", 404));
		if (source.Id != target.Id && source.ForkedFromId != target.Id) return (null, Error("pr_error_source"));
		if (!GitProcessService.IsValidBranchName(sourceBranch) || !GitProcessService.IsValidBranchName(targetBranch)) return (null, Error("pr_error_branch"));
		if (source.Id == target.Id && sourceBranch == targetBranch) return (null, Error("pr_error_same_branch"));
		if (string.IsNullOrWhiteSpace(title)) return (null, Error("pr_error_title"));

		var preview = await PreviewAsync(target, targetBranch, source, sourceBranch);
		if (preview?.BaseSha == null || preview.HeadSha == null) return (null, Error("pr_error_branch"));
		if (preview.Commits.Count == 0) return (null, Error("pr_error_nothing"));

		var pr = new PullRequest
		{
			RepositoryId = target.Id,
			TargetBranch = targetBranch,
			SourceRepositoryId = source.Id,
			SourceBranch = sourceBranch,
			SourceDisplayName = $"{source.OwnerName}/{source.Name}",
			AuthorId = author.Id,
			Title = title.Trim(),
			Body = body ?? "",
		};
		db.PullRequests.Add(pr);
		await db.SaveChangesAsync();

		var targetPath = repos.GetRepoPath(target.OwnerName, target.Name);
		if (await git.FetchBranchAs(targetPath, repos.GetRepoPath(source.OwnerName, source.Name), sourceBranch, pr.HeadRef) == null)
		{
			db.PullRequests.Remove(pr);
			await db.SaveChangesAsync();
			return (null, Error("pr_error_branch"));
		}

		await webhooks.PullRequestAsync(target, pr, "opened", author);
		return (pr, null);
	}

	/// <summary>Picks up new pushes to the source branch of an open pull request. When the fork or the branch is gone the
	/// last copy (refs/pull/N/head) simply stays.</summary>
	public async Task RefreshAsync(PullRequest pr, Repository target)
	{
		if (pr.State != PullRequestState.Open || pr.SourceRepositoryId == null) return;
		var source = pr.SourceRepositoryId == target.Id ? target : await db.Repositories.Include(r => r.Owner).Include(r => r.GroupOwner).FirstOrDefaultAsync(r => r.Id == pr.SourceRepositoryId);
		if (source == null) return;
		try
		{
			await git.FetchBranchAs(repos.GetRepoPath(target.OwnerName, target.Name), repos.GetRepoPath(source.OwnerName, source.Name), pr.SourceBranch, pr.HeadRef);
		}
		catch (RepositoryDataMissingException) { /* the source's folder is gone; keep the last copy */ }
	}

	public async Task<bool> CanMergeAsync(Repository target, string? userId) => await access.CanWriteAsync(target, userId);

	public async Task<bool> CanCloseAsync(Repository target, PullRequest pr, string? userId) =>
		userId != null && (pr.AuthorId == userId || await access.CanWriteAsync(target, userId));

	/// <summary>Merges on the server, without a work tree. <paramref name="squash"/>: one new commit on the target branch with all
	/// the changes; otherwise a merge commit that keeps the pull request's commits.</summary>
	public async Task<PullRequestError?> MergeAsync(Repository target, PullRequest pr, AppUser user, bool squash)
	{
		if (!await CanMergeAsync(target, user.Id)) return Error("pr_error_no_permission", 403);
		if (pr.State != PullRequestState.Open) return Error("pr_error_not_open", 409);

		await RefreshAsync(pr, target);
		var path = repos.GetRepoPath(target.OwnerName, target.Name);
		var baseSha = await git.ResolveCommit(path, $"refs/heads/{pr.TargetBranch}");
		var headSha = await git.ResolveCommit(path, pr.HeadRef);
		if (baseSha == null || headSha == null) return Error("pr_error_branch", 409);

		var tree = await git.MergeTree(path, baseSha, headSha);
		if (tree == null) return Error("pr_error_conflict", 409);

		var name = string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName! : user.DisplayName;
		var email = user.Email ?? $"{user.UserName}@users.noreply";
		var message = squash
			? $"{pr.Title} (#{pr.Id})\n\n{pr.Body}".TrimEnd() + "\n"
			: $"Merge pull request #{pr.Id} from {pr.SourceDisplayName}:{pr.SourceBranch}\n\n{pr.Title}\n";
		var parents = squash ? new[] { baseSha } : new[] { baseSha, headSha };
		var commit = await git.CommitTree(path, tree, parents, message, name, email);
		if (commit == null || !await git.UpdateRef(path, $"refs/heads/{pr.TargetBranch}", commit, baseSha))
			return Error("pr_error_merge_failed", 409);   // e.g. someone pushed to the target branch at that very moment

		pr.State = PullRequestState.Merged;
		pr.MergedById = user.Id;
		pr.MergeCommitSha = commit;
		pr.ClosedAt = pr.UpdatedAt = DateTime.UtcNow;
		target.UpdatedAt = DateTime.UtcNow;
		await db.SaveChangesAsync();

		await audit.WriteAsync("pr.merge", $"{target.OwnerName}/{target.Name}#{pr.Id}", squash ? "squash" : "merge commit");
		await webhooks.PullRequestAsync(target, pr, "closed", user);
		// A merge moves the target branch like a push does, so push listeners (CI) hear about it too.
		var refName = $"refs/heads/{pr.TargetBranch}";
		await webhooks.PushAsync(target, git, path, new Dictionary<string, string> { [refName] = baseSha }, new Dictionary<string, string> { [refName] = commit }, user);
		return null;
	}

	public async Task<PullRequestError?> SetClosedAsync(Repository target, PullRequest pr, AppUser user, bool close)
	{
		if (!await CanCloseAsync(target, pr, user.Id)) return Error("pr_error_no_permission", 403);
		if (pr.State == PullRequestState.Merged || (pr.State == PullRequestState.Closed) == close) return Error("pr_error_not_open", 409);

		pr.State = close ? PullRequestState.Closed : PullRequestState.Open;
		pr.ClosedAt = close ? DateTime.UtcNow : null;
		pr.UpdatedAt = DateTime.UtcNow;
		await db.SaveChangesAsync();
		if (!close) await RefreshAsync(pr, target);
		await webhooks.PullRequestAsync(target, pr, close ? "closed" : "reopened", user);
		return null;
	}

	public async Task<(PullRequestComment? Comment, PullRequestError? Error)> CommentAsync(Repository target, PullRequest pr, AppUser user, string? body)
	{
		if (!await access.CanReadAsync(target, user.Id)) return (null, Error("error_repo_not_found", 404));
		if (string.IsNullOrWhiteSpace(body)) return (null, Error("pr_error_comment"));

		var comment = new PullRequestComment { PullRequestId = pr.Id, AuthorId = user.Id, Body = body };
		db.PullRequestComments.Add(comment);
		pr.UpdatedAt = DateTime.UtcNow;
		await db.SaveChangesAsync();
		return (comment, null);
	}

	/// <summary>The pull request with its author, merger, source and comments (with their authors).</summary>
	public async Task<PullRequest?> GetAsync(Repository target, int id) =>
		await db.PullRequests
			.Include(p => p.Author)
			.Include(p => p.MergedBy)
			.Include(p => p.SourceRepository).ThenInclude(r => r!.Owner)
			.Include(p => p.SourceRepository).ThenInclude(r => r!.GroupOwner)
			.Include(p => p.Comments).ThenInclude(c => c.Author)
			.FirstOrDefaultAsync(p => p.RepositoryId == target.Id && p.Id == id);

	/// <summary>Open pull requests when <paramref name="open"/> is true, otherwise the closed and merged ones.</summary>
	public async Task<List<PullRequest>> ListAsync(Repository target, bool open) =>
		await db.PullRequests
			.Include(p => p.Author)
			.Include(p => p.Comments)
			.Where(p => p.RepositoryId == target.Id && (open ? p.State == PullRequestState.Open : p.State != PullRequestState.Open))
			.OrderByDescending(p => p.Id)
			.ToListAsync();

	public async Task<int> CountOpenAsync(Repository target) =>
		await db.PullRequests.CountAsync(p => p.RepositoryId == target.Id && p.State == PullRequestState.Open);
}
