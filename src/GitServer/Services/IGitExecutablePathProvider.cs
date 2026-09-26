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

	/// <summary>True when git comes from the admin's MinGit installer (Windows); false when it is the system's own git
	/// (Linux, macOS, or <see cref="GitServerOptions.GitExecutable"/> set), which the installer must leave alone.</summary>
	bool IsManagedByInstaller { get; }
}

/// <summary>Where git comes from:
/// <list type="bullet">
/// <item><see cref="GitServerOptions.GitExecutable"/> when set, on any platform;</item>
/// <item>otherwise on Linux and macOS the system's git on the PATH, installed with the package manager (MinGit is Windows-only);</item>
/// <item>otherwise on Windows the active MinGit install from /dashboard/Admin/GitVersion. There is deliberately no PATH fallback
/// there: until an admin installs a version, CurrentPath is empty and GitProcessService.GetVersion() reports "not installed"
/// rather than silently running whatever "git" happens to resolve to.</item>
/// </list></summary>
public class GitExecutablePathProvider : IGitExecutablePathProvider
{
	private string _currentPath;

	public GitExecutablePathProvider(IServiceScopeFactory scopeFactory, IOptions<GitServerOptions> options)
	{
		var configured = options.Value.GitExecutable;
		if (!string.IsNullOrWhiteSpace(configured) || !OperatingSystem.IsWindows())
		{
			_currentPath = string.IsNullOrWhiteSpace(configured) ? "git" : configured.Trim();
			IsManagedByInstaller = false;
			return;
		}

		using var scope = scopeFactory.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var active = db.GitInstallations.AsNoTracking().FirstOrDefault(i => i.IsActive);
		_currentPath = active != null ? GitInstallerService.GetGitExePath(active.InstallPath) : "";
		IsManagedByInstaller = true;
	}

	public string CurrentPath => _currentPath;

	public bool IsManagedByInstaller { get; }

	public void SetPath(string path) => _currentPath = path;
}
