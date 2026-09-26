namespace GitServer.Controllers.Api;

// Request and response shapes of the JSON API under /api. JSON property names are the camelCase forms of these members.

/// <summary>Returned with a 4xx status when a request is refused; <see cref="Error"/> is a message in the caller's language.</summary>
public record ErrorResponse(string Error);

// ---- Repositories ------------------------------------------------------------------------------------------

/// <summary>Someone who has access to a private repository: a user (UserName is set) or a group (GroupName is set).</summary>
public record CollaboratorDto(string? UserName, string? GroupName);

/// <summary>One repository as shown in a list. Dates are already formatted in the caller's time zone and language.</summary>
public record RepoCardDto(
	string DisplayName, string Href, bool IsPrivate, bool IsReadOnly, string? Description,
	string? Updated, string? Created, IReadOnlyList<CollaboratorDto>? Collaborators);

/// <summary>A profile's repositories: the user's own, plus (for the owner only) those reached through groups. Both lists are paged independently.</summary>
public record UserReposResponse(
	string Query, bool IsOwner, int ResultCount, int TotalCount, int Page, bool HasNext, IReadOnlyList<RepoCardDto> Repos,
	int GroupTotalCount, int GroupPage, bool GroupHasNext, IReadOnlyList<RepoCardDto> GroupRepos);

public record GroupReposResponse(int Page, bool HasNext, IReadOnlyList<RepoCardDto> Repos);

/// <summary>Where to put a fork: <see cref="Group"/> is the name of a group where the caller may create repositories, or null for their own
/// namespace; <see cref="Name"/> is null to keep the source's name.</summary>
public record ForkRequest(string? Name = null, string? Group = null);

/// <summary>The fork that was created.</summary>
public record ForkResponse(string Owner, string Name, string Href, bool IsPrivate);

// ---- Webhooks ----------------------------------------------------------------------------------------------

/// <summary>A repository's webhook. The secret itself is never returned, only whether there is one.
/// <see cref="Events"/> holds "push", "issues" and/or "issue_comment".</summary>
public record WebhookDto(int Id, string Url, IReadOnlyList<string> Events, bool Active, bool HasSecret, string Created, WebhookDeliveryDto? LastDelivery);

/// <summary>One delivery attempt. <see cref="StatusCode"/> is null when the receiver did not answer; <see cref="Error"/> says why.</summary>
public record WebhookDeliveryDto(int Id, Guid DeliveryId, string Event, int Attempt, string At, int? StatusCode, string? Error, int DurationMs, bool Succeeded);

/// <summary>A new webhook. <see cref="Events"/> defaults to ["push"]; an empty or missing secret sends no signature.</summary>
public record CreateWebhookRequest(string? Url, string? Secret = null, IReadOnlyList<string>? Events = null, bool Active = true);

/// <summary>Changes to a webhook; null leaves a field as it is. <see cref="Secret"/> "" removes the secret.</summary>
public record UpdateWebhookRequest(string? Url = null, string? Secret = null, IReadOnlyList<string>? Events = null, bool? Active = null);

/// <summary>The id of the queued delivery; its outcome appears in the deliveries list shortly after.</summary>
public record WebhookPingResponse(Guid DeliveryId);

// ---- Administration ----------------------------------------------------------------------------------------

public record AdminUserDto(
	string Id, string? UserName, string DisplayName, string? Email, string Created, string? LastLogin,
	bool IsDisabled, bool IsAdmin, bool IsPending, bool IsSelf);

public record AdminUsersResponse(int Page, bool HasNext, int TotalPages, IReadOnlyList<AdminUserDto> Users);

public record ReservedPatternDto(int Id, string Pattern);

/// <summary>BuiltIn names cannot be changed; Patterns are wildcard patterns ("*" any characters, "?" one character) that admins manage.</summary>
public record ReservedNamesResponse(IReadOnlyList<string> BuiltIn, IReadOnlyList<ReservedPatternDto> Patterns);

public record ReservedNameRequest(string? Pattern);

public record StartInstallRequest(string? TagName);
public record StartInstallResponse(string JobId);

public record FileEntryDto(string Name, bool IsDirectory, long? Size);

/// <summary>Either Content (the file as text) or Error (why it cannot be previewed) is set.</summary>
public record FileContentResponse(string? Content, string? Error);

// ---- API keys ----------------------------------------------------------------------------------------------

/// <summary>ReadOnly: the key may only use GET; anything that changes data is refused with 403.</summary>
public record CreateApiKeyRequest(string? Name, bool ReadOnly = false);
public record SetApiKeyEnabledRequest(bool Enabled);

public record ApiKeyDto(
	int Id, string Name, string Prefix, bool IsEnabled, bool IsReadOnly, bool IsExpired, string Created, string Expires, string? LastUsed);

/// <summary>Key is the plain-text API key. It is only ever returned here, once, and cannot be retrieved again.</summary>
public record CreatedApiKeyResponse(string Key, ApiKeyDto ApiKey);

// ---- Audit log ---------------------------------------------------------------------------------------------

/// <summary>One audit entry. Via is "web" (sign-in cookie) or "api-key".</summary>
public record AuditEntryDto(int Id, string At, string Actor, string Action, string? Target, string? Details, string Via, string? Ip);

public record AuditLogResponse(int Page, bool HasNext, IReadOnlyList<AuditEntryDto> Entries);

// ---- Database migration -------------------------------------------------------------------------------------

/// <summary>Whether Admin > Database migration should be shown at all: only makes sense while still on SQLite,
/// with a SQL Server connection string already configured to migrate to.</summary>
public record DatabaseMigrationStatusResponse(bool IsAvailable, bool SqlServerConfigured);

public record DatabaseMigrationCheckResponse(bool CanConnect, bool IsFresh, string? Error);

public record DatabaseMigrationTableResultDto(string Table, int Rows);
public record DatabaseMigrationRunResponse(bool Success, string? Error, IReadOnlyList<DatabaseMigrationTableResultDto> Tables);

// ---- Pull requests -----------------------------------------------------------------------------------------

/// <summary>A pull request. <see cref="State"/> is "open", "closed" or "merged"; <see cref="Source"/> is "owner/repo" of the branch it merges from.</summary>
public record PullRequestDto(
	int Number, string Title, string Body, string State, string Author, string Source, string SourceBranch, string TargetBranch,
	string Created, string? MergedBy, string? MergeCommitSha, int Comments, string Href);

/// <summary>The details of one pull request: what it brings in and whether the server can merge it.</summary>
public record PullRequestDetailDto(PullRequestDto PullRequest, bool Mergeable, IReadOnlyList<string> Commits, IReadOnlyList<string> Files);

/// <summary>A new pull request from <see cref="Head"/> into <see cref="Base"/>. <see cref="HeadRepo"/> ("owner/repo") is a fork to
/// merge from; null means a branch of the repository itself. <see cref="Base"/> defaults to the repository's default branch.</summary>
public record CreatePullRequestRequest(string? Title, string? Head, string? Base = null, string? HeadRepo = null, string? Body = null);

public record PullRequestCommentRequest(string? Body);

public record PullRequestCommentDto(int Id, string Author, string Body, string Created);

/// <summary>How to merge: "merge" (a merge commit, the default) or "squash" (one new commit).</summary>
public record MergePullRequestRequest(string? Method = null);
