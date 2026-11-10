using GZJam.Library.Servers;
using GZJam.Testing;

namespace GZJam.Library.Tests;

/// <summary>A server's catalog (#290): every song on it, its folders as the server has them, and searched like the library.</summary>
public sealed class ServerCatalogTests : IDisposable
{
    private readonly TempLibrary _lib = new();
    private readonly FakeNavidrome _server = new();
    private readonly MusicServer _record;

    public ServerCatalogTests() => _record = _lib.Database.AddServer("synology", "http://synology:4533", "gzjam");

    public void Dispose() => _lib.Dispose();

    private async Task ReadAsync()
    {
        var songs = await ServerCatalogReader.ReadAsync(_server.Client(), cancel: TestContext.Current.CancellationToken);
        _lib.Database.ReplaceServerCatalog(_record.Id, songs, new DateTime(2026, 11, 5, 23, 0, 0, DateTimeKind.Utc));
    }

    private IReadOnlyList<string?> Titles(string folder, string? text) =>
        _lib.Database.SearchServerCatalog(_record.Id, folder, text).Songs.Select(s => s.Title).ToList();

    [Fact]
    public async Task Every_song_is_read_and_kept_with_when()
    {
        await ReadAsync();
        Assert.Equal(4, _lib.Database.GetServerCatalogCount(_record.Id));
        Assert.Equal(new DateTime(2026, 11, 5, 23, 0, 0, DateTimeKind.Utc), _lib.Database.GetServerCatalogTime(_record.Id));
    }

    [Fact]
    public async Task The_servers_folders_make_its_tree_without_the_mount_folder()
    {
        await ReadAsync();
        FolderNode tree = FolderTree.BuildServer(_record.Id, "synology", _lib.Database.GetServerCatalogFolders(_record.Id));

        Assert.True(tree.IsServer);
        Assert.Equal(4, tree.TrackCount);
        Assert.Equal(["Blues", "Jazz & Swing"], tree.Children.Select(c => c.Name));
        FolderNode jazz = tree.Children[1];
        Assert.Equal(["Count Basie", "Ella Fitzgerald"], jazz.Children.Select(c => c.Name));
        Assert.Equal(("Jazz & Swing/Count Basie/Cute", 2), (jazz.Children[0].Children[0].RelativePath, jazz.Children[0].Children[0].TrackCount));
        Assert.All(jazz.Children, c => Assert.Equal(_record.Id, c.ServerId));
    }

    [Fact]
    public async Task Without_real_paths_the_tree_is_album_artist_then_album()
    {
        _server.RealPaths = false;
        var songs = await ServerCatalogReader.ReadAsync(_server.Client(), cancel: TestContext.Current.CancellationToken);
        _lib.Database.ReplaceServerCatalog(_record.Id, songs.Select(s => s with { Path = null }), DateTime.UtcNow);

        FolderNode tree = FolderTree.BuildServer(_record.Id, "synology", _lib.Database.GetServerCatalogFolders(_record.Id));
        Assert.Equal(["Count Basie", "Ella Fitzgerald", "Ma Rainey"], tree.Children.Select(c => c.Name));
        Assert.Equal("Cute", Assert.Single(tree.Children[0].Children).Name);
    }

    [Fact]
    public async Task A_folder_shows_its_songs_and_those_below_in_order()
    {
        await ReadAsync();
        Assert.Equal(["Basie Boogie", "Li'l Darlin'"], Titles("Jazz & Swing/Count Basie", null));
        Assert.Equal(3, Titles("Jazz & Swing", null).Count);
        Assert.Equal(4, Titles("", null).Count);
        Assert.Empty(Titles("Jazz", null));   // a whole folder name, not the start of one
    }

    [Theory]
    [InlineData("basie", new[] { "Basie Boogie", "Li'l Darlin'" })]
    [InlineData("swing", new[] { "Basie Boogie", "Li'l Darlin'", "Air Mail Special" })]   // the folder, and Ella Swings
    [InlineData("\"air mail\"", new[] { "Air Mail Special" })]
    [InlineData("artist:rainey", new[] { "Lucky Rock Blues" })]
    [InlineData("basie -darlin", new[] { "Basie Boogie" })]
    public async Task Words_and_filters_find_the_servers_songs(string text, string[] titles)
    {
        await ReadAsync();
        Assert.Equal(titles.Order(), Titles("", text).Order());
    }

    [Fact]
    public async Task Year_and_BPM_ranges_work_and_the_librarys_own_filters_are_named_as_left_out()
    {
        var songs = (await ServerCatalogReader.ReadAsync(_server.Client(), cancel: TestContext.Current.CancellationToken))
            .Select(s => s with { Year = s.Id == "s1" ? 1958 : 1925, Bpm = s.Id == "s1" ? 160 : 90 });
        _lib.Database.ReplaceServerCatalog(_record.Id, songs, DateTime.UtcNow);

        Assert.Equal(["Basie Boogie"], Titles("", "year:1950-1960"));
        Assert.Equal(["Basie Boogie"], Titles("", "bpm:150-170"));
        Assert.Equal(["Basie Boogie"], Titles("", "~bpm:158"));
        CatalogSearch found = _lib.Database.SearchServerCatalog(_record.Id, "", "boogie rating:yes label:lindy");
        Assert.Equal(["rating:", "label:"], found.LeftOut);
        Assert.Single(found.Songs);
    }

    [Fact]
    public async Task A_search_counts_the_servers_songs_that_arent_the_DJs_own()
    {
        await ReadAsync();
        Assert.Equal(2, _lib.Database.CountServerCatalogNotOwn(_record.Id, "basie"));

        // Li'l Darlin' is in My music too (#318): only Basie Boogie is the server's alone.
        _lib.Database.SaveServerSong(new ServerSongCopy(_record.Id, "s4", @"C:\Music\Li'l Darlin'.mp3", null, null, OwnCopy: true), DateTime.UtcNow);
        // A download isn't the DJ's own: still counted.
        _lib.Database.SaveServerSong(new ServerSongCopy(_record.Id, "s1", @"C:\Downloads\Basie Boogie.mp3", null, null, OwnCopy: false), DateTime.UtcNow);

        Assert.Equal(1, _lib.Database.CountServerCatalogNotOwn(_record.Id, "basie"));
        Assert.Equal(0, _lib.Database.CountServerCatalogNotOwn(_record.Id, "darlin"));
        Assert.Equal(1, _lib.Database.CountServerCatalogNotOwn(_record.Id, "artist:rainey"));
    }

    [Fact]
    public async Task Reading_it_again_replaces_it()
    {
        await ReadAsync();
        _lib.Database.ReplaceServerCatalog(_record.Id, [], DateTime.UtcNow);
        Assert.Equal(0, _lib.Database.GetServerCatalogCount(_record.Id));
    }
}
