using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace GitServer.Services;

public class LocalizationService(IHttpContextAccessor httpContextAccessor, IWebHostEnvironment env)
{
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private readonly string _localizationPath = Path.Combine(env.ContentRootPath, "Localization");

    private static readonly ConcurrentDictionary<string, Dictionary<string, string>> _cache = new();
    private static readonly ConcurrentDictionary<string, List<(string Code, string Name)>> _countryCache = new();

    // Maps each ISO 3166-1 region code to one representative "xx-CC" culture name for that region
    // (e.g. "NL" -> "nl-NL"). CultureInfo.DisplayName translates properly per CurrentUICulture,
    // unlike RegionInfo.DisplayName, which always returns the region's own native name regardless
    // of CurrentUICulture.
    private static readonly Lazy<Dictionary<string, string>> _regionToCulture = new(() =>
        CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Select(c =>
            {
                try { return (Culture: c.Name, Region: new RegionInfo(c.Name).TwoLetterISORegionName); }
                catch { return (Culture: (string?)null, Region: (string?)null); }
            })
            .Where(x => x.Culture != null && x.Region != null)
            .GroupBy(x => x.Region!)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Culture, StringComparer.Ordinal).First().Culture!));

	public string CurrentLanguage
    {
        get
        {
            var ctx = _httpContextAccessor.HttpContext;
            if (ctx?.Request.Cookies.TryGetValue("lang", out var lang) == true
                && !string.IsNullOrEmpty(lang)
                && File.Exists(Path.Combine(_localizationPath, lang + ".json")))
            {
                return lang;
            }
            return "en";
        }
    }

    public IEnumerable<(string Code, string Name)> AvailableLanguages()
    {
        if (!Directory.Exists(_localizationPath))
            yield break;

        var langs = Directory.GetFiles(_localizationPath, "*.json")
            .Select(file =>
            {
                var code = Path.GetFileNameWithoutExtension(file);
                var dict = GetDictionary(code);
                var name = dict.TryGetValue("__name__", out var n) ? n : code;
                var order = dict.TryGetValue("__order__", out var o) && int.TryParse(o, out var oi) ? oi : 999;
                return (code, name, order);
            })
            .OrderBy(x => x.order)
            .ThenBy(x => x.name);

        foreach (var (code, name, _) in langs)
            yield return (code, name);
    }

    /// <summary>All known countries as (ISO 3166-1 alpha-2 code, localized name), sorted by localized name.</summary>
    public IEnumerable<(string Code, string Name)> AvailableCountries() =>
        _countryCache.GetOrAdd(CurrentLanguage, lang =>
        {
            var culture = GetCultureOrDefault(lang);
            var list = _regionToCulture.Value.Keys
                .Select(code => (Code: code, Name: LocalizedRegionName(code, culture)))
                .Where(x => x.Name != null)
                .Select(x => (x.Code, Name: x.Name!))
                .ToList();
            list.Sort((a, b) => string.Compare(a.Name, b.Name, culture, CompareOptions.None));
            return list;
        });

    /// <summary>Localized display name for a stored ISO 3166-1 alpha-2 country code, or the code itself if unknown.</summary>
    public string CountryName(string? code)
    {
        if (string.IsNullOrEmpty(code)) return "";
        var culture = GetCultureOrDefault(CurrentLanguage);
        return LocalizedRegionName(code, culture) ?? code;
    }

    private static CultureInfo GetCultureOrDefault(string lang)
    {
        try { return CultureInfo.GetCultureInfo(lang); }
        catch (CultureNotFoundException) { return CultureInfo.GetCultureInfo("en"); }
    }

    private static string? LocalizedRegionName(string regionCode, CultureInfo culture)
    {
        if (!_regionToCulture.Value.TryGetValue(regionCode, out var representativeCulture))
            return null;

        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = culture;
            var displayName = CultureInfo.GetCultureInfo(representativeCulture).DisplayName;

            // "Niederländisch (Niederlande)" -> "Niederlande"
            var open = displayName.IndexOf('(');
            var close = displayName.LastIndexOf(')');
            if (open >= 0 && close > open)
                return displayName[(open + 1)..close];

            return new RegionInfo(regionCode).EnglishName;
        }
        catch (ArgumentException)
        {
            return null;
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    public string this[string key]
    {
        get
        {
            var lang = CurrentLanguage;
            var dict = GetDictionary(lang);
            if (dict.TryGetValue(key, out var val)) return val;

            if (lang != "en")
            {
                var enDict = GetDictionary("en");
                if (enDict.TryGetValue(key, out var enVal)) return enVal;
            }

            return key;
        }
    }

    public string Format(string key, params object[] args)
    {
        try { return string.Format(this[key], args); }
        catch { return this[key]; }
    }

    private Dictionary<string, string> GetDictionary(string lang)
    {
        return _cache.GetOrAdd(lang, code =>
        {
            var file = Path.Combine(_localizationPath, code + ".json");
            if (!File.Exists(file)) return new Dictionary<string, string>();
            try
            {
                var json = File.ReadAllText(file);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                       ?? new Dictionary<string, string>();
            }
            catch
            {
                return new Dictionary<string, string>();
            }
        });
    }
}
