namespace GitServer.Tests.TestSupport;

/// <summary>Locates the repository checkout so tests can read the real Localization files and
/// scan the real source tree.</summary>
public static class TestPaths
{
	public static string RepoRoot { get; } = FindRepoRoot();

	public static string AppProject => Path.Combine(RepoRoot, "src", "GitServer");
	public static string LocalizationRoot => Path.Combine(AppProject, "Localization");

	/// <summary>Every language folder that has a strings.json (en, nl, de, ...).</summary>
	public static IEnumerable<string> Languages() =>
		Directory.GetDirectories(LocalizationRoot)
			.Where(d => File.Exists(Path.Combine(d, "strings.json")))
			.Select(d => Path.GetFileName(d)!)
			.OrderBy(l => l, StringComparer.Ordinal);

	/// <summary>xUnit theory data: one row per language, so a failure names the language.</summary>
	public static IEnumerable<object[]> LanguageRows() => Languages().Select(l => new object[] { l });

	/// <summary>Application source files (.cs / .cshtml), excluding build output and EF migrations.</summary>
	public static IEnumerable<string> SourceFiles() =>
		Directory.EnumerateFiles(AppProject, "*.*", SearchOption.AllDirectories)
			.Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase))
			.Where(f =>
			{
				var rel = Path.GetRelativePath(AppProject, f).Replace('\\', '/');
				return !rel.StartsWith("bin/") && !rel.StartsWith("obj/") && !rel.StartsWith("Data/Migrations/");
			});

	private static string FindRepoRoot()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null)
		{
			if (File.Exists(Path.Combine(dir.FullName, "GitServer.slnx")))
				return dir.FullName;
			dir = dir.Parent;
		}
		throw new InvalidOperationException("Could not locate GitServer.slnx above " + AppContext.BaseDirectory);
	}
}
