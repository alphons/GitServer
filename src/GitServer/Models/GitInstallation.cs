namespace GitServer.Models;

/// <summary>A MinGit distribution downloaded and extracted by the admin git-updater feature.</summary>
public class GitInstallation
{
    public int Id { get; set; }
    public string Version { get; set; } = "";
    public string TagName { get; set; } = "";
    public string InstallPath { get; set; } = "";
    public DateTime InstalledAt { get; set; }
    public bool IsActive { get; set; }
}
