namespace GZJam.Library.Tests;

/// <summary>A set copied out as files, numbered, with a playlist beside them (#101).</summary>
public sealed class SongExportTests : IDisposable
{
    private readonly string _work = Directory.CreateTempSubdirectory("gzjam-export-").FullName;

    public void Dispose() => Directory.Delete(_work, recursive: true);

    private Track Song(string fileName, string? artist, string? title, int bytes = 1000, int seconds = 180)
    {
        string path = Path.Combine(_work, "music", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Enumerable.Range(0, bytes).Select(i => (byte)i).ToArray());
        return new Track { Path = path, Folder = Path.GetDirectoryName(path)!, FileName = fileName, Title = title, Artist = artist,
            Duration = TimeSpan.FromSeconds(seconds) };
    }

    private string Stick => Directory.CreateDirectory(Path.Combine(_work, "stick")).FullName;

    [Fact]
    public async Task The_songs_are_copied_in_order_with_a_playlist_and_the_originals_are_left_as_they_were()
    {
        Track[] set =
        [
            Song("woodside.MP3", "Count Basie", "Jumpin' at the Woodside", bytes: 1500),
            Song("03 Track.flac", null, null, bytes: 700, seconds: 0),
            Song("savoy.mp3", "Chick Webb", "Stompin' at the Savoy: \"live\"?"),
        ];
        DateTime modified = File.GetLastWriteTimeUtc(set[0].Path);

        var export = SongExport.Plan("Friday Night: Oct 14", set, Stick);
        Assert.Equal(Path.Combine(Stick, "Friday Night_ Oct 14"), export.Folder);
        Assert.Equal(3200, export.Bytes);
        Assert.Empty(export.Missing);
        Assert.True(export.HasRoom);
        Assert.Equal(["01 - Count Basie - Jumpin' at the Woodside.mp3", "02 - 03 Track.flac", "03 - Chick Webb - Stompin' at the Savoy_ _live__.mp3"],
            export.Files.Select(f => f.FileName));

        var progress = new List<int>();
        await export.CopyAsync(new SyncProgress(progress.Add));
        Assert.Equal([1, 2, 3], progress);
        Assert.Equal(File.ReadAllBytes(set[0].Path), File.ReadAllBytes(Path.Combine(export.Folder, export.Files[0].FileName)));
        Assert.Equal(modified, File.GetLastWriteTimeUtc(Path.Combine(export.Folder, export.Files[0].FileName)));
        Assert.True(File.Exists(set[0].Path));   // copied, not moved

        // The playlist names the copies beside it, so it plays from wherever the stick is.
        Assert.Equal(Path.Combine(export.Folder, "Friday Night_ Oct 14.m3u8"), export.PlaylistPath);
        string[] lines = File.ReadAllText(export.PlaylistPath).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("#EXTM3U", lines[0]);
        Assert.Equal("#EXTINF:180,Count Basie - Jumpin' at the Woodside", lines[1]);
        Assert.Equal("01 - Count Basie - Jumpin' at the Woodside.mp3", lines[2]);
        Assert.Equal("#EXTINF:-1,03 Track", lines[3]);
        Assert.Equal(export.Files.Select(f => Path.Combine(export.Folder, f.FileName)), PlaylistFile.Read(export.PlaylistPath));
    }

    [Fact]
    public async Task Nothing_is_overwritten_a_second_copy_goes_into_a_new_folder()
    {
        Track[] set = [Song("a.mp3", "Band", "One")];
        await SongExport.Plan("Set", set, Stick).CopyAsync();
        var again = SongExport.Plan("Set", set, Stick);
        Assert.Equal(Path.Combine(Stick, "Set (2)"), again.Folder);
        await again.CopyAsync();
        Assert.True(File.Exists(Path.Combine(Stick, "Set", "01 - Band - One.mp3")));
        Assert.True(File.Exists(Path.Combine(Stick, "Set (2)", "01 - Band - One.mp3")));
    }

    [Fact]
    public void Missing_songs_are_left_out_and_said_and_the_numbers_stay_in_a_row()
    {
        var gone = Song("gone.mp3", "Band", "Gone");
        File.Delete(gone.Path);
        Track[] set = [Song("a.mp3", "Band", "One"), gone, Song("b.mp3", "Band", "Two")];
        var export = SongExport.Plan("Set", set, Stick);
        Assert.Equal([gone], export.Missing);
        Assert.Equal(["01 - Band - One.mp3", "02 - Band - Two.mp3"], export.Files.Select(f => f.FileName));
    }

    [Fact]
    public void Names_are_kept_short_and_a_long_set_gets_three_digits()
    {
        var songs = Enumerable.Range(1, 100).Select(i => Song($"{i}.mp3", "Band", new string('x', 200))).ToList();
        var export = SongExport.Plan("Long night", songs, Stick);
        Assert.StartsWith("001 - Band - ", export.Files[0].FileName);
        Assert.StartsWith("100 - Band - ", export.Files[99].FileName);
        Assert.All(export.Files, f => Assert.True(f.FileName.Length <= 120 && f.FileName.EndsWith(".mp3")));
        Assert.Equal("GZJam songs", SongExport.SafeName(" ... ", "GZJam songs"));
    }

    /// <summary>Reports on the caller's thread, so the test sees each step in order.</summary>
    private sealed class SyncProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
