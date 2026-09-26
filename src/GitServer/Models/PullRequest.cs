namespace GitServer.Models;

public enum PullRequestState { Open = 0, Closed = 1, Merged = 2 }

/// <summary>A request to merge <see cref="SourceBranch"/> of <see cref="SourceRepository"/> (this repository or a fork of it)
/// into <see cref="TargetBranch"/> of <see cref="Repository"/>. The source branch is copied into the target repository as
/// refs/pull/{Id}/head, so comparing and merging never need the fork again — the pull request stays readable after
/// the fork or its branch is deleted (then <see cref="SourceRepositoryId"/> is null).</summary>
public class PullRequest
{
	public int Id { get; set; }

	/// <summary>The repository the pull request asks to merge into.</summary>
	public int RepositoryId { get; set; }
	public Repository Repository { get; set; } = null!;
	public string TargetBranch { get; set; } = "main";

	public int? SourceRepositoryId { get; set; }
	public Repository? SourceRepository { get; set; }
	public string SourceBranch { get; set; } = "";

	/// <summary>The "owner/name" of the source when it was opened, still shown after the source repository is deleted.</summary>
	public string SourceDisplayName { get; set; } = "";

	public string AuthorId { get; set; } = "";
	public AppUser Author { get; set; } = null!;
	public string Title { get; set; } = "";
	public string Body { get; set; } = "";

	public PullRequestState State { get; set; } = PullRequestState.Open;
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
	public DateTime? ClosedAt { get; set; }

	public string? MergedById { get; set; }
	public AppUser? MergedBy { get; set; }
	public string? MergeCommitSha { get; set; }

	public ICollection<PullRequestComment> Comments { get; set; } = new List<PullRequestComment>();

	/// <summary>Where the source branch is kept inside the target repository.</summary>
	public string HeadRef => $"refs/pull/{Id}/head";
}

public class PullRequestComment
{
	public int Id { get; set; }
	public int PullRequestId { get; set; }
	public PullRequest PullRequest { get; set; } = null!;
	public string AuthorId { get; set; } = "";
	public AppUser Author { get; set; } = null!;
	public string Body { get; set; } = "";
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
