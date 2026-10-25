using GZJam.Testing;
using Microsoft.Data.Sqlite;

namespace GZJam.Library.Tests;

/// <summary>A song renamed or moved keeps what the DJ gave it (issue #74).</summary>
public sealed class RenameTests : IDisposable
{
    private readonly TempLibrary _lib = new();

    public void Dispose() => _lib.Dispose();

    private LibraryDatabase Db => _lib.Database;

    private string PathOf(string relativePath) => Path.Combine(_lib.MusicPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private Track Find(string path) => Db.Search(null).Single(t => t.Path == path);

    /// <summary>A heart, a label, a cue, a playlist and a play: what a rename used to lose.</summary>
    private long Mark(string path)
    {
        Db.SetRating([path], Rating.Loved);
        Db.AddLabel([path], "Lindy");
        Db.SetCues(path, [new SongCue(1, TimeSpan.FromSeconds(12))]);
        PlaylistInfo playlist = Db.CreatePlaylist("Saturday");
        Db.AddToPlaylist(playlist.Id, [path]);
        PlaySession session = Db.StartOrContinueSession(DateTime.UtcNow, TimeSpan.FromHours(3));
        Db.AddPlay(session.Id, Find(path), DateTime.UtcNow, "1", 0);
        return playlist.Id;
    }

    private void AssertMarked(string path, long playlistId)
    {
        Track t = Find(path);
        Assert.Equal(Rating.Loved, t.Rating);
        Assert.Equal(["Lindy"], t.Labels);
        Assert.Equal(1, t.PlayCount);
        Assert.Equal([new SongCue(1, TimeSpan.FromSeconds(12))], Db.GetCues(path));
        Assert.Equal([path], Db.GetPlaylistEntries(playlistId).Select(e => e.Path));
    }

    [Fact]
    public void A_renamed_song_keeps_its_heart_labels_cues_playlists_and_plays()
    {
        string old = _lib.AddMp3("Swing/a.mp3", new TestMp3.Tags(Title: "Splanky"));
        _lib.AddMp3("Swing/b.mp3", new TestMp3.Tags(Title: "Other"));
        LibraryRoot root = _lib.AddRootAndScan();
        long playlist = Mark(old);

        string renamed = PathOf("Swing/Count Basie - Splanky.mp3");
        File.Move(old, renamed);
        ScanResult result = _lib.Scanner.Scan(root);

        Assert.Equal(1, result.Moved);
        Assert.Equal(0, result.Added);
        Assert.Equal(0, result.Removed);
        Assert.True(result.ChangedAnything);
        Assert.Equal(2, Db.GetTrackCount());
        AssertMarked(renamed, playlist);
        Assert.Equal(Rating.Unrated, Find(PathOf("Swing/b.mp3")).Rating);
    }

    [Fact]
    public void Songs_in_a_renamed_folder_keep_theirs_too_even_copies_of_one_file()
    {
        string a = _lib.AddMp3("Swing/a.mp3", new TestMp3.Tags(Title: "Same"));
        string b = PathOf("Swing/b.mp3");
        File.Copy(a, b);
        File.SetLastWriteTimeUtc(b, File.GetLastWriteTimeUtc(a));   // as Windows copies do
        LibraryRoot root = _lib.AddRootAndScan();
        Db.SetRating([a], Rating.Loved);
        Db.SetRating([b], Rating.Rejected);

        Directory.Move(PathOf("Swing"), PathOf("Lindy Hop"));
        ScanResult result = _lib.Scanner.Scan(root);

        Assert.Equal(2, result.Moved);
        Assert.Equal(Rating.Loved, Find(PathOf("Lindy Hop/a.mp3")).Rating);
        Assert.Equal(Rating.Rejected, Find(PathOf("Lindy Hop/b.mp3")).Rating);
    }

    [Fact]
    public void Copies_that_cant_be_told_apart_are_left_as_new_songs()
    {
        string a = _lib.AddMp3("Swing/a.mp3", new TestMp3.Tags(Title: "Same"));
        string b = PathOf("Swing/b.mp3");
        File.Copy(a, b);
        File.SetLastWriteTimeUtc(b, File.GetLastWriteTimeUtc(a));
        LibraryRoot root = _lib.AddRootAndScan();
        Db.SetRating([a], Rating.Loved);

        File.Move(a, PathOf("Swing/c.mp3"));
        File.Move(b, PathOf("Swing/d.mp3"));
        ScanResult result = _lib.Scanner.Scan(root);

        Assert.Equal(0, result.Moved);   // a guess could give one song the other's marks
        Assert.Equal(2, result.Added);
        Assert.Equal(2, result.Removed);
        Assert.All(Db.Search(null), t => Assert.Equal(Rating.Unrated, t.Rating));
    }

    [Fact]
    public void A_changed_file_with_a_new_name_is_a_new_song()
    {
        string old = _lib.AddMp3("Swing/a.mp3", new TestMp3.Tags(Title: "Before"));
        LibraryRoot root = _lib.AddRootAndScan();
        Db.SetRating([old], Rating.Loved);

        File.Delete(old);
        string other = _lib.AddMp3("Swing/b.mp3", new TestMp3.Tags(Title: "Something else entirely"));
        File.SetLastWriteTimeUtc(other, DateTime.UtcNow.AddMinutes(1));
        ScanResult result = _lib.Scanner.Scan(root);

        Assert.Equal(0, result.Moved);
        Assert.Equal(Rating.Unrated, Find(other).Rating);
    }

    [Fact]
    public void What_a_deleted_song_left_at_the_new_name_gives_way_to_the_moved_one()
    {
        string old = _lib.AddMp3("Swing/a.mp3", new TestMp3.Tags(Title: "Keep"));
        string target = _lib.AddMp3("Swing/b.mp3", new TestMp3.Tags(Title: "Gone"));
        File.SetLastWriteTimeUtc(target, DateTime.UtcNow.AddMinutes(-5));
        LibraryRoot root = _lib.AddRootAndScan();
        Db.SetRating([old], Rating.Loved);
        Db.SetRating([target], Rating.Rejected);
        Db.SetCues(target, [new SongCue(3, TimeSpan.FromSeconds(40))]);

        File.Delete(target);
        _lib.Scanner.Scan(root);   // b's marks stay behind, by path
        File.Move(old, target);
        Assert.Equal(1, _lib.Scanner.Scan(root).Moved);

        Assert.Equal(Rating.Loved, Find(target).Rating);
        Assert.Empty(Db.GetCues(target));
    }

    /// <summary>A second music folder beside the first, for moves between them (#87).</summary>
    private LibraryRoot OtherFolder()
    {
        string path = Path.Combine(_lib.Directory, "Other Music");
        Directory.CreateDirectory(path);
        LibraryRoot root = Db.AddRoot(path);
        _lib.Scanner.Scan(root);
        return root;
    }

    [Fact]
    public void A_song_moved_to_another_music_folder_keeps_its_marks_whichever_folder_is_scanned_first()
    {
        foreach (bool oldFolderFirst in new[] { true, false })
        {
            using var lib = new TempLibrary();
            string old = lib.AddMp3("Swing/Splanky.mp3", new TestMp3.Tags(Title: "Splanky"));
            LibraryRoot music = lib.AddRootAndScan();
            string otherPath = Path.Combine(lib.Directory, "Other Music");
            Directory.CreateDirectory(otherPath);
            LibraryRoot other = lib.Database.AddRoot(otherPath);
            lib.Scanner.Scan(other);
            lib.Database.SetRating([old], Rating.Loved);
            lib.Database.AddLabel([old], "Lindy");

            string moved = Path.Combine(otherPath, "Basie", "Splanky.mp3");
            Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
            File.Move(old, moved);
            var (first, second) = oldFolderFirst ? (music, other) : (other, music);
            ScanResult a = lib.Scanner.Scan(first);
            ScanResult b = lib.Scanner.Scan(second);

            // The first folder scanned can't know yet where it went (or came from); the second pairs it.
            Assert.Empty(a.Moves);
            Assert.Equal([(old, moved)], b.Moves);
            Assert.Equal((1, 0, 0), (b.Moved, b.Added, b.Removed));
            Track t = lib.Database.Search(null).Single();
            Assert.Equal(moved, t.Path);
            Assert.Equal(Rating.Loved, t.Rating);
            Assert.Equal(["Lindy"], t.Labels);
        }
    }

    [Fact]
    public void A_rename_reports_its_old_and_new_paths()
    {
        string old = _lib.AddMp3("Swing/a.mp3", new TestMp3.Tags(Title: "Splanky"));
        LibraryRoot root = _lib.AddRootAndScan();
        string renamed = PathOf("Swing/Splanky.mp3");
        File.Move(old, renamed);
        Assert.Equal([(old, renamed)], _lib.Scanner.Scan(root).Moves);
    }

    [Fact]
    public void A_song_copied_to_another_folder_leaves_the_original_its_marks()
    {
        string original = _lib.AddMp3("Swing/a.mp3", new TestMp3.Tags(Title: "Splanky"));
        LibraryRoot music = _lib.AddRootAndScan();
        LibraryRoot other = OtherFolder();
        Db.SetRating([original], Rating.Loved);

        string copy = Path.Combine(other.Path, "a.mp3");
        File.Copy(original, copy);
        File.SetLastWriteTimeUtc(copy, File.GetLastWriteTimeUtc(original));
        Assert.Equal(0, _lib.Scanner.Scan(other).Moved);
        Assert.Equal(0, _lib.Scanner.Scan(music).Moved);

        Assert.Equal(Rating.Loved, Find(original).Rating);
        Assert.Equal(Rating.Unrated, Find(copy).Rating);
    }

    [Fact]
    public void A_song_gone_from_a_folder_and_back_again_isnt_taken_for_a_move()
    {
        string path = _lib.AddMp3("Swing/a.mp3", new TestMp3.Tags(Title: "Splanky"));
        LibraryRoot music = _lib.AddRootAndScan();
        OtherFolder();
        Db.SetRating([path], Rating.Loved);
        string aside = Path.Combine(_lib.Directory, "a.mp3");

        File.Move(path, aside);   // out of every music folder for a moment
        _lib.Scanner.Scan(music);
        File.Move(aside, path);
        ScanResult back = _lib.Scanner.Scan(music);

        Assert.Equal(0, back.Moved);
        Assert.Equal(Rating.Loved, Find(path).Rating);   // still there, by path
    }

    [Fact]
    public void Every_table_kept_by_path_moves_with_the_song()
    {
        using var c = new SqliteConnection($"Data Source={Path.Combine(_lib.Directory, "library.db")}");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT m.name FROM sqlite_master m, pragma_table_info(m.name) p
            WHERE m.type = 'table' AND p.name = 'path' ORDER BY m.name;
            """;
        var tables = new List<string>();
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                tables.Add(r.GetString(0));

        // The index itself is rebuilt from the file, album art depends on the folder, and roots
        // are the music folders, not songs.
        string[] notMoved = ["tracks", "art", "roots"];
        Assert.Equal(tables.Except(notMoved).Order(), LibraryDatabase.MovedTables.Order());
    }
}
