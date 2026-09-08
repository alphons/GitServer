using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace GitServer.Services;

public record GitRelease(string TagName, string Version, bool Prerelease, DateTime PublishedAt, string? AssetUrl, long AssetSize);

/// <summary>Queries the git-for-windows/git GitHub releases for the portable MinGit distribution,
/// used by the admin git-updater feature to check for and download new versions.</summary>
public class GitReleaseService(IHttpClientFactory httpClientFactory, IOptions<GitServerOptions> options, ILogger<GitReleaseService> logger)
{
    private string ReleasesUrl => options.Value.GitReleasesApiUrl.TrimEnd('/');
    private Regex AssetPattern => new(options.Value.GitReleaseAssetPattern, RegexOptions.Compiled);

    private HttpClient CreateClient() => httpClientFactory.CreateClient("GitHubReleases");

    public async Task<GitRelease?> GetLatestReleaseAsync(CancellationToken ct = default)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync($"{ReleasesUrl}/latest", ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("GitHub releases/latest returned {status}", response.StatusCode);
            return null;
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return ParseRelease(doc.RootElement, AssetPattern);
    }

    public async Task<List<GitRelease>> ListReleasesAsync(int count, CancellationToken ct = default)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync($"{ReleasesUrl}?per_page={count}", ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("GitHub releases listing returned {status}", response.StatusCode);
            return new List<GitRelease>();
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var pattern = AssetPattern;
        var releases = new List<GitRelease>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var release = ParseRelease(element, pattern);
            if (release != null) releases.Add(release);
        }
        return releases;
    }

    private static GitRelease? ParseRelease(JsonElement element, Regex assetPattern)
    {
        var tagName = element.GetProperty("tag_name").GetString() ?? "";
        var prerelease = element.TryGetProperty("prerelease", out var p) && p.GetBoolean();
        var publishedAt = element.TryGetProperty("published_at", out var d) && d.TryGetDateTime(out var dt) ? dt : DateTime.MinValue;

        string? assetUrl = null;
        long assetSize = 0;
        if (element.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (!assetPattern.IsMatch(name)) continue;

                assetUrl = asset.GetProperty("browser_download_url").GetString();
                assetSize = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                break;
            }
        }

        if (assetUrl is null) return null;

        var version = ExtractVersion(tagName);
        return new GitRelease(tagName, version, prerelease, publishedAt, assetUrl, assetSize);
    }

    // "v2.55.0.windows.1" -> "2.55.0.1"; falls back to the raw tag if the pattern doesn't match.
    private static string ExtractVersion(string tagName)
    {
        var match = Regex.Match(tagName, @"v?(\d+\.\d+\.\d+)(?:\.windows\.(\d+))?");
        if (!match.Success) return tagName;
        return match.Groups[2].Success ? $"{match.Groups[1].Value}.{match.Groups[2].Value}" : match.Groups[1].Value;
    }
}
