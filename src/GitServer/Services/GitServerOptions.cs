namespace GitServer.Services;

public class GitServerOptions
{
    public string RepositoriesPath { get; set; } = "C:\\GitRepos";
    public string GitExecutable { get; set; } = "git";
    public bool AllowRegistration { get; set; } = true;
    public bool RequireEmailConfirmation { get; set; } = false;
}
