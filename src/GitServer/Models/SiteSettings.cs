namespace GitServer.Models;

/// <summary>Site-wide admin-configurable toggles. Single row, always Id == 1.</summary>
public class SiteSettings
{
    public int Id { get; set; } = 1;

    public bool AllowRegistration { get; set; } = true;
    public bool AllowUserRepoCreation { get; set; } = true;
    public bool AllowPushToCreateRepositories { get; set; } = true;
    public bool AllowAnonymousPush { get; set; }
    public bool ShowCommitAuthorAvatar { get; set; } = true;

    /// <summary>How many days a newly created API key stays valid.</summary>
    public int ApiKeyLifetimeDays { get; set; } = 90;
}
