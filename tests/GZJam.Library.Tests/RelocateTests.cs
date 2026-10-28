using GZJam.Testing;

namespace GZJam.Library.Tests;

/// <summary>A library restored where the music lives somewhere else (#112): its paths follow the music folder.</summary>
public sealed class RelocateTests : IDisposable
{
    private readonly TempLibrary _lib = new();

    public void Dispose() => _lib.Dispose();

    private LibraryDatabase Db => _lib.Database;

    [Fact]
    public void A_music_folder_moved_elsewhere_keeps_its_songs_marks_playlists_and_plays_and_scans_as_unchanged()
    {
        string song = _lib.AddMp3("Swing/a.mp3", new TestMp3.Tags(Title: "Splanky"));
        _lib.AddMp3("Swing/b.mp3", new TestMp3.Tags(Title: "Other"));
        _lib.AddRootAndScan();
        Db.SetRating([song], Rating.Loved);
        Db.AddLabel([song], "Lindy");
        Db.SetCues(song, [new SongCue(1, TimeSpan.FromSeconds(12))]);
        Db.SetEq(song, TrackEq.Flat with { High = -6 });
        PlaylistInfo playlist = Db.CreatePlaylist("Saturday");
        Db.AddToPlaylist(playlist.Id, [song]);
        PlaySession session = Db.StartOrContinueSession(DateTime.UtcNow, TimeSpan.FromHours(3));
        Db.AddPlay(session.Id, Db.Search(null).Single(t => t.Path == song), DateTime.UtcNow, "1", 0);

        // The music, somewhere else on the new computer.
        string elsewhere = Path.Combine(Path.GetDirectoryName(_lib.MusicPath)!, "Elsewhere", "My Music");
        Directory.CreateDirectory(Path.GetDirectoryName(elsewhere)!);
        Directory.Move(_lib.MusicPath, elsewhere);
        Assert.Equal(2, LibraryDatabase.MoveMusicFolders(Db.DatabasePath, [(_lib.MusicPath, elsewhere)]));

        string moved = Path.Combine(elsewhere, "Swing", "a.mp3");
        LibraryRoot root = Db.GetRoots().Single();
        Assert.Equal(elsewhere, root.Path);
        ScanResult scan = _lib.Scanner.Scan(root);
        Assert.Equal((0, 0, 0, 2), (scan.Added, scan.Updated, scan.Removed, scan.Unchanged));   // nothing read again
        Track track = Db.Search(null).Single(t => t.Path == moved);
        Assert.Equal(Rating.Loved, track.Rating);
        Assert.Equal(["Lindy"], track.Labels);
        Assert.Equal(1, track.PlayCount);
        Assert.Equal(-6, Db.GetEq(moved).High);
        Assert.Equal([new SongCue(1, TimeSpan.FromSeconds(12))], Db.GetCues(moved));
        Assert.Equal([moved], Db.GetPlaylistEntries(playlist.Id).Select(e => e.Path));
    }

    [Theory]
    [InlineData(@"C:\Music\Swing\Basie\a.mp3", @"C:\Music", "Swing|Basie|a.mp3")]
    [InlineData(@"c:\music\Swing\a.mp3", @"C:\Music\", "Swing|a.mp3")]           // any case, a trailing separator
    [InlineData("/home/sam/Music/Swing/a.mp3", "/home/sam/Music", "Swing|a.mp3")]
    [InlineData(@"/home/sam/Music/AC\DC.mp3", "/home/sam/Music", @"AC\DC.mp3")]   // a '\' in a Linux name stays
    public void A_path_under_the_old_folder_goes_under_the_new_one_with_this_computers_separator(string path, string from, string rest)
    {
        string to = OperatingSystem.IsWindows() ? @"D:\Music" : "/media/sam/Music";
        Assert.Equal(Path.Combine([to, .. rest.Split('|')]), LibraryDatabase.MovedPath(path, from, to));
    }

    [Theory]
    [InlineData(@"C:\Musical\a.mp3", @"C:\Music")]          // another folder that starts the same
    [InlineData("/home/sam/music/a.mp3", "/home/sam/Music")] // a different folder on Linux
    [InlineData(@"D:\Music\a.mp3", @"C:\Music")]
    public void A_path_not_under_the_old_folder_stays(string path, string from) =>
        Assert.Null(LibraryDatabase.MovedPath(path, from, "/anywhere"));

    [Fact]
    public void The_folder_itself_moves_too() =>
        Assert.Equal("/media/sam/Music", LibraryDatabase.MovedPath(@"C:\Music", @"C:\Music", "/media/sam/Music"));
}
