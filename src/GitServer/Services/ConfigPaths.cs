using System.Text.RegularExpressions;

namespace GitServer.Services;

/// <summary>Makes folder settings from appsettings.json work on every OS. On Windows nothing changes. Elsewhere a relative
/// path written with backslashes ("App_Data\git") is read with forward slashes, and a Windows-only path ("D:\GitRepos",
/// "\\server\share") stops the app at startup with a message naming the setting, instead of a folder literally called
/// "D:\GitRepos" appearing next to the app.</summary>
public static partial class ConfigPaths
{
	[GeneratedRegex(@"^([A-Za-z]:[\\/]|\\\\)")]
	private static partial Regex WindowsOnlyRegex();

	/// <param name="setting">The configuration key, for the error message, e.g. "GitServer:RepositoriesPath".</param>
	/// <param name="contentRoot">Relative paths resolve against this (the app's folder), not the current directory.</param>
	public static string Resolve(string value, string setting, string contentRoot)
	{
		if (!OperatingSystem.IsWindows())
		{
			if (WindowsOnlyRegex().IsMatch(value))
				throw new InvalidOperationException(
					$"{setting} is '{value}', a Windows path. Set {setting} to a folder on this machine (e.g. in appsettings.Production.json " +
					$"or the environment variable {setting.Replace(":", "__")}).");
			value = value.Replace('\\', '/');
		}
		return Path.IsPathRooted(value) ? value : Path.GetFullPath(Path.Combine(contentRoot, value));
	}
}
