using GitServer.Services;
using GitServer.Tests.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace GitServer.Tests;

public class LocalizationServiceTests : IDisposable
{
	private readonly List<string> _tempDirs = new();

	public void Dispose()
	{
		foreach (var dir in _tempDirs.Where(Directory.Exists))
			Directory.Delete(dir, recursive: true);
	}

	private sealed class FakeEnvironment(string contentRoot) : IWebHostEnvironment
	{
		public string ApplicationName { get; set; } = "GitServer.Tests";
		public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
		public string WebRootPath { get; set; } = "";
		public string EnvironmentName { get; set; } = "Test";
		public string ContentRootPath { get; set; } = contentRoot;
		public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
	}

	private static LocalizationService Create(string? langCookie, string? contentRoot = null)
	{
		var context = new DefaultHttpContext();
		if (langCookie != null) context.Request.Headers.Cookie = $"lang={langCookie}";
		return new LocalizationService(new HttpContextAccessor { HttpContext = context },
			new FakeEnvironment(contentRoot ?? TestPaths.AppProject));
	}

	// ---- Language selection ------------------------------------------------------------------

	[Fact]
	public void WithoutACookie_TheLanguageIsEnglish()
	{
		var l = Create(null);

		Assert.Equal("en", l.CurrentLanguage);
		Assert.Equal("Groups", l["nav_groups"]);
	}

	[Theory]
	[InlineData("nl", "Groepen")]
	[InlineData("de", "Gruppen")]
	[InlineData("fr", "Groupes")]
	[InlineData("es", "Grupos")]
	public void TheLangCookie_SelectsTheTranslation(string lang, string expected)
	{
		var l = Create(lang);

		Assert.Equal(lang, l.CurrentLanguage);
		Assert.Equal(expected, l["nav_groups"]);
	}

	[Theory]
	[InlineData("xx")]
	[InlineData("")]
	[InlineData("../../etc")]
	public void AnUnknownOrHostileCookie_FallsBackToEnglish(string cookie)
	{
		var l = Create(cookie);

		Assert.Equal("en", l.CurrentLanguage);
		Assert.Equal("Groups", l["nav_groups"]);
	}

	[Fact]
	public void AnUnknownKey_IsReturnedAsIs_SoMissingTextIsVisibleNotABlank()
	{
		Assert.Equal("no_such_key_anywhere", Create("nl")["no_such_key_anywhere"]);
	}

	[Fact]
	public void Format_FillsPlaceholders_InTheSelectedLanguage()
	{
		Assert.Equal("User alice saved.", Create(null).Format("admin_user_saved", "alice"));
		Assert.Equal("Gebruiker alice opgeslagen.", Create("nl").Format("admin_user_saved", "alice"));
	}

	[Fact]
	public void Format_WithTooFewArguments_DoesNotThrow_AndReturnsTheRawText()
	{
		Assert.Equal("User {0} saved.", Create(null).Format("admin_user_saved"));
	}

	[Fact]
	public void ATranslationMissingInTheSelectedLanguage_FallsBackToEnglish()
	{
		// A throwaway language "qq" that only translates one key. English is copied alongside so the
		// process-wide dictionary cache can never end up holding an empty English table.
		var root = Path.Combine(Path.GetTempPath(), $"gitserver-l10n-{Guid.NewGuid():N}");
		_tempDirs.Add(root);
		Directory.CreateDirectory(Path.Combine(root, "Localization", "en"));
		Directory.CreateDirectory(Path.Combine(root, "Localization", "qq"));
		File.Copy(Path.Combine(TestPaths.LocalizationRoot, "en", "strings.json"), Path.Combine(root, "Localization", "en", "strings.json"));
		File.WriteAllText(Path.Combine(root, "Localization", "qq", "strings.json"), """{ "__name__": "Test", "nav_groups": "QQ-Groups" }""");

		var l = Create("qq", root);

		Assert.Equal("qq", l.CurrentLanguage);
		Assert.Equal("QQ-Groups", l["nav_groups"]);
		Assert.Equal("Explore", l["nav_explore"]); // not translated in qq -> English
	}

	[Fact]
	public void AvailableLanguages_ListsEveryFolder_WithItsDisplayName_EnglishFirst()
	{
		var languages = Create(null).AvailableLanguages().ToList();

		Assert.Equal("en", languages[0].Code); // __order__ = 1
		Assert.Equal("English", languages[0].Name);
		Assert.Contains(languages, x => x is { Code: "nl", Name: "Nederlands" });
		Assert.Equal(TestPaths.Languages().Count(), languages.Count);
	}

	// ---- Cultures, countries -----------------------------------------------------------------

	[Fact]
	public void CurrentCulture_FollowsTheLanguage_SoDatesAndMonthsAreLocalised()
	{
		var date = new DateTime(2026, 9, 18);

		// Full month names: the abbreviations differ between Windows (NLS) and Linux (ICU) data.
		Assert.Equal("18 september 2026", date.ToString("d MMMM yyyy", Create("nl").CurrentCulture));
		Assert.Equal("18 September 2026", date.ToString("d MMMM yyyy", Create("en").CurrentCulture));
		Assert.Equal("18 septembre 2026", date.ToString("d MMMM yyyy", Create("fr").CurrentCulture));
		Assert.Equal("18 September 2026", date.ToString("d MMMM yyyy", Create("de").CurrentCulture));
	}

	[Fact]
	public void CountryName_IsLocalised_AndFallsBackToTheCodeWhenUnknown()
	{
		Assert.Equal("Netherlands", Create("en").CountryName("NL"));
		Assert.Equal("Nederland", Create("nl").CountryName("NL"));
		Assert.Equal("Duitsland", Create("nl").CountryName("DE"));
		Assert.Equal("ZZ", Create("en").CountryName("ZZ"));
		Assert.Equal("", Create("en").CountryName(null));
		Assert.Equal("", Create("en").CountryName(""));
	}

	[Fact]
	public void AvailableCountries_AreSortedByLocalisedName_AndIncludeTheNetherlands()
	{
		var countries = Create("nl").AvailableCountries().ToList();

		Assert.Contains(countries, c => c.Code == "NL" && c.Name == "Nederland");
		Assert.True(countries.Count > 150);
		var names = countries.Select(c => c.Name).ToList();
		Assert.Equal(names.OrderBy(n => n, StringComparer.Create(new System.Globalization.CultureInfo("nl"), false)), names);
	}

	// ---- Emails ------------------------------------------------------------------------------

	[Fact]
	public void RenderEmail_ReplacesPlaceholders_AndWrapsTheSharedLayout()
	{
		var html = Create("nl").RenderEmail("register", ("link", "https://example.test/confirm?t=1"), ("button_label", "Bevestig"), ("fallback_label", "Werkt de knop niet?"));

		Assert.Contains("https://example.test/confirm?t=1", html);
		Assert.Contains("Bevestig", html);
		Assert.DoesNotContain("{{link}}", html);
		Assert.DoesNotContain("{{body}}", html);
		Assert.Contains("<html", html, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void RenderEmail_UnknownTemplate_GivesAnEmptyBodyNotAnException()
	{
		Assert.DoesNotContain("{{body}}", Create("nl").RenderEmail("does-not-exist"));
	}
}
