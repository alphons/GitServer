using GitServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GitServer.Services;

/// <summary>Names that user and group accounts may never take, because the first URL segment
/// (/{owner}/{repo}) would then collide with something the site itself serves at the root.
/// <para>Built-in (read-only) names: the JSON API (/api), the pages (/dashboard), the git path prefix and the
/// static folders in wwwroot — read from code, configuration and disk, so nothing needs editing when those change.</para>
/// <para>On top of that, admins maintain wildcard patterns in the database ("*" = any run of characters,
/// "?" = one character). Everything is matched case-insensitively.</para></summary>
public class ReservedNames(AppDbContext db, IOptions<GitServerOptions> options, IWebHostEnvironment env)
{
	private static readonly string[] Fixed = ["api", "dashboard"];

	/// <summary>The read-only names, sorted.</summary>
	public IReadOnlyList<string> BuiltIn()
	{
		var names = new HashSet<string>(Fixed, StringComparer.OrdinalIgnoreCase);

		var prefix = options.Value.NormalizedGitPathPrefix.Trim('/');
		if (prefix.Length > 0) names.Add(prefix);

		if (Directory.Exists(env.WebRootPath))
			foreach (var entry in new DirectoryInfo(env.WebRootPath).GetFileSystemInfos())
				names.Add(Path.GetFileNameWithoutExtension(entry.Name));

		return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
	}

	public async Task<bool> IsReservedAsync(string? name)
	{
		if (string.IsNullOrWhiteSpace(name)) return false;
		name = name.Trim();

		if (BuiltIn().Contains(name, StringComparer.OrdinalIgnoreCase)) return true;

		var patterns = await db.ReservedNamePatterns.Select(p => p.Pattern).ToListAsync();
		return EmailBlocklist.IsBlocked(name, patterns);   // same wildcard matcher as the blocked e-mail patterns
	}
}
