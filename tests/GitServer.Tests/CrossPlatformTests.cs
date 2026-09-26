using GitServer.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace GitServer.Tests;

/// <summary>What keeps the app from depending on Windows: where git comes from, and how folder settings are read.
/// Each test states what it expects on the OS it runs on; CI runs them on both Windows and Linux.</summary>
public class CrossPlatformTests
{
	private static readonly string Root = Path.Combine(Path.GetTempPath(), "gitserver-root");

	[Fact]
	public void ARelativeFolderSetting_ResolvesAgainstTheAppFolder_WhicheverSlashItUses()
	{
		var expected = Path.GetFullPath(Path.Combine(Root, "App_Data", "git"));

		Assert.Equal(expected, ConfigPaths.Resolve("App_Data/git", "X", Root));
		Assert.Equal(expected, ConfigPaths.Resolve(@"App_Data\git", "X", Root));   // appsettings.json used backslashes for years
	}

	[Fact]
	public void AnAbsoluteFolderSetting_IsKept()
	{
		var absolute = Path.Combine(Path.GetTempPath(), "repos");
		Assert.Equal(absolute, ConfigPaths.Resolve(absolute, "X", Root));
	}

	[Theory]
	[InlineData(@"D:\GitRepos")]
	[InlineData(@"C:/Data/Keys")]
	[InlineData(@"\\server\share")]
	public void AWindowsPath_IsRefusedWithTheSettingName_OnlyOutsideWindows(string value)
	{
		if (OperatingSystem.IsWindows())
		{
			Assert.Equal(value, ConfigPaths.Resolve(value, "GitServer:RepositoriesPath", Root));
			return;
		}

		var ex = Assert.Throws<InvalidOperationException>(() => ConfigPaths.Resolve(value, "GitServer:RepositoriesPath", Root));
		Assert.Contains("GitServer:RepositoriesPath", ex.Message);
		Assert.Contains("GitServer__RepositoriesPath", ex.Message);
	}

	[Fact]
	public void AConfiguredGitExecutable_IsUsed_AndTheInstallerStaysOut()
	{
		var provider = new GitExecutablePathProvider(null!, Options.Create(new GitServerOptions { GitExecutable = " /usr/bin/git " }));

		Assert.Equal("/usr/bin/git", provider.CurrentPath);
		Assert.False(provider.IsManagedByInstaller);
	}

	[Fact]
	public void WithoutConfiguration_LinuxAndMacUseGitFromThePath()
	{
		if (OperatingSystem.IsWindows()) return;   // Windows reads the MinGit install from the database instead

		var provider = new GitExecutablePathProvider(null!, Options.Create(new GitServerOptions()));

		Assert.Equal("git", provider.CurrentPath);
		Assert.False(provider.IsManagedByInstaller);
	}
}
