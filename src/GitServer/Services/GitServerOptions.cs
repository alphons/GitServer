namespace GitServer.Services;

public class GitServerOptions
{
	public string RepositoriesPath { get; set; } = "C:\\GitRepos";

	/// <summary>"Sqlite" (default) or "SqlServer". ConnectionStrings:Default is a connection string for that provider.
	/// Each provider has its own migrations, applied at startup.</summary>
	public string DatabaseProvider { get; set; } = "Sqlite";

	/// <summary>Address shown on the terms page for questions or requests about personal data. Empty = not shown.</summary>
	public string ContactEmail { get; set; } = "";

	/// <summary>Wrong passwords in a row (web login and git over HTTPS) before the account is locked for LoginLockoutMinutes.</summary>
	public int MaxFailedLoginAttempts { get; set; } = 5;

	/// <summary>How long an account stays locked after too many failed logins.</summary>
	public int LoginLockoutMinutes { get; set; } = 15;

	/// <summary>Requests per minute one IP address may make to the JSON API (/api). 0 = no limit.</summary>
	public int ApiRequestsPerMinute { get; set; } = 300;

	/// <summary>Sign-in, registration and password-reset form posts per minute per IP address. 0 = no limit.</summary>
	public int AuthRequestsPerMinute { get; set; } = 10;

	/// <summary>Trust X-Forwarded-For / X-Forwarded-Proto from a reverse proxy, so limits and the audit log see the visitor's IP
	/// instead of the proxy's. Only enable this when the app is reachable exclusively through that proxy.</summary>
	public bool TrustForwardedHeaders { get; set; }

	/// <summary>Only used to seed SiteSettings.AllowRegistration the first time the app runs.
	/// After that, the live value lives in the database and is managed from /dashboard/Admin/Settings.</summary>
	public bool AllowRegistration { get; set; } = true;

	/// <summary>Visibility for repositories auto-created on first push (e.g. via "existing remote" in an IDE).</summary>
	public bool DefaultPrivateOnAutoCreate { get; set; } = true;

	/// <summary>Path segment in front of git smart-HTTP URLs, e.g. "/git". Set to "" to serve at the root.</summary>
	public string GitPathPrefix { get; set; } = "/git";

	/// <summary>GitPathPrefix normalized to either "" or "/segment" (no trailing slash).</summary>
	public string NormalizedGitPathPrefix
	{
		get
		{
			var trimmed = GitPathPrefix?.Trim('/') ?? "";
			return trimmed.Length == 0 ? "" : "/" + trimmed;
		}
	}

	/// <summary>Max size in MB for a single push (Kestrel request body). Null = unlimited.
	/// Note: IIS's own limit in Web.Config (maxAllowedContentLength) is separate and not driven by this setting.</summary>
	public long? MaxPushSizeMb { get; set; } = null;

	/// <summary>Number of items per page on the /dashboard/explore repo listing.</summary>
	public int ExploreRepoPageSize { get; set; } = 10;

	/// <summary>Number of items per page on the /dashboard/explore/users listing.</summary>
	public int ExploreUserPageSize { get; set; } = 10;

	/// <summary>Number of repos shown on the home page's "recent repositories" list.</summary>
	public int IndexRecentReposCount { get; set; } = 20;

	/// <summary>Number of items per page on a user's profile repository listing.</summary>
	public int ProfileRepoPageSize { get; set; } = 10;

	/// <summary>Number of items per page on the /dashboard/Admin/Users listing.</summary>
	public int AdminUsersPageSize { get; set; } = 10;

	/// <summary>GitHub Releases API base URL the admin git-updater queries for MinGit versions
	/// (no trailing slash), e.g. "/latest" and "?per_page=N" are appended to it.</summary>
	public string GitReleasesApiUrl { get; set; } = "https://api.github.com/repos/git-for-windows/git/releases";

	/// <summary>Regex matched against each release asset's file name to pick the one to download
	/// (e.g. the 64-bit MinGit zip, skipping the 32-bit/ARM64/busybox/full-installer variants).</summary>
	public string GitReleaseAssetPattern { get; set; } = @"^MinGit-[\d.]+-64-bit\.zip$";

	/// <summary>Where the admin git-updater extracts downloaded MinGit versions, one subfolder per
	/// release tag. Relative paths are resolved against the app's content root; use an absolute
	/// path (e.g. on another drive) to keep installs alongside RepositoriesPath instead.</summary>
	public string GitExecutableInstallRoot { get; set; } = "App_Data\\git";
}
