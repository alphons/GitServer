using System.Text.RegularExpressions;

namespace GitServer.Services;

public static class EmailBlocklist
{
    /// <summary>True if the email matches any of the given wildcard patterns ("*" = any run of
    /// characters, "?" = single character), case-insensitively.</summary>
    public static bool IsBlocked(string email, IEnumerable<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern)) continue;

            var regexPattern = "^" + Regex.Escape(pattern.Trim())
                .Replace(@"\*", ".*")
                .Replace(@"\?", ".") + "$";

            if (Regex.IsMatch(email, regexPattern, RegexOptions.IgnoreCase))
                return true;
        }

        return false;
    }
}
