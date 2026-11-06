using System.Net;
using System.Text;
using System.Text.Json;
using GZJam.Library.Servers;
using GZJam.Testing;

namespace GZJam.Library.Tests;

/// <summary>Bringing a server's chosen songs onto the laptop (#283): where they go, what isn't downloaded twice, and the playlists they fill.</summary>
public sealed class ServerSyncTests : IDisposable
{
    private readonly TempLibrary _lib = new();
    private readonly FakeNavidrome _server = new();
    private readonly MusicServer _record;

    public ServerSyncTests()
    {
        _record = _lib.Database.AddServer("synology", "http://synology:4533", "gzjam");
        Downloads = Path.Combine(_lib.Directory, "GZJam from servers");
    }

    public void Dispose() => _lib.Dispose();

    private string Downloads { get; }

    private string Here(params string[] parts) => Path.Combine([Downloads, "synology", .. parts]);

    private ServerSync Sync(long? limit = null, Func<string?>? stopBefore = null) =>
        new(_lib.Database, _server.Client(), _record, Downloads) { Limit = limit, StopBefore = stopBefore, KeepFree = 0 };

    private void Choose(params ServerChoice[] choices) => _lib.Database.SetServerChoices(_record.Id, choices);

    private static ServerChoice Playlist(string id, string name) => new(ServerChoiceKind.Playlist, id, name);

    private IReadOnlyList<string> PlaylistPaths(string name) =>
        _lib.Database.GetPlaylistEntries(_lib.Database.GetPlaylists().Single(p => p.Name == name).Id).Select(e => e.Path).ToList();

    // ───────────── Where songs go ─────────────

    [Fact]
    public async Task A_playlists_songs_arrive_in_the_servers_layout_and_fill_a_playlist_in_its_order()
    {
        Choose(Playlist("pl1", "Friday"));
        SyncResult result = await Sync().RunAsync(cancel: TestContext.Current.CancellationToken);

        Assert.True(result.Finished);
        Assert.Equal((3, 3, 0, 0), (result.Songs, result.Downloaded, result.AlreadyHere, result.Failed));
        string boogie = Here("Jazz & Swing", "Count Basie", "Cute", "03 - Basie Boogie.mp3");   // /music/ left off
        Assert.Equal(FakeNavidrome.Bytes("s1"), File.ReadAllBytes(boogie));
        Assert.Empty(Directory.EnumerateFiles(Downloads, "*.partial", SearchOption.AllDirectories));

        Assert.Equal([Here("Jazz & Swing", "Ella Fitzgerald", "Ella Swings", "07 - Air Mail Special.flac"), boogie,
            Here("Blues", "Ma Rainey", "Mother of the Blues", "Lucky Rock Blues.mp3")], PlaylistPaths("Friday"));
        Assert.Equal(3, _lib.Database.GetServerSongs(_record.Id).Count);
        Assert.Equal((3, result.Bytes), _lib.Database.GetDownloadedTotals());
    }

    [Fact]
    public async Task A_second_sync_downloads_nothing_again_and_follows_the_servers_playlist()
    {
        Choose(Playlist("pl1", "Friday"));
        await Sync().RunAsync(cancel: TestContext.Current.CancellationToken);
        long playlistId = _lib.Database.GetServerChoices(_record.Id).Single().PlaylistId!.Value;
        _lib.Database.RenamePlaylist(playlistId, "Friday at Hot Jam");   // the DJ's name for it stays
        int downloads = _server.Downloads;

        _server.Playlists["pl1"] = ["s3", "s1"];   // reordered on the server, one taken out
        SyncResult again = await Sync().RunAsync(cancel: TestContext.Current.CancellationToken);

        Assert.Equal((2, 0, 2), (again.Songs, again.Downloaded, again.AlreadyHere));
        Assert.Equal(downloads, _server.Downloads);
        Assert.Equal([Here("Blues", "Ma Rainey", "Mother of the Blues", "Lucky Rock Blues.mp3"),
            Here("Jazz & Swing", "Count Basie", "Cute", "03 - Basie Boogie.mp3")], PlaylistPaths("Friday at Hot Jam"));
        Assert.True(File.Exists(Here("Jazz & Swing", "Ella Fitzgerald", "Ella Swings", "07 - Air Mail Special.flac")));   // kept: cleaning up is later
    }

    [Fact]
    public async Task Starred_songs_albums_and_artists_all_arrive_each_once()
    {
        _server.StarredSongs = ["s1"];
        _server.StarredAlbums = ["al-cute"];     // s1 and s4
        _server.StarredArtists = ["ar-ella"];    // her album: s2
        Choose(ServerChoice.Starred);

        SyncResult result = await Sync().RunAsync(cancel: TestContext.Current.CancellationToken);

        Assert.Equal((3, 3), (result.Songs, result.Downloaded));
        Assert.True(File.Exists(Here("Jazz & Swing", "Count Basie", "Cute", "05 - Li'l Darlin'.mp3")));
        Assert.True(File.Exists(Here("Jazz & Swing", "Ella Fitzgerald", "Ella Swings", "07 - Air Mail Special.flac")));
        Assert.DoesNotContain(_lib.Database.GetPlaylists(), p => p.Name == "Starred");   // starred songs fill no playlist
    }

    [Fact]
    public async Task A_song_the_DJ_already_has_isnt_downloaded_and_the_playlist_uses_theirs()
    {
        string mine = _lib.AddMp3("Count Basie/Basie Boogie.mp3", new TestMp3.Tags(Title: "Basie Boogie", Artist: "Count Basie"));
        _lib.AddRootAndScan();
        _server.Sizes["s1"] = new FileInfo(mine).Length;   // the same file as the server's
        Choose(Playlist("pl1", "Friday"));

        SyncResult result = await Sync().RunAsync(cancel: TestContext.Current.CancellationToken);

        Assert.Equal((2, 1), (result.Downloaded, result.AlreadyHere));
        Assert.Contains(mine, PlaylistPaths("Friday"));
        Assert.False(File.Exists(Here("Jazz & Swing", "Count Basie", "Cute", "03 - Basie Boogie.mp3")));
        Assert.True(_lib.Database.GetServerSongs(_record.Id).Single(s => s.SongId == "s1").OwnCopy);
        Assert.Equal(2, _lib.Database.GetDownloadedTotals().Songs);   // theirs isn't counted as a download
    }

    [Fact]
    public async Task Without_real_paths_songs_are_laid_out_from_their_tags()
    {
        _server.RealPaths = false;
        Choose(Playlist("pl1", "Friday"));
        await Sync().RunAsync(cancel: TestContext.Current.CancellationToken);

        Assert.True(File.Exists(Here("Count Basie", "Cute", "03 - Basie Boogie.mp3")));
        Assert.True(File.Exists(Here("Ella Fitzgerald", "Ella Swings", "07 - Air Mail Special.flac")));
    }

    // ───────────── When it can't ─────────────

    [Fact]
    public async Task A_song_that_arrives_incomplete_isnt_kept_and_the_rest_still_arrive()
    {
        _server.Truncate = "s2";
        Choose(Playlist("pl1", "Friday"));
        SyncResult result = await Sync().RunAsync(cancel: TestContext.Current.CancellationToken);

        Assert.Equal((2, 1), (result.Downloaded, result.Failed));
        Assert.Contains(result.Problems, p => p.StartsWith("Air Mail Special: It arrived incomplete", StringComparison.Ordinal));
        Assert.False(File.Exists(Here("Jazz & Swing", "Ella Fitzgerald", "Ella Swings", "07 - Air Mail Special.flac")));
        Assert.Empty(Directory.EnumerateFiles(Downloads, "*.partial", SearchOption.AllDirectories));
        Assert.Equal(2, PlaylistPaths("Friday").Count);   // the songs that are here
    }

    [Fact]
    public async Task It_stops_when_the_room_set_aside_is_full()
    {
        Choose(Playlist("pl1", "Friday"));
        SyncResult result = await Sync(limit: 2500).RunAsync(cancel: TestContext.Current.CancellationToken);   // each song is 1,000 bytes or so

        Assert.Equal(2, result.Downloaded);
        Assert.Contains("full", result.Stopped);
    }

    [Fact]
    public async Task It_stops_between_songs_when_asked_to()
    {
        Choose(Playlist("pl1", "Friday"));
        int asked = 0;
        SyncResult result = await Sync(stopBefore: () => ++asked > 1 ? "The room is hearing music." : null)
            .RunAsync(cancel: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Downloaded);
        Assert.Equal("The room is hearing music.", result.Stopped);
        Assert.Single(PlaylistPaths("Friday"));
    }

    [Fact]
    public async Task A_server_that_cant_be_reached_says_so_and_downloads_nothing()
    {
        _server.Down = true;
        Choose(Playlist("pl1", "Friday"));
        SyncResult result = await Sync().RunAsync(cancel: TestContext.Current.CancellationToken);

        Assert.False(result.Finished);
        Assert.Equal(0, result.Downloaded);
        Assert.Empty(_lib.Database.GetPlaylists());
    }

    [Fact]
    public void Half_downloaded_files_left_behind_are_cleared()
    {
        string leftover = Here("Blues", "x.mp3.partial");
        Directory.CreateDirectory(Path.GetDirectoryName(leftover)!);
        File.WriteAllText(leftover, "half");
        File.WriteAllText(Here("Blues", "kept.mp3"), "whole");

        Assert.Equal(1, ServerSync.ClearLeftovers(Downloads));
        Assert.False(File.Exists(leftover));
        Assert.True(File.Exists(Here("Blues", "kept.mp3")));
    }

    // ───────────── Names ─────────────

    [Theory]
    [InlineData("AC/DC: Live?", "AC_DC_ Live_")]
    [InlineData("Ends with a dot.", "Ends with a dot")]
    [InlineData("  spaced  ", "spaced")]
    [InlineData("CON", "_CON")]
    [InlineData("aux.mp3", "_aux.mp3")]
    [InlineData("Café Society", "Café Society")]
    [InlineData("...", "_")]
    public void Names_are_made_safe_on_every_system(string name, string safe) => Assert.Equal(safe, ServerPaths.Safe(name));

    [Fact]
    public void Two_songs_in_one_place_in_any_case_get_a_number()
    {
        string folder = Path.Combine(_lib.Directory, "srv");
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine(folder, "Count Basie", "One.mp3") };
        string placed = ServerPaths.Place(folder, ["count basie", "one.mp3"], taken.Contains);
        Assert.Equal(Path.Combine(folder, "count basie", "one (2).mp3"), placed);
    }

    [Fact]
    public void A_path_too_long_for_Windows_is_shortened_keeping_its_type()
    {
        string folder = Path.Combine(_lib.Directory, "srv");
        string longName = new('x', 120);
        string placed = ServerPaths.Place(folder, [longName, longName, longName + ".flac"], _ => false);
        Assert.True(placed.Length <= 250, placed.Length.ToString());
        Assert.EndsWith(".flac", placed);
    }
}
