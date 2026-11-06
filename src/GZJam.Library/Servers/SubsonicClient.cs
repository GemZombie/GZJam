using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GZJam.Library.Servers;

/// <summary>
/// Talks to a music server through the Subsonic API (#184), which Navidrome, Gonic,
/// Airsonic-Advanced, Ampache and others speak (OpenSubsonic is its newer, extended form). Plain
/// HTTP requests with JSON answers: <c>/rest/ping.view?u=…&amp;t=…&amp;s=…&amp;v=…&amp;c=…&amp;f=json</c>.
/// Signs in with a salted token (an MD5 of the password and a fresh salt), so the password itself
/// is never sent; only a server that can't check tokens (one that signs users in through LDAP) is
/// sent it, hex-encoded as the API asks.
/// </summary>
public sealed class SubsonicClient
{
    /// <summary>The API version GZJam speaks: Subsonic 6.1's, which every current server answers.</summary>
    public const string ApiVersion = "1.16.1";

    /// <summary>How GZJam names itself to the server (Navidrome lists it under Players).</summary>
    public const string ClientName = "GZJam";

    private readonly HttpClient _http;
    private readonly string _password;
    private bool _sendPassword;   // the server can't check tokens (error 41)

    public SubsonicClient(HttpClient http, string address, string userName, string password)
    {
        _http = http;
        Address = NormalizeAddress(address) ?? throw new ArgumentException($"Not a server address: {address}", nameof(address));
        UserName = userName;
        _password = password;
    }

    public string Address { get; }
    public string UserName { get; }

    /// <summary>
    /// The address as GZJam keeps it, from what the DJ typed or pasted: "synology:4533" gets
    /// http://, and a page of the server's web player (Navidrome's <c>/app/#/album/…</c>) or its API
    /// (<c>/rest</c>) becomes the server's own address. Null if it isn't a web address at all.
    /// </summary>
    public static string? NormalizeAddress(string? typed)
    {
        string text = (typed ?? "").Trim();
        if (text.Length == 0 || text.Any(char.IsWhiteSpace))
            return null;
        if (!text.Contains("://", StringComparison.Ordinal))
            text = "http://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https") || uri.Host.Length == 0)
            return null;

        string path = uri.AbsolutePath.TrimEnd('/');
        foreach (string page in (string[])["/app", "/rest"])
        {
            int at = FindSegment(path, page);
            if (at >= 0)
                path = path[..at];
        }
        string port = uri.IsDefaultPort ? "" : ":" + uri.Port.ToString(CultureInfo.InvariantCulture);
        return $"{uri.Scheme}://{uri.IdnHost}{port}{path}";
    }

    /// <summary>Where <paramref name="segment"/> ("/app") starts as a whole path segment of <paramref name="path"/>, or -1.</summary>
    private static int FindSegment(string path, string segment)
    {
        for (int at = path.IndexOf(segment, StringComparison.OrdinalIgnoreCase); at >= 0;
             at = path.IndexOf(segment, at + 1, StringComparison.OrdinalIgnoreCase))
        {
            int end = at + segment.Length;
            if (end == path.Length || path[end] == '/')
                return at;
        }
        return -1;
    }

    /// <summary>
    /// Signs in and finds out what the server is, whether this account may download, and how many
    /// songs it has: what Settings › Servers shows. Never throws for a server that's down or wrong;
    /// the outcome says what happened.
    /// </summary>
    public async Task<ServerCheck> CheckAsync(CancellationToken cancel = default)
    {
        Answer ping = await CallAsync("ping", [], cancel).ConfigureAwait(false);
        if (ping.Error == 41 && !_sendPassword)
        {
            _sendPassword = true;   // tokens can't be checked here: sign in as the API's older way asks
            ping = await CallAsync("ping", [], cancel).ConfigureAwait(false);
        }
        if (ping.Outcome is { } failed)
            return new ServerCheck(failed, ping.ServerType, ping.ServerVersion, ping.OpenSubsonic, Detail: ping.Message);
        if (ping.Error is { } code)
            return new ServerCheck(code is 40 or 41 or 42 or 43 or 44 ? ServerCheckOutcome.WrongSignIn : ServerCheckOutcome.Failed,
                ping.ServerType, ping.ServerVersion, ping.OpenSubsonic, Detail: ping.Message);

        bool? canDownload = null;
        Answer user = await CallAsync("getUser", [("username", UserName)], cancel).ConfigureAwait(false);
        if (user.Body is { } userBody && userBody.TryGetProperty("user", out JsonElement u)
            && u.TryGetProperty("downloadRole", out JsonElement role) && role.ValueKind is JsonValueKind.True or JsonValueKind.False)
            canDownload = role.GetBoolean();

        int? songs = null;
        Answer scan = await CallAsync("getScanStatus", [], cancel).ConfigureAwait(false);
        if (scan.Body is { } scanBody && scanBody.TryGetProperty("scanStatus", out JsonElement status)
            && status.TryGetProperty("count", out JsonElement count) && count.TryGetInt32(out int n))
            songs = n;

        return new ServerCheck(ServerCheckOutcome.Connected, ping.ServerType, ping.ServerVersion, ping.OpenSubsonic, canDownload, songs);
    }

    /// <summary>The address of an API call, signed in: a fresh salt and token each time.</summary>
    internal Uri UriFor(string endpoint, IEnumerable<(string Name, string Value)> args)
    {
        var query = new StringBuilder();
        void Add(string name, string value) =>
            query.Append(query.Length == 0 ? '?' : '&').Append(name).Append('=').Append(Uri.EscapeDataString(value));

        Add("u", UserName);
        if (_sendPassword)
            Add("p", "enc:" + Convert.ToHexStringLower(Encoding.UTF8.GetBytes(_password)));
        else
        {
            string salt = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
            Add("t", Token(_password, salt));
            Add("s", salt);
        }
        Add("v", ApiVersion);
        Add("c", ClientName);
        Add("f", "json");
        foreach ((string name, string value) in args)
            Add(name, value);
        return new Uri($"{Address}/rest/{endpoint}.view{query}");
    }

    /// <summary>The Subsonic sign-in token: the MD5 of the password followed by the salt, in lowercase hex.</summary>
    internal static string Token(string password, string salt) =>
        Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(password + salt)));

    /// <summary>
    /// One call's answer. <see cref="Outcome"/> is set when there was no proper answer at all
    /// (nothing reached, or not a Subsonic server); <see cref="Error"/> when the server said no.
    /// </summary>
    private sealed record Answer(
        ServerCheckOutcome? Outcome = null,
        int? Error = null,
        string? Message = null,
        JsonElement? Body = null,
        string? ServerType = null,
        string? ServerVersion = null,
        bool OpenSubsonic = false);

    private async Task<Answer> CallAsync(string endpoint, IEnumerable<(string, string)> args, CancellationToken cancel)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(UriFor(endpoint, args), HttpCompletionOption.ResponseContentRead, cancel).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            return new Answer(ServerCheckOutcome.Unreachable, Message: e.Message);
        }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested)
        {
            return new Answer(ServerCheckOutcome.Unreachable, Message: "No answer in time.");
        }

        using (response)
        {
            string text = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
            JsonElement root;
            try
            {
                using var json = JsonDocument.Parse(text);
                if (!json.RootElement.TryGetProperty("subsonic-response", out JsonElement found))
                    return NotSubsonic(response.StatusCode);
                root = found.Clone();
            }
            catch (JsonException)
            {
                return NotSubsonic(response.StatusCode);
            }

            string? type = root.TryGetProperty("type", out JsonElement t) ? t.GetString() : null;
            string? version = root.TryGetProperty("serverVersion", out JsonElement v) ? v.GetString() : null;
            if (version is not null && version.IndexOf(" (", StringComparison.Ordinal) is int paren and > 0)
                version = version[..paren];   // Navidrome: "0.64.2 (10114574)", the build in brackets
            bool open = root.TryGetProperty("openSubsonic", out JsonElement o) && o.ValueKind == JsonValueKind.True;

            if (root.TryGetProperty("status", out JsonElement s) && s.GetString() == "ok")
                return new Answer(Body: root, ServerType: type, ServerVersion: version, OpenSubsonic: open);

            int code = 0;
            string? message = null;
            if (root.TryGetProperty("error", out JsonElement error))
            {
                if (error.TryGetProperty("code", out JsonElement c) && c.TryGetInt32(out int n))
                    code = n;
                message = error.TryGetProperty("message", out JsonElement m) ? m.GetString() : null;
            }
            return new Answer(Error: code, Message: message, ServerType: type, ServerVersion: version, OpenSubsonic: open);
        }
    }

    private static Answer NotSubsonic(HttpStatusCode status) =>
        new(ServerCheckOutcome.NotSubsonic, Message: $"The answer wasn't from a Subsonic server (HTTP {(int)status}).");
}
