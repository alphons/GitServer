using System.Text.RegularExpressions;
using GitServer.Models;

namespace GitServer.Services;

/// <summary>A language variant of a markdown file in a folder, e.g. README.nl.md or Test.nl.md.</summary>
public record MdVariant(string FileName, string? Lang, string Flag);

/// <summary>
/// Finds language variants of a markdown file (base.md, base.&lt;lang&gt;.md) that live next to each
/// other in the same folder, so the viewer can offer a flag picker instead of just showing one of them.
/// </summary>
public static class MarkdownVariants
{
	// "Test.nl.md": base "Test", lang "nl". Checked first so the greedy base swallows everything but the
	// trailing ".<lang>.md", e.g. "A.b.nl.md" -> base "A.b", lang "nl".
	private static readonly Regex LangSuffixPattern = new(@"^(?<base>.+)\.(?<lang>[A-Za-z]{2,3})\.md$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
	private static readonly Regex PlainPattern = new(@"^(?<base>.+)\.md$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Dictionary<string, string> LangToFlag = new(StringComparer.OrdinalIgnoreCase)
	{
		["nl"] = "🇳🇱", ["en"] = "🇬🇧", ["de"] = "🇩🇪", ["fr"] = "🇫🇷", ["es"] = "🇪🇸",
		["it"] = "🇮🇹", ["pt"] = "🇵🇹", ["pl"] = "🇵🇱", ["ru"] = "🇷🇺", ["tr"] = "🇹🇷",
		["ja"] = "🇯🇵", ["zh"] = "🇨🇳", ["ko"] = "🇰🇷", ["ar"] = "🇸🇦", ["sv"] = "🇸🇪",
		["da"] = "🇩🇰", ["no"] = "🇳🇴", ["fi"] = "🇫🇮", ["uk"] = "🇺🇦", ["cs"] = "🇨🇿",
		["ro"] = "🇷🇴", ["hu"] = "🇭🇺", ["el"] = "🇬🇷", ["he"] = "🇮🇱", ["hi"] = "🇮🇳",
	};

	public static string FlagFor(string? lang) =>
		lang != null && LangToFlag.TryGetValue(lang, out var flag) ? flag : "🏳️";

	/// <summary>Splits a markdown file name into its base name and optional language code.</summary>
	public static (string Base, string? Lang)? Parse(string fileName)
	{
		var match = LangSuffixPattern.Match(fileName);
		if (match.Success)
			return (match.Groups["base"].Value, match.Groups["lang"].Value.ToLowerInvariant());

		match = PlainPattern.Match(fileName);
		return match.Success ? (match.Groups["base"].Value, null) : null;
	}

	/// <summary>All markdown variants of <paramref name="baseName"/> (e.g. "README") present among <paramref name="entries"/>,
	/// with the language-less file first and the rest sorted by language code.</summary>
	public static List<MdVariant> FindVariants(IEnumerable<TreeEntry> entries, string baseName) => entries
		.Where(e => e.Type == "blob")
		.Select(e => (Entry: e, Parsed: Parse(e.Name)))
		.Where(x => x.Parsed is { } p && string.Equals(p.Base, baseName, StringComparison.OrdinalIgnoreCase))
		.Select(x => new MdVariant(x.Entry.Name, x.Parsed!.Value.Lang, FlagFor(x.Parsed.Value.Lang)))
		.OrderBy(o => o.Lang != null)
		.ThenBy(o => o.Lang, StringComparer.OrdinalIgnoreCase)
		.ToList();
}
