using GitServer.Data;
using Microsoft.EntityFrameworkCore;

namespace GitServer.Services;

/// <summary>Holds the git executable path currently in use, switchable at runtime by the
/// admin git-updater feature without requiring an app restart.</summary>
public interface IGitExecutablePathProvider
{
    string CurrentPath { get; }
    void SetPath(string path);
}

/// <summary>Seeded at startup from the active GitInstallation row, if any. No other fallback:
/// until an admin installs and activates a MinGit version via /Admin/GitVersion, CurrentPath is
/// empty and GitProcessService.GetVersion() reports "not installed" rather than silently running
/// whatever "git" happens to resolve to on the server's PATH.</summary>
public class GitExecutablePathProvider : IGitExecutablePathProvider
{
    private string _currentPath;

    public GitExecutablePathProvider(IServiceScopeFactory scopeFactory)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var active = db.GitInstallations.AsNoTracking().FirstOrDefault(i => i.IsActive);
        _currentPath = active != null ? GitInstallerService.GetGitExePath(active.InstallPath) : "";
    }

    public string CurrentPath => _currentPath;

    public void SetPath(string path) => _currentPath = path;
}
