using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GitServer.Tests.TestSupport;
using Xunit;

namespace GitServer.Tests;

/// <summary>
/// Guards the translation files themselves: every language must be complete and consistent with
/// English, and every key the code asks for must exist. New UI text that is only added to one or
/// two languages (the recurring mistake) now fails the build instead of shipping half-translated.
/// </summary>
public partial class LocalizationFilesTests
{
	private static Dictionary<string, string> Load(string language) =>
		JsonSerializer.Deserialize<Dictionary<string, string>>(
			File.ReadAllText(Path.Combine(TestPaths.LocalizationRoot, language, "strings.json")))!;

	private static IEnumerable<string> RealKeys(Dictionary<string, string> d) =>
		d.Keys.Where(k => !k.StartsWith("__", StringComparison.Ordinal));

	[GeneratedRegex(@"\{\d+\}")]
	private static partial Regex PlaceholderRegex();

	private static string[] Placeholders(string text) =>
		PlaceholderRegex().Matches(text).Select(m => m.Value).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();

	public static IEnumerable<object[]> Languages => TestPaths.LanguageRows();
	public static IEnumerable<object[]> NonEnglishLanguages => TestPaths.LanguageRows().Where(r => (string)r[0] != "en");

	[Fact]
	public void English_And_AtLeastNineOtherLanguages_Exist()
	{
		var languages = TestPaths.Languages().ToList();

		Assert.Contains("en", languages);
		Assert.True(languages.Count >= 10, "Expected at least 10 languages, found: " + string.Join(", ", languages));
	}

	[Theory]
	[MemberData(nameof(Languages))]
	public void StringsFile_IsValidJson_WithADisplayName(string language)
	{
		var strings = Load(language);

		Assert.True(strings.TryGetValue("__name__", out var name) && !string.IsNullOrWhiteSpace(name),
			$"{language}: missing __name__ (shown in the language picker)");
	}

	[Theory]
	[MemberData(nameof(Languages))]
	public void FolderName_IsAValidCulture(string language)
	{
		// LocalizationService formats dates and country names with CultureInfo.GetCultureInfo(folder name).
		var culture = CultureInfo.GetCultureInfo(language);

		Assert.False(string.IsNullOrEmpty(culture.Name));
	}

	[Theory]
	[MemberData(nameof(NonEnglishLanguages))]
	public void Language_HasEveryKeyThatEnglishHas(string language)
	{
		var english = Load("en");
		var strings = Load(language);

		var missing = RealKeys(english).Where(k => !strings.ContainsKey(k)).OrderBy(k => k).ToList();

		Assert.True(missing.Count == 0, $"{language} is missing {missing.Count} key(s): {string.Join(", ", missing)}");
	}

	[Theory]
	[MemberData(nameof(NonEnglishLanguages))]
	public void Language_HasNoKeysThatEnglishLacks(string language)
	{
		var english = Load("en");
		var extra = RealKeys(Load(language)).Where(k => !english.ContainsKey(k)).OrderBy(k => k).ToList();

		Assert.True(extra.Count == 0, $"{language} has key(s) not in en (typo or stale): {string.Join(", ", extra)}");
	}

	[Theory]
	[MemberData(nameof(Languages))]
	public void EveryValue_IsNotBlank(string language)
	{
		var blank = RealKeys(Load(language)).Where(k => string.IsNullOrWhiteSpace(Load(language)[k])).ToList();

		Assert.True(blank.Count == 0, $"{language}: blank value(s): {string.Join(", ", blank)}");
	}

	[Theory]
	[MemberData(nameof(NonEnglishLanguages))]
	public void Placeholders_MatchEnglish(string language)
	{
		var english = Load("en");
		var strings = Load(language);

		var mismatches = RealKeys(english)
			.Where(k => strings.ContainsKey(k) && !Placeholders(english[k]).SequenceEqual(Placeholders(strings[k])))
			.Select(k => $"{k}: en {string.Join("", Placeholders(english[k]))} vs {language} {string.Join("", Placeholders(strings[k]))}")
			.ToList();

		Assert.True(mismatches.Count == 0, $"{language}: placeholder mismatch — {string.Join("; ", mismatches)}");
	}

	[Theory]
	[MemberData(nameof(Languages))]
	public void EmailTemplates_ExistAndCarryTheSamePlaceholdersAsEnglish(string language)
	{
		foreach (var template in new[] { "register", "reset-password" })
		{
			var english = File.ReadAllText(Path.Combine(TestPaths.LocalizationRoot, "en", "emails", template + ".html"));
			var path = Path.Combine(TestPaths.LocalizationRoot, language, "emails", template + ".html");

			Assert.True(File.Exists(path), $"{language}: missing emails/{template}.html");
			var actual = File.ReadAllText(path);
			var tokens = new Regex(@"\{\{[a-z_]+\}\}");
			Assert.Equal(
				tokens.Matches(english).Select(m => m.Value).Distinct().OrderBy(x => x),
				tokens.Matches(actual).Select(m => m.Value).Distinct().OrderBy(x => x));
		}
	}

	[Fact]
	public void EmailLayout_HasTheBodyPlaceholder()
	{
		Assert.Contains("{{body}}", File.ReadAllText(Path.Combine(TestPaths.LocalizationRoot, "_email-layout.html")));
	}

	[Fact]
	public void EveryKeyUsedInTheCode_ExistsInEnglish()
	{
		var english = Load("en");
		var used = new Regex("""\bL\[\s*"([^"\\]+)"\s*\]|\bL\.Format\(\s*"([^"\\]+)"|\bL\(\s*"([^"\\]+)"\s*\)""");
		var missing = new SortedSet<string>();

		foreach (var file in TestPaths.SourceFiles())
		{
			foreach (Match m in used.Matches(File.ReadAllText(file)))
			{
				var key = m.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value;
				if (!english.ContainsKey(key))
					missing.Add($"{key}  ({Path.GetRelativePath(TestPaths.AppProject, file)})");
			}
		}

		Assert.True(missing.Count == 0, "Keys used in code but missing from en/strings.json:\n" + string.Join("\n", missing));
	}

	[Fact]
	public void English_HasNoKeyThatTheCodeNeverUses()
	{
		// Dead strings rot: they get translated, kept in sync, and never shown. Dynamic keys
		// ("nav_" + x) would defeat this, so the code base deliberately uses only literal keys.
		var english = Load("en");
		var sources = string.Join("\n", TestPaths.SourceFiles().Select(File.ReadAllText));
		var unused = RealKeys(english).Where(k => !sources.Contains("\"" + k + "\"")).OrderBy(k => k).ToList();

		Assert.True(unused.Count == 0, $"{unused.Count} unused key(s) in en/strings.json: {string.Join(", ", unused)}");
	}
}
