using System.Collections.Concurrent;
using System.Text.Json;

namespace GitServer.Services;

public class LocalizationService(IHttpContextAccessor httpContextAccessor, IWebHostEnvironment env)
{
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private readonly string _localizationPath = Path.Combine(env.ContentRootPath, "Localization");

    private static readonly ConcurrentDictionary<string, Dictionary<string, string>> _cache = new();

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
