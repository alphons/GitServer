using System.Security.Cryptography;
using System.Text;

namespace GitServer.Services;

public static class GravatarService
{
    public static string GetUrl(string? email, int size = 80, string defaultImage = "identicon")
    {
        var normalized = (email ?? "").Trim().ToLowerInvariant();
        var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        return $"https://www.gravatar.com/avatar/{hash}?s={size}&d={defaultImage}";
    }
}
