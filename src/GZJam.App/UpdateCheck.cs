using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GZJam.App;

/// <summary>A release of GZJam on GitHub: its version ("0.8.0") and its page.</summary>
public sealed record Release(string Version, string Url);

/// <summary>
/// Asks GitHub, once at startup, for the newest release, to say when there's a newer GZJam. One
/// request, sending nothing about the DJ or their music; nothing is downloaded or installed.
/// Turned off in Settings › About.
/// </summary>
internal static partial class UpdateCheck
{
    private const string LatestUrl = "https://api.github.com/repos/GemZombie/GZJam/releases/latest";

    /// <summary>A slow connection gives up rather than keep a request open all night.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The newest release, or null if GitHub can't be reached or says something unexpected.
    /// GitHub's "latest" leaves out drafts and pre-releases.
    /// </summary>
    public static async Task<Release?> LatestAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = Timeout };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GZJam", About.Version));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            string json = await http.GetStringAsync(LatestUrl).ConfigureAwait(false);
            return Parse(json);
        }
        catch (Exception ex)   // offline, slow, blocked: say nothing
        {
            Log.Info($"Couldn't ask GitHub for the newest GZJam: {ex.Message}");
            return null;
        }
    }

    /// <summary>A release from GitHub's answer; null for a draft, a pre-release or a tag that isn't a version.</summary>
    internal static Release? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (Flag(root, "draft") || Flag(root, "prerelease"))
                return null;
            string? tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            string? url = root.TryGetProperty("html_url", out var u) ? u.GetString() : null;
            if (tag is null || ReleaseVersion().Match(tag) is not { Success: true } match)
                return null;
            if (url is null || !url.StartsWith(About.ProjectUrl + "/", StringComparison.Ordinal))
                url = About.ProjectUrl + "/releases/latest";
            return new Release(match.Groups[1].Value, url);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    /// <summary>
    /// Whether <paramref name="release"/> ("0.8.0") is newer than <paramref name="current"/>. A
    /// build made here ("0.8.0-dev") is on its way to 0.8.0, so it's older than 0.8.0 itself; one
    /// that can't be read ("?") is never told about a new version.
    /// </summary>
    internal static bool IsNewer(string release, string current)
    {
        if (!Version.TryParse(release, out var released))
            return false;
        var match = CurrentVersion().Match(current);
        if (!match.Success)
            return false;
        var mine = Version.Parse(match.Groups[1].Value);
        bool preRelease = match.Groups[2].Success;
        return released > mine || (released == mine && preRelease);
    }

    [GeneratedRegex(@"^v(\d+\.\d+\.\d+)$")]
    private static partial Regex ReleaseVersion();

    [GeneratedRegex(@"^(\d+\.\d+\.\d+)(-.+)?$")]
    private static partial Regex CurrentVersion();
}
