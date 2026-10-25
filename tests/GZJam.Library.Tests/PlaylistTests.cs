using GZJam.Testing;

namespace GZJam.Library.Tests;

/// <summary>Playlists: sets planned in order, saved by name.</summary>
public sealed class PlaylistTests : IDisposable
{
    private readonly TempLibrary _lib = new();

    public void Dispose() => _lib.Dispose();

    private (string A, string B, string C) ThreeSongs()
    {
        string a = _lib.AddMp3("Swing/a.mp3", new TestMp3.Tags(Title: "A", Duration: TimeSpan.FromSeconds(100)));
        string b = _lib.AddMp3("Swing/b.mp3", new TestMp3.Tags(Title: "B", Duration: TimeSpan.FromSeconds(200)));
        string c = _lib.AddMp3("Blues/c.mp3", new TestMp3.Tags(Title: "C", Duration: TimeSpan.FromSeconds(300)));
        _lib.AddRootAndScan();
        return (a, b, c);
    }

    private IEnumerable<string?> Titles(long playlist, string? search = null) =>
        _lib.Database.Search(search, playlist: playlist).Select(t => t.Title);

    [Fact]
    public void A_playlist_keeps_its_order_and_a_song_can_be_in_it_twice()
    {
        var (a, b, c) = ThreeSongs();
        byte[] before = File.ReadAllBytes(a);
        var saturday = _lib.Database.CreatePlaylist("Saturday");

        _lib.Database.AddToPlaylist(saturday.Id, [c, a]);
        _lib.Database.AddToPlaylist(saturday.Id, [b], index: 1);   // between them
        _lib.Database.AddToPlaylist(saturday.Id, [a]);              // again, at the end

        Assert.Equal(["C", "B", "A", "A"], Titles(saturday.Id));
        var rows = _lib.Database.Search(null, playlist: saturday.Id);
        Assert.Equal([1, 2, 3, 4], rows.Select(t => t.PlaylistNumber!.Value));
        Assert.Equal(4, rows.Select(t => t.PlaylistEntryId).Distinct().Count());   // each row its own entry
        Assert.All(_lib.Database.Search(null), t => Assert.Null(t.PlaylistNumber));   // the library isn't numbered

        var info = Assert.Single(_lib.Database.GetPlaylists());
        Assert.Equal(("Saturday", 4), (info.Name, info.Count));
        Assert.Equal(700, info.Length.TotalSeconds, 0);   // the test files' lengths are a frame or so out
        Assert.Equal(before, File.ReadAllBytes(a));   // never written to the files
    }

    [Fact]
    public void Search_filters_within_a_playlist_and_numbers_stay_its_own()
    {
        var (a, b, c) = ThreeSongs();
        var p = _lib.Database.CreatePlaylist("Mixed");
        _lib.Database.AddToPlaylist(p.Id, [a, c, b]);

        var found = _lib.Database.Search("swing", playlist: p.Id);   // folder names count, as in the library
        Assert.Equal(["A", "B"], found.Select(t => t.Title));
        Assert.Equal([1, 3], found.Select(t => t.PlaylistNumber!.Value));
    }

    [Fact]
    public void Entries_move_and_are_removed_without_touching_the_others()
    {
        var (a, b, c) = ThreeSongs();
        var p = _lib.Database.CreatePlaylist("Order");
        _lib.Database.AddToPlaylist(p.Id, [a, b, c, a]);
        var entries = _lib.Database.GetPlaylistEntries(p.Id);

        _lib.Database.MovePlaylistEntries(p.Id, [entries[2].EntryId, entries[3].EntryId], 0);   // C and the second A to the top
        Assert.Equal(["C", "A", "A", "B"], Titles(p.Id));

        _lib.Database.MovePlaylistEntries(p.Id, [entries[1].EntryId], 99);   // B to the end (it is already)
        _lib.Database.MovePlaylistEntries(p.Id, [entries[2].EntryId], 4);    // C to the end
        Assert.Equal(["A", "A", "B", "C"], Titles(p.Id));

        _lib.Database.RemoveFromPlaylist(p.Id, [entries[0].EntryId]);   // the first A only
        Assert.Equal(["A", "B", "C"], Titles(p.Id));
        Assert.Equal(entries[3].EntryId, _lib.Database.GetPlaylistEntries(p.Id)[0].EntryId);
        Assert.True(File.Exists(a));
    }

    [Fact]
    public void Names_are_kept_apart_and_deleting_leaves_the_songs()
    {
        var (a, _, _) = ThreeSongs();
        var first = _lib.Database.CreatePlaylist("  Saturday ");
        var second = _lib.Database.CreatePlaylist("saturday");
        Assert.Equal(("Saturday", "saturday 2"), (first.Name, second.Name));
        Assert.Equal("Saturday 2", _lib.Database.RenamePlaylist(second.Id, "Saturday"));   // taken: the next number
        Assert.Equal("saturday", _lib.Database.RenamePlaylist(first.Id, "saturday"));      // its own name, recapitalized
        Assert.Throws<ArgumentException>(() => _lib.Database.CreatePlaylist("  "));

        _lib.Database.AddToPlaylist(first.Id, [a]);
        _lib.Database.DeletePlaylist(first.Id);
        Assert.Equal(["Saturday 2"], _lib.Database.GetPlaylists().Select(p => p.Name));
        Assert.Equal(3, _lib.Database.Search(null).Count);
    }

    [Fact]
    public void A_song_gone_from_the_library_is_kept_but_not_shown()
    {
        var (a, b, _) = ThreeSongs();
        var p = _lib.Database.CreatePlaylist("Kept");
        _lib.Database.AddToPlaylist(p.Id, [a, @"C:\elsewhere\gone.mp3", b]);

        Assert.Equal(["A", "B"], Titles(p.Id));
        Assert.Equal(2, _lib.Database.GetPlaylists()[0].Count);
        Assert.Equal(3, _lib.Database.GetPlaylistEntries(p.Id).Count);   // back if the file comes back
    }
}
