namespace GitServer.Services;

public class GitServerOptions
{
    public string RepositoriesPath { get; set; } = "C:\\GitRepos";
    public string GitExecutable { get; set; } = "git";
    public bool AllowRegistration { get; set; } = true;
    public bool RequireEmailConfirmation { get; set; } = false;

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
}
