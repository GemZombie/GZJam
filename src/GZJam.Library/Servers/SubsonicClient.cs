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

    // ───────────── What's on the server (#283) ─────────────

    /// <summary>What the server says of its own reading of its music: see <see cref="ServerScan"/>.</summary>
    public async Task<ServerScan> GetScanStatusAsync(CancellationToken cancel = default)
    {
        JsonElement body = await GetAsync("getScanStatus", [], cancel).ConfigureAwait(false);
        if (!body.TryGetProperty("scanStatus", out JsonElement status))
            return new ServerScan(false, null, null);
        bool scanning = status.TryGetProperty("scanning", out JsonElement s) && s.ValueKind == JsonValueKind.True;
        int? count = status.TryGetProperty("count", out JsonElement c) && c.TryGetInt32(out int n) ? n : null;
        string? last = status.TryGetProperty("lastScan", out JsonElement l) && l.ValueKind == JsonValueKind.String ? l.GetString() : null;
        return new ServerScan(scanning, count, last);
    }

    /// <summary>The server's playlists that this account can see: its own and public ones.</summary>
    public async Task<IReadOnlyList<ServerPlaylist>> GetPlaylistsAsync(CancellationToken cancel = default)
    {
        JsonElement body = await GetAsync("getPlaylists", [], cancel).ConfigureAwait(false);
        var playlists = new List<ServerPlaylist>();
        if (body.TryGetProperty("playlists", out JsonElement list))
            foreach (JsonElement p in Items(list, "playlist"))
                playlists.Add(new ServerPlaylist(Text(p, "id") ?? "", Text(p, "name") ?? "", Int(p, "songCount") ?? 0,
                    TimeSpan.FromSeconds(Int(p, "duration") ?? 0), Text(p, "owner")));
        return playlists;
    }

    /// <summary>A playlist's songs, in its order.</summary>
    public async Task<IReadOnlyList<ServerSong>> GetPlaylistSongsAsync(string playlistId, CancellationToken cancel = default)
    {
        JsonElement body = await GetAsync("getPlaylist", [("id", playlistId)], cancel).ConfigureAwait(false);
        return body.TryGetProperty("playlist", out JsonElement playlist) ? Songs(Items(playlist, "entry")) : [];
    }

    /// <summary>What this account starred: songs, whole albums and artists.</summary>
    public async Task<ServerStarred> GetStarredAsync(CancellationToken cancel = default)
    {
        JsonElement body = await GetAsync("getStarred2", [], cancel).ConfigureAwait(false);
        if (!body.TryGetProperty("starred2", out JsonElement starred))
            return new ServerStarred([], [], []);
        return new ServerStarred(
            Songs(Items(starred, "song")),
            Items(starred, "album").Select(a => (Text(a, "id") ?? "", Text(a, "name") ?? Text(a, "title") ?? "")).ToList(),
            Items(starred, "artist").Select(a => (Text(a, "id") ?? "", Text(a, "name") ?? "")).ToList());
    }

    /// <summary>An album's songs, in order.</summary>
    public async Task<IReadOnlyList<ServerSong>> GetAlbumSongsAsync(string albumId, CancellationToken cancel = default)
    {
        JsonElement body = await GetAsync("getAlbum", [("id", albumId)], cancel).ConfigureAwait(false);
        return body.TryGetProperty("album", out JsonElement album) ? Songs(Items(album, "song")) : [];
    }

    /// <summary>The IDs of an artist's albums.</summary>
    public async Task<IReadOnlyList<string>> GetArtistAlbumsAsync(string artistId, CancellationToken cancel = default)
    {
        JsonElement body = await GetAsync("getArtist", [("id", artistId)], cancel).ConfigureAwait(false);
        return body.TryGetProperty("artist", out JsonElement artist)
            ? Items(artist, "album").Select(a => Text(a, "id")).OfType<string>().ToList()
            : [];
    }

    /// <summary>
    /// Downloads a song's original file (the API's <c>download</c>, never converted) into
    /// <paramref name="destination"/>, reporting the bytes so far. Gives up if the server sends
    /// nothing for <paramref name="stall"/> (30 seconds unless said), rather than waiting forever on
    /// a connection that has quietly gone.
    /// </summary>
    public async Task DownloadAsync(string songId, Stream destination, IProgress<long>? progress = null, TimeSpan? stall = null,
        CancellationToken cancel = default)
    {
        TimeSpan quiet = stall ?? TimeSpan.FromSeconds(30);
        using var watchdog = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        watchdog.CancelAfter(quiet);
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(UriFor("download", [("id", songId)]), HttpCompletionOption.ResponseHeadersRead, watchdog.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new ServerException("The server stopped answering.", outcome: ServerCheckOutcome.Unreachable);
        }
        catch (HttpRequestException e)
        {
            throw new ServerException(e.Message, e, ServerCheckOutcome.Unreachable);
        }
        using (response)
        {
            string? type = response.Content.Headers.ContentType?.MediaType;
            if (!response.IsSuccessStatusCode || type is "application/json" or "text/xml" or "application/xml" or "text/html")
            {
                // A failed download is answered with an error, not the file.
                string text = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
                throw new ServerException(ErrorIn(text) ?? $"The server said no (HTTP {(int)response.StatusCode}).");
            }
            await using Stream source = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
            byte[] buffer = new byte[81920];
            long total = 0;
            try
            {
                while (true)
                {
                    watchdog.CancelAfter(quiet);
                    int read = await source.ReadAsync(buffer, watchdog.Token).ConfigureAwait(false);
                    if (read == 0)
                        break;
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
                    total += read;
                    progress?.Report(total);
                }
            }
            catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
            {
                throw new ServerException("The server stopped sending the song.", outcome: ServerCheckOutcome.Unreachable);
            }
            catch (HttpRequestException e)
            {
                throw new ServerException(e.Message, e, ServerCheckOutcome.Unreachable);
            }
        }
    }

    /// <summary>
    /// A song's or album's picture (the API's <c>getCoverArt</c>, by the <see cref="ServerSong.CoverArt"/>
    /// the server gave it): the server's choice of a cover.jpg in its folder, the picture in the
    /// file, or one from the internet. <paramref name="size"/>: a square that size, made by the
    /// server; null for the picture as it is. Null if the server has none.
    /// </summary>
    public async Task<byte[]?> GetCoverArtAsync(string coverArt, int? size = null, CancellationToken cancel = default)
    {
        (string, string)[] query = size is { } px
            ? [("id", coverArt), ("size", px.ToString(CultureInfo.InvariantCulture))]
            : [("id", coverArt)];
        try
        {
            using var response = await _http.GetAsync(UriFor("getCoverArt", query), cancel).ConfigureAwait(false);
            string? type = response.Content.Headers.ContentType?.MediaType;
            if (!response.IsSuccessStatusCode || type is null || !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return null;   // none, or an error answered in words
            return await response.Content.ReadAsByteArrayAsync(cancel).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new ServerException("The server stopped answering.", outcome: ServerCheckOutcome.Unreachable);
        }
        catch (HttpRequestException e)
        {
            throw new ServerException(e.Message, e, ServerCheckOutcome.Unreachable);
        }
    }

    /// <summary>A Subsonic error's message, from an error answer's text; null if it isn't one.</summary>
    private static string? ErrorIn(string text)
    {
        try
        {
            using var json = JsonDocument.Parse(text);
            return json.RootElement.TryGetProperty("subsonic-response", out JsonElement root)
                && root.TryGetProperty("error", out JsonElement error) && error.TryGetProperty("message", out JsonElement m)
                ? m.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>An API call that must work: its answer, or a <see cref="ServerException"/> saying why not.</summary>
    private async Task<JsonElement> GetAsync(string endpoint, IEnumerable<(string, string)> args, CancellationToken cancel)
    {
        Answer answer = await CallAsync(endpoint, args, cancel).ConfigureAwait(false);
        if (answer.Error == 41 && !_sendPassword)
        {
            _sendPassword = true;
            answer = await CallAsync(endpoint, args, cancel).ConfigureAwait(false);
        }
        if (answer.Body is { } body)
            return body;
        ServerCheckOutcome outcome = answer.Outcome
            ?? (answer.Error is 40 or 41 or 42 or 43 or 44 ? ServerCheckOutcome.WrongSignIn : ServerCheckOutcome.Failed);
        throw new ServerException(answer.Message ?? (outcome == ServerCheckOutcome.Unreachable
            ? "The server can't be reached."
            : "The server said no."), outcome: outcome);
    }

    /// <summary>A list in an answer: the API gives one item as an object, several as an array, none as nothing.</summary>
    private static IEnumerable<JsonElement> Items(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out JsonElement value))
            yield break;
        if (value.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in value.EnumerateArray())
                yield return item;
        else if (value.ValueKind == JsonValueKind.Object)
            yield return value;
    }

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        } : null;

    private static long? Long(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n) ? n : null;

    private static int? Int(JsonElement e, string name) => Long(e, name) is { } n ? (int)Math.Clamp(n, int.MinValue, int.MaxValue) : null;

    private static List<ServerSong> Songs(IEnumerable<JsonElement> items) =>
        items.Where(s => !(s.TryGetProperty("isDir", out JsonElement dir) && dir.ValueKind == JsonValueKind.True))
            .Select(s => new ServerSong(
                Text(s, "id") ?? "",
                Text(s, "title"),
                Text(s, "artist"),
                Text(s, "album"),
                Text(s, "displayAlbumArtist") ?? Text(s, "albumArtist"),
                Int(s, "track"),
                Int(s, "discNumber"),
                Int(s, "year"),
                Text(s, "path"),
                Text(s, "suffix"),
                Long(s, "size"),
                TimeSpan.FromSeconds(Int(s, "duration") ?? 0),
                Text(s, "genre"),
                Int(s, "bpm") is { } bpm and > 0 ? bpm : null,
                Text(s, "albumId"),
                Text(s, "coverArt")))
            .Where(s => s.Id.Length > 0)
            .ToList();

    // ───────────── Browsing the server (#284) ─────────────

    /// <summary>Songs, albums and artists matching <paramref name="query"/>, as the server finds them.</summary>
    public async Task<ServerSearch> SearchAsync(string query, int songs = 60, int albums = 20, int artists = 10, CancellationToken cancel = default,
        int songOffset = 0)
    {
        JsonElement body = await GetAsync("search3",
            [("query", query), ("songCount", songs.ToString(CultureInfo.InvariantCulture)),
             ("songOffset", songOffset.ToString(CultureInfo.InvariantCulture)),
             ("albumCount", albums.ToString(CultureInfo.InvariantCulture)), ("artistCount", artists.ToString(CultureInfo.InvariantCulture))],
            cancel).ConfigureAwait(false);
        if (!body.TryGetProperty("searchResult3", out JsonElement found))
            return new ServerSearch([], [], []);
        return new ServerSearch(Songs(Items(found, "song")), Albums(Items(found, "album")),
            Items(found, "artist").Select(a => new ServerArtist(Text(a, "id") ?? "", Text(a, "name") ?? "", Int(a, "albumCount") ?? 0))
                .Where(a => a.Id.Length > 0).ToList());
    }

    /// <summary>Albums by <paramref name="type"/>: "newest" (recently added), "recent" (played), "frequent", "alphabeticalByName"…</summary>
    public async Task<IReadOnlyList<ServerAlbum>> GetAlbumListAsync(string type, int size = 40, int offset = 0, CancellationToken cancel = default)
    {
        JsonElement body = await GetAsync("getAlbumList2",
            [("type", type), ("size", size.ToString(CultureInfo.InvariantCulture)), ("offset", offset.ToString(CultureInfo.InvariantCulture))],
            cancel).ConfigureAwait(false);
        return body.TryGetProperty("albumList2", out JsonElement list) ? Albums(Items(list, "album")) : [];
    }

    /// <summary>An artist's albums.</summary>
    public async Task<IReadOnlyList<ServerAlbum>> GetArtistAsync(string artistId, CancellationToken cancel = default)
    {
        JsonElement body = await GetAsync("getArtist", [("id", artistId)], cancel).ConfigureAwait(false);
        return body.TryGetProperty("artist", out JsonElement artist) ? Albums(Items(artist, "album")) : [];
    }

    /// <summary>Stars a song for this account (a ♥ in GZJam, #286), or takes its star off.</summary>
    public async Task StarAsync(string songId, bool starred, CancellationToken cancel = default) =>
        await GetAsync(starred ? "star" : "unstar", [("id", songId)], cancel).ConfigureAwait(false);

    /// <summary>How many songs go in one call: they go in the address, which a proxy may refuse if it's long.</summary>
    private const int SongsPerCall = 100;

    /// <summary>
    /// Makes a playlist of these songs, in this order (#303), or replaces the songs of the one with
    /// <paramref name="playlistId"/>, and gives its name. Its ID on the server.
    /// </summary>
    public async Task<string> SendPlaylistAsync(string? playlistId, string name, IReadOnlyList<string> songIds, CancellationToken cancel = default)
    {
        (string, string) target = playlistId is null ? ("name", name) : ("playlistId", playlistId);
        JsonElement body = await GetAsync("createPlaylist",
            [target, .. songIds.Take(SongsPerCall).Select(id => ("songId", id))], cancel).ConfigureAwait(false);
        string? made = playlistId ?? (body.TryGetProperty("playlist", out JsonElement playlist) ? Text(playlist, "id") : null);
        // Before Subsonic 1.14 the answer doesn't say: it's the newest of that name.
        made ??= (await GetPlaylistsAsync(cancel).ConfigureAwait(false)).LastOrDefault(p => p.Name == name)?.Id
            ?? throw new ServerException("The server didn't say what it made.", outcome: ServerCheckOutcome.Failed);
        if (playlistId is not null)
            await GetAsync("updatePlaylist", [("playlistId", made), ("name", name)], cancel).ConfigureAwait(false);
        foreach (string[] part in songIds.Skip(SongsPerCall).Chunk(SongsPerCall))
            await GetAsync("updatePlaylist", [("playlistId", made), .. part.Select(id => ("songIdToAdd", id))], cancel).ConfigureAwait(false);
        return made;
    }

    /// <summary>A link anyone can open to listen to a playlist (OpenSubsonic's createShare, #303).</summary>
    public async Task<string> CreateShareAsync(string playlistId, string? description, CancellationToken cancel = default)
    {
        JsonElement body = await GetAsync("createShare",
            string.IsNullOrWhiteSpace(description) ? [("id", playlistId)] : [("id", playlistId), ("description", description.Trim())], cancel).ConfigureAwait(false);
        string? link = body.TryGetProperty("shares", out JsonElement shares)
            ? Items(shares, "share").Select(s => Text(s, "url")).FirstOrDefault(u => !string.IsNullOrEmpty(u))
            : null;
        return link ?? throw new ServerException("The server didn't give a link.", outcome: ServerCheckOutcome.Failed);
    }

    /// <summary>One song, by its ID; null if the server no longer has it.</summary>
    public async Task<ServerSong?> GetSongAsync(string songId, CancellationToken cancel = default)
    {
        try
        {
            JsonElement body = await GetAsync("getSong", [("id", songId)], cancel).ConfigureAwait(false);
            return body.TryGetProperty("song", out JsonElement song) ? Songs([song]).FirstOrDefault() : null;
        }
        catch (ServerException e) when (e.Outcome == ServerCheckOutcome.Failed)
        {
            return null;   // error 70: not found
        }
    }

    private static List<ServerAlbum> Albums(IEnumerable<JsonElement> items) =>
        items.Select(a => new ServerAlbum(
                Text(a, "id") ?? "",
                Text(a, "name") ?? Text(a, "title") ?? Text(a, "album") ?? "",
                Text(a, "displayArtist") ?? Text(a, "artist"),
                Int(a, "year"),
                Int(a, "songCount") ?? 0,
                TimeSpan.FromSeconds(Int(a, "duration") ?? 0)))
            .Where(a => a.Id.Length > 0)
            .ToList();

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

    /// <summary>How long to wait before asking again when a connection couldn't be made (Tailscale waking a route up, say).</summary>
    public TimeSpan RetryAfter { get; init; } = TimeSpan.FromSeconds(1);

    private async Task<Answer> CallAsync(string endpoint, IEnumerable<(string, string)> args, CancellationToken cancel)
    {
        var list = args.ToList();
        HttpResponseMessage response;
        try
        {
            try
            {
                response = await _http.GetAsync(UriFor(endpoint, list), HttpCompletionOption.ResponseContentRead, cancel).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                // Once more, after a moment: a first connection after a quiet spell can fail.
                await Task.Delay(RetryAfter, cancel).ConfigureAwait(false);
                response = await _http.GetAsync(UriFor(endpoint, list), HttpCompletionOption.ResponseContentRead, cancel).ConfigureAwait(false);
            }
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
