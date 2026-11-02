namespace GitServer.Services;

/// <summary>The colour themes a user can pick. The value is stored on the user and set as data-theme on the page.</summary>
public static class Themes
{
	public const string Default = "dark";

	/// <summary>Remembers the choice of visitors who are not signed in.</summary>
	public const string CookieName = "theme";

	public static readonly string[] All = ["dark", "light", "pastel"];

	/// <summary>The theme to render with: the given one when it is known, otherwise the default.</summary>
	public static string Normalize(string? theme) => All.Contains(theme) ? theme! : Default;
}
