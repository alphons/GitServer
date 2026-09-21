using System.Collections.Concurrent;
using System.Globalization;

namespace GitServer.Services;

public class TimeZoneService(IHttpContextAccessor httpContextAccessor, LocalizationService localization)
{
	public const string CookieName = "tz";

	private static readonly ConcurrentDictionary<string, List<(string Id, string DisplayName)>> ZoneCache = new();

	public string CurrentTimeZoneId
	{
		get
		{
			var ctx = httpContextAccessor.HttpContext;
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

	// The one place that decides how a moment in time is shown to people: in the caller's time zone and language.

	/// <summary>"21 Sep 2026 14:05".</summary>
	public string FormatDateTime(DateTime utc) => ToLocal(utc).ToString("d MMM yyyy HH:mm", localization.CurrentCulture);

	/// <summary>"21 Sep 2026".</summary>
	public string FormatDate(DateTime utc) => ToLocal(utc).ToString("d MMM yyyy", localization.CurrentCulture);

	/// <summary>"September 2026".</summary>
	public string FormatMonth(DateTime utc) => ToLocal(utc).ToString("MMMM yyyy", localization.CurrentCulture);

	/// <summary><see cref="FormatDateTime(DateTime)"/>, or null when there is no value.</summary>
	public string? FormatDateTime(DateTime? utc) => utc.HasValue ? FormatDateTime(utc.Value) : null;

	public IEnumerable<(string Id, string DisplayName)> AvailableTimeZones() =>
		ZoneCache.GetOrAdd(localization.CurrentLanguage, lang =>
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
