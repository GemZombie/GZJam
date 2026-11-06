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

    public Dictionary<string, string[]> Playlists { get; } = new() { ["pl1"] = ["s2", "s1", "s3"] };
    public Dictionary<string, string> PlaylistNames { get; } = new() { ["pl1"] = "Friday" };
    public Dictionary<string, long> Sizes { get; } = [];
    public string[] StarredSongs { get; set; } = [];
    public string[] StarredAlbums { get; set; } = [];
    public string[] StarredArtists { get; set; } = [];
    public bool RealPaths { get; set; } = true;
    public bool Down { get; set; }
    public string? Truncate { get; set; }
    public int Downloads { get; private set; }

    public static byte[] Bytes(string id) => Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat(id + "-audio-", 100)));

    private long SizeOf(string id) => Sizes.TryGetValue(id, out long size) ? size : Bytes(id).Length;

    public SubsonicClient Client() => new(new HttpClient(this), "http://synology:4533", "gzjam", "sesame");

    private object Song(string id)
    {
        var s = Songs[id];
        return new
        {
            id, title = s.Title, artist = s.Artist, album = s.Album, albumId = s.AlbumId, track = s.Track, suffix = s.Suffix,
            size = SizeOf(id), duration = 180,
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

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Down)
            throw new HttpRequestException("No connection could be made.");
        Uri uri = request.RequestUri!;
        string id = System.Web.HttpUtility.ParseQueryString(uri.Query)["id"] ?? "";
        if (uri.AbsolutePath == "/rest/download.view")
        {
            Downloads++;
            byte[] bytes = Bytes(id);
            if (id == Truncate)
                bytes = bytes[..(bytes.Length / 2)];
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new("audio/mpeg");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
        string json = uri.AbsolutePath switch
        {
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
            "/rest/getArtist.view" => Ok(new { artist = new { id, album = id == "ar-ella" ? new[] { new { id = "al-ella" } } : [] } }),
            _ => Ok(new { }),
        };
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }
}
