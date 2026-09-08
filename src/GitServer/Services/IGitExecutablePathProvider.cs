using GitServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GitServer.Services;

/// <summary>Holds the git executable path currently in use, switchable at runtime by the
/// admin git-updater feature without requiring an app restart.</summary>
public interface IGitExecutablePathProvider
{
    string CurrentPath { get; }
    void SetPath(string path);
}

/// <summary>Seeded at startup from the active GitInstallation row (if any), falling back to
/// GitServerOptions.GitExecutable so existing deployments keep working unchanged until an
/// admin explicitly installs and activates a MinGit version.</summary>
public class GitExecutablePathProvider : IGitExecutablePathProvider
{
    private string _currentPath;

    public GitExecutablePathProvider(IServiceScopeFactory scopeFactory, IOptions<GitServerOptions> options)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var active = db.GitInstallations.AsNoTracking().FirstOrDefault(i => i.IsActive);
        _currentPath = active != null ? GitInstallerService.GetGitExePath(active.InstallPath) : options.Value.GitExecutable;
    }

    public string CurrentPath => _currentPath;

    public void SetPath(string path) => _currentPath = path;
}
