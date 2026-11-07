using System.Net;
using System.Text;
using System.Text.Json;
using GZJam.Library.Servers;

namespace GZJam.Testing;

/// <summary>
/// A Navidrome with four songs, two albums and an artist, as user "gzjam" (any password; sign-in is
/// tested in the library tests' ServerTests). Each song's file is some bytes of its own.
/// </summary>
public sealed class FakeNavidrome : HttpMessageHandler
{
    private static readonly Dictionary<string, (string Title, string Artist, string Album, string AlbumId, int Track, string Path, string Suffix)> Songs = new()
    {
        ["s1"] = ("Basie Boogie", "Count Basie", "Cute", "al-cute", 3, "/music/Jazz & Swing/Count Basie/Cute/03 - Basie Boogie.mp3", "mp3"),
        ["s2"] = ("Air Mail Special", "Ella Fitzgerald", "Ella Swings", "al-ella", 7, "/music/Jazz & Swing/Ella Fitzgerald/Ella Swings/07 - Air Mail Special.flac", "flac"),
        ["s3"] = ("Lucky Rock Blues", "Ma Rainey", "Mother of the Blues", "al-ma", 0, "/music/Blues/Ma Rainey/Mother of the Blues/Lucky Rock Blues.mp3", "mp3"),
        ["s4"] = ("Li'l Darlin'", "Count Basie", "Cute", "al-cute", 5, "/music/Jazz & Swing/Count Basie/Cute/05 - Li'l Darlin'.mp3", "mp3"),
    };

    private static readonly Dictionary<string, (string Name, string Artist, int Year)> Albums = new()
    {
        ["al-cute"] = ("Cute", "Count Basie", 1958),
        ["al-ella"] = ("Ella Swings", "Ella Fitzgerald", 1961),
        ["al-ma"] = ("Mother of the Blues", "Ma Rainey", 1925),
    };

    private static readonly Dictionary<string, string> ArtistIds = new()
    {
        ["Count Basie"] = "ar-basie", ["Ella Fitzgerald"] = "ar-ella", ["Ma Rainey"] = "ar-ma",
    };

    private static string Query(Uri uri) => System.Web.HttpUtility.ParseQueryString(uri.Query)["query"] ?? "";

    private object Album(string id)
    {
        var a = Albums[id];
        int songs = Songs.Count(s => s.Value.AlbumId == id);
        return new { id, name = a.Name, artist = a.Artist, year = a.Year, songCount = songs, duration = 180 * songs };
    }

    public Dictionary<string, string[]> Playlists { get; } = new() { ["pl1"] = ["s2", "s1", "s3"] };
    public Dictionary<string, string> PlaylistNames { get; } = new() { ["pl1"] = "Friday" };
    public Dictionary<string, long> Sizes { get; } = [];
    public string[] StarredSongs { get; set; } = [];
    public string[] StarredAlbums { get; set; } = [];
    public string[] StarredArtists { get; set; } = [];
    public bool RealPaths { get; set; } = true;
    public bool Down { get; set; }

    /// <summary>A tiny real PNG: each album's picture.</summary>
    public static readonly byte[] Picture = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    /// <summary>The pictures asked for (getCoverArt): their ID, and the size asked for, if any.</summary>
    public List<(string Id, string? Size)> ArtAsked { get; } = [];

    /// <summary>Lists no songs at all (a server reading its music again).</summary>
    public bool Empty { get; set; }

    /// <summary>While set, downloads wait for it: to see how many come down at once, and in what order.</summary>
    public TaskCompletionSource? Hold { get; set; }

    private int _atOnce;

    /// <summary>The most downloads under way at once, while <see cref="Hold"/> held them.</summary>
    public int MostAtOnce { get; private set; }

    /// <summary>The songs whose downloads started, in order (while <see cref="Hold"/> is set).</summary>
    public List<string> Started { get; } = [];

    /// <summary>Downloads stopped by GZJam before they finished.</summary>
    public int Canceled { get; private set; }

    private async Task<HttpResponseMessage> HeldAsync(HttpRequestMessage request, TaskCompletionSource hold, CancellationToken cancellationToken)
    {
        string id = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["id"] ?? "";
        lock (Started)
        {
            Started.Add(id);
            MostAtOnce = Math.Max(MostAtOnce, ++_atOnce);
        }
        try
        {
            await hold.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            lock (Started)
                Canceled++;
            throw;
        }
        finally
        {
            lock (Started)
                _atOnce--;
        }
        Downloads++;
        var content = new ByteArrayContent(FileOf(id));
        content.Headers.ContentType = new("audio/mpeg");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }
    /// <summary>The stars asked for, in order: "star s1", "unstar s1" (#286).</summary>
    public List<string> Stars { get; } = [];

    public string? Truncate { get; set; }
    public int Downloads { get; private set; }

    /// <summary>How long a download takes to start: for a song that's still on its way.</summary>
    public TimeSpan DownloadDelay { get; set; }

    /// <summary>A song's file, in place of its made-up bytes: a real MP3, for previewing.</summary>
    public Dictionary<string, byte[]> Files { get; } = [];

    public static byte[] Bytes(string id) => Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat(id + "-audio-", 100)));

    private long SizeOf(string id) => Sizes.TryGetValue(id, out long size) ? size : FileOf(id).Length;

    private byte[] FileOf(string id) => Files.TryGetValue(id, out byte[]? file) ? file : Bytes(id);

    public SubsonicClient Client() => new(new HttpClient(this), "http://synology:4533", "gzjam", "sesame") { RetryAfter = TimeSpan.Zero };

    private object Song(string id)
    {
        var s = Songs[id];
        return new
        {
            id, title = s.Title, artist = s.Artist, album = s.Album, albumId = s.AlbumId, track = s.Track, suffix = s.Suffix,
            size = SizeOf(id), duration = 180, coverArt = "al-" + s.AlbumId,
            path = RealPaths ? s.Path : $"{s.Artist}/{s.Album}/{(s.Track > 0 ? $"{s.Track:00} - " : "")}{s.Title}.{s.Suffix}",
        };
    }

    private static string Ok(object body)
    {
        var response = new Dictionary<string, object> { ["status"] = "ok", ["version"] = "1.16.1", ["type"] = "navidrome" };
        foreach (var property in body.GetType().GetProperties())
            response[property.Name] = property.GetValue(body)!;
        return JsonSerializer.Serialize(new Dictionary<string, object> { ["subsonic-response"] = response });
    }

    private async Task<HttpResponseMessage> DelayedAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        TimeSpan delay = DownloadDelay;
        await Task.Delay(delay, cancellationToken);
        DownloadDelay = TimeSpan.Zero;
        try
        {
            return await SendAsync(request, cancellationToken);
        }
        finally
        {
            DownloadDelay = delay;
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Down)
            throw new HttpRequestException("No connection could be made.");
        Uri uri = request.RequestUri!;
        string id = System.Web.HttpUtility.ParseQueryString(uri.Query)["id"] ?? "";
        if (uri.AbsolutePath == "/rest/getCoverArt.view")
        {
            lock (ArtAsked)
                ArtAsked.Add((id, System.Web.HttpUtility.ParseQueryString(uri.Query)["size"]));
            var picture = new ByteArrayContent(Picture);
            picture.Headers.ContentType = new("image/png");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = picture });
        }
        if (uri.AbsolutePath is "/rest/star.view" or "/rest/unstar.view")
            lock (Stars)
                Stars.Add($"{uri.AbsolutePath[6..^5]} {id}");
        if (uri.AbsolutePath == "/rest/download.view" && Hold is { } hold)
            return HeldAsync(request, hold, cancellationToken);
        if (uri.AbsolutePath == "/rest/download.view" && DownloadDelay > TimeSpan.Zero)
            return DelayedAsync(request, cancellationToken);
        if (uri.AbsolutePath == "/rest/download.view")
        {
            Downloads++;
            byte[] bytes = FileOf(id);
            if (id == Truncate)
                bytes = bytes[..(bytes.Length / 2)];
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new("audio/mpeg");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
        string json = uri.AbsolutePath switch
        {
            "/rest/search3.view" => Ok(new
            {
                searchResult3 = new
                {
                    artist = Query(uri) is var q && "count basie".Contains(q, StringComparison.OrdinalIgnoreCase)
                        ? new[] { new { id = "ar-basie", name = "Count Basie", albumCount = 1 } } : [],
                    album = Albums.Where(a => a.Value.Name.Contains(Query(uri), StringComparison.OrdinalIgnoreCase) || a.Value.Artist.Contains(Query(uri), StringComparison.OrdinalIgnoreCase))
                        .Select(a => Album(a.Key)).ToArray(),
                    song = Songs.Where(s => !Empty && (s.Value.Title.Contains(Query(uri), StringComparison.OrdinalIgnoreCase) || s.Value.Artist.Contains(Query(uri), StringComparison.OrdinalIgnoreCase)))
                        .Select(s => Song(s.Key)).ToArray(),
                },
            }),
            "/rest/getAlbumList2.view" => Ok(new { albumList2 = new { album = Empty ? [] : Albums.Keys.Select(Album).ToArray() } }),
            "/rest/getSong.view" => Songs.ContainsKey(id) ? Ok(new { song = Song(id) })
                : "{\"subsonic-response\":{\"status\":\"failed\",\"version\":\"1.16.1\",\"error\":{\"code\":70,\"message\":\"Song not found\"}}}",
            "/rest/getPlaylists.view" => Ok(new
            {
                playlists = new
                {
                    playlist = Playlists.Select(p => new { id = p.Key, name = PlaylistNames.GetValueOrDefault(p.Key, p.Key), songCount = p.Value.Length, duration = 180 * p.Value.Length, owner = "gzjam" }).ToArray(),
                },
            }),
            "/rest/getPlaylist.view" => Ok(new { playlist = new { id, entry = Playlists[id].Select(Song).ToArray() } }),
            "/rest/getStarred2.view" => Ok(new
            {
                starred2 = new
                {
                    song = StarredSongs.Select(Song).ToArray(),
                    album = StarredAlbums.Select(a => new { id = a, name = a }).ToArray(),
                    artist = StarredArtists.Select(a => new { id = a, name = a }).ToArray(),
                },
            }),
            "/rest/getAlbum.view" => Ok(new { album = new { id, song = Songs.Where(s => s.Value.AlbumId == id).Select(s => Song(s.Key)).ToArray() } }),
            "/rest/getArtist.view" => Ok(new { artist = new { id, album = Albums.Where(a => ArtistIds.GetValueOrDefault(a.Value.Artist) == id).Select(a => Album(a.Key)).ToArray() } }),
            _ => Ok(new { }),
        };
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }
}
