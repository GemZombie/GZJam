using GZJam.Testing;
using Microsoft.Data.Sqlite;

namespace GZJam.Library.Tests;

/// <summary>
/// Playlist folders (#354): nested, each name unique only beside its own, moved and deleted with
/// what's in them; and the upgrade that brings them keeping every playlist, its songs and what
/// points at it, readable still by a GZJam that doesn't know folders.
/// </summary>
public sealed class PlaylistFolderTests : IDisposable
{
    private readonly TempLibrary _lib = new();

    public void Dispose() => _lib.Dispose();

    private LibraryDatabase Db => _lib.Database;

    private static T Scalar<T>(string path, string sql)
    {
        using var c = new SqliteConnection($"Data Source={path};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return (T)cmd.ExecuteScalar()!;
    }

    private static void Execute(string path, string sql)
    {
        using var c = new SqliteConnection($"Data Source={path};Pooling=False;Foreign Keys=True");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 1.7's library (33 upgrades) with a playlist that has songs, has been sent to a server and
    /// is a server's kept playlist: upgraded, all of that is still there, at the top.
    /// </summary>
    [Fact]
    public void The_upgrade_keeps_every_playlist_its_songs_and_whats_linked_to_it()
    {
        string path = Path.Combine(_lib.Directory, "from-1.7.db");
        LibraryDatabase.CreateAsOf(path, upgrades: 33);
        Execute(path, """
            INSERT INTO playlists (id, name) VALUES (7, 'Friday'), (8, 'Saturday');
            INSERT INTO playlist_entries (playlist_id, position, path) VALUES (7, 0, 'a.mp3'), (7, 1, 'b.mp3'), (8, 0, 'c.mp3');
            INSERT INTO servers (id, name, address, user_name) VALUES (1, 'synology', 'http://nas', 'dj');
            INSERT INTO server_sent_playlists (playlist_id, server_id, remote_id) VALUES (7, 1, 'pl1');
            INSERT INTO server_choices (server_id, kind, remote_id, name, playlist_id) VALUES (1, 'playlist', 'pl2', 'Saturday', 8);
            """);
        SqliteConnection.ClearAllPools();

        var db = new LibraryDatabase(path);

        var playlists = db.GetPlaylists();
        Assert.Equal(["Friday", "Saturday"], playlists.Select(p => p.Name));
        Assert.All(playlists, p => Assert.Null(p.FolderId));
        Assert.Equal(["a.mp3", "b.mp3"], db.GetPlaylistEntries(7).Select(e => e.Path));
        Assert.Equal(["c.mp3"], db.GetPlaylistEntries(8).Select(e => e.Path));
        Assert.Equal("pl1", db.GetSentPlaylist(7, 1));
        Assert.Equal("synology", playlists.Single(p => p.Id == 8).FollowsServer);
        Assert.Equal(1L, db.Pragma("foreign_keys"));   // on again once the upgrade's done

        // Still a playlist's songs go with it, as before.
        db.DeletePlaylist(7);
        Assert.Empty(db.GetPlaylistEntries(7));
        Assert.Null(db.GetSentPlaylist(7, 1));
    }

    /// <summary>A GZJam before folders (1.7) still reads and adds playlists in the upgraded library.</summary>
    [Fact]
    public void A_GZJam_before_folders_still_reads_it_and_can_add_playlists()
    {
        var setlists = Db.CreatePlaylistFolder("Setlists");
        Db.CreatePlaylist("Nov 8", setlists.Id);
        Db.CreatePlaylist("Nov 8");   // the same name at the top: fine, it's another folder
        SqliteConnection.ClearAllPools();
        string path = Db.DatabasePath;

        // Nothing it has to have newer than itself: it says 1.7 (and back to 0.7) can read it.
        Assert.Equal(19L, Scalar<long>(path, "SELECT oldest_reader FROM schema_compat;"));
        // 1.7's own reading of the list and making of a playlist still work.
        Assert.Equal(2L, Scalar<long>(path, "SELECT COUNT(*) FROM (SELECT p.id, p.name FROM playlists p ORDER BY p.name COLLATE NOCASE);"));
        Execute(path, "INSERT INTO playlists (name) VALUES ('Made in 1.7');");
        Assert.Contains(Db.GetPlaylists(), p => p is { Name: "Made in 1.7", FolderId: null, Depth: 0 });
    }

    [Fact]
    public void Folders_nest_and_count_their_playlists_all_the_way_down()
    {
        var setlists = Db.CreatePlaylistFolder("Setlists");
        var y2026 = Db.CreatePlaylistFolder("2026", setlists.Id);
        Db.CreatePlaylist("Nov 8", y2026.Id);
        Db.CreatePlaylist("Nov 15", y2026.Id);
        Db.CreatePlaylist("Warm-ups", setlists.Id);
        Db.CreatePlaylist("Top");

        var folders = Db.GetPlaylistFolders();
        Assert.Equal((0, 3), folders.Where(f => f.Id == setlists.Id).Select(f => (f.Depth, f.Count)).Single());
        Assert.Equal((1, 2), folders.Where(f => f.Id == y2026.Id).Select(f => (f.Depth, f.Count)).Single());
        var playlists = Db.GetPlaylists().ToDictionary(p => p.Name);
        Assert.Equal(2, playlists["Nov 8"].Depth);
        Assert.Equal(1, playlists["Warm-ups"].Depth);
        Assert.Equal(0, playlists["Top"].Depth);
        Assert.Equal(["Setlists", "2026"], Db.PlaylistFolderPath(y2026.Id));
    }

    [Fact]
    public void A_name_is_unique_only_beside_its_own()
    {
        var setlists = Db.CreatePlaylistFolder("Setlists");
        var practice = Db.CreatePlaylistFolder("Practice");

        Assert.Equal("Nov 8", Db.CreatePlaylist("Nov 8", setlists.Id).Name);
        Assert.Equal("Nov 8", Db.CreatePlaylist("Nov 8", practice.Id).Name);   // another folder: fine
        Assert.Equal("nov 8 2", Db.CreatePlaylist("nov 8", setlists.Id).Name); // the same folder: numbered
        Assert.Equal("Setlists 2", Db.CreatePlaylistFolder("Setlists").Name);
        Assert.Equal("Setlists", Db.CreatePlaylistFolder("Setlists", practice.Id).Name);
    }

    [Fact]
    public void Moving_a_playlist_takes_a_number_if_its_name_is_taken_there()
    {
        var setlists = Db.CreatePlaylistFolder("Setlists");
        Db.CreatePlaylist("Friday", setlists.Id);
        var friday = Db.CreatePlaylist("Friday");

        Assert.Equal("Friday 2", Db.MovePlaylist(friday.Id, setlists.Id));
        Assert.Equal("Friday 2", Db.MovePlaylist(friday.Id, null));   // back to the top: free there now
        Assert.Equal(["Friday", "Friday 2"], Db.GetPlaylists().Select(p => p.Name));
    }

    [Fact]
    public void A_folder_moves_but_never_into_itself()
    {
        var setlists = Db.CreatePlaylistFolder("Setlists");
        var y2026 = Db.CreatePlaylistFolder("2026", setlists.Id);
        var archive = Db.CreatePlaylistFolder("Archive");

        Assert.False(Db.MovePlaylistFolder(setlists.Id, y2026.Id));   // into its own folder
        Assert.False(Db.MovePlaylistFolder(setlists.Id, setlists.Id));
        Assert.True(Db.MovePlaylistFolder(setlists.Id, archive.Id));
        Assert.Equal(["Archive", "Setlists", "2026"], Db.PlaylistFolderPath(y2026.Id));
        Assert.True(Db.MovePlaylistFolder(y2026.Id, null));
        Assert.Equal(0, Db.GetPlaylistFolders().Single(f => f.Id == y2026.Id).Depth);
    }

    [Fact]
    public void Deleting_a_folder_takes_its_folders_and_playlists_but_not_the_songs()
    {
        string song = _lib.AddMp3("Swing/a.mp3", new TestMp3.Tags(Title: "A"));
        _lib.AddRootAndScan();
        var setlists = Db.CreatePlaylistFolder("Setlists");
        var y2026 = Db.CreatePlaylistFolder("2026", setlists.Id);
        var nov8 = Db.CreatePlaylist("Nov 8", y2026.Id);
        Db.AddToPlaylist(nov8.Id, [song]);
        Db.CreatePlaylist("Top");

        Db.DeletePlaylistFolder(setlists.Id);

        Assert.Empty(Db.GetPlaylistFolders());
        Assert.Equal(["Top"], Db.GetPlaylists().Select(p => p.Name));
        Assert.Empty(Db.GetPlaylistEntries(nov8.Id));
        Assert.Single(Db.Search(null));   // the song is still in the library
        Assert.True(File.Exists(song));
    }

    [Fact]
    public void Renaming_keeps_names_unique_beside_their_own()
    {
        var setlists = Db.CreatePlaylistFolder("Setlists");
        Db.CreatePlaylistFolder("Archive");
        var friday = Db.CreatePlaylist("Friday", setlists.Id);
        Db.CreatePlaylist("Saturday", setlists.Id);
        Db.CreatePlaylist("Saturday");

        Assert.Equal("Saturday 2", Db.RenamePlaylist(friday.Id, "Saturday"));
        Assert.Equal("Archive 2", Db.RenamePlaylistFolder(setlists.Id, "Archive"));
    }
}
