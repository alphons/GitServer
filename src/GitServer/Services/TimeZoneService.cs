using System.Collections.Concurrent;
using System.Globalization;

namespace GitServer.Services;

public class TimeZoneService(IHttpContextAccessor httpContextAccessor, LocalizationService localization)
{
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private readonly LocalizationService _localization = localization;

    public const string CookieName = "tz";

    private static readonly ConcurrentDictionary<string, List<(string Id, string DisplayName)>> _zoneCache = new();

    public string CurrentTimeZoneId
    {
        get
        {
            var ctx = _httpContextAccessor.HttpContext;
            if (ctx?.Request.Cookies.TryGetValue(CookieName, out var tz) == true && !string.IsNullOrEmpty(tz))
                return tz;
            return "UTC";
        }
    }

    private TimeZoneInfo CurrentTimeZone
    {
        get
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(CurrentTimeZoneId); }
            catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
            catch (InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
        }
    }

    /// <summary>Converts a UTC DateTime to the current user's local time zone.</summary>
    public DateTime ToLocal(DateTime utc)
    {
        var utcKind = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utcKind, CurrentTimeZone);
    }

    public IEnumerable<(string Id, string DisplayName)> AvailableTimeZones() =>
        _zoneCache.GetOrAdd(_localization.CurrentLanguage, lang =>
        {
            CultureInfo culture;
            try { culture = CultureInfo.GetCultureInfo(lang); }
            catch (CultureNotFoundException) { culture = CultureInfo.GetCultureInfo("en"); }

            var original = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = culture;
                return TimeZoneInfo.GetSystemTimeZones()
                    .OrderBy(tz => tz.BaseUtcOffset)
                    .Select(tz => (tz.Id, $"{FormatOffset(tz.BaseUtcOffset)} {tz.DisplayName}"))
                    .ToList();
            }
            finally
            {
                CultureInfo.CurrentUICulture = original;
            }
        });

    private static string FormatOffset(TimeSpan offset) =>
        $"(UTC{(offset < TimeSpan.Zero ? "-" : "+")}{offset:hh\\:mm})";
}
