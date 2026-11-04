using GZJam.Testing;

namespace GZJam.Library.Tests;

/// <summary>
/// Editing songs' tags (#129): kept in the library over what the files say, by default; written
/// into the files when the DJ chooses.
/// </summary>
public sealed class TagEditTests : IDisposable
{
    private readonly TempLibrary _lib = new();

    public void Dispose() => _lib.Dispose();

    private string CopySample(string name)
    {
        string path = Path.Combine(_lib.MusicPath, "Formats", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Formats", name), path);
        return path;
    }

    private Track Find(string path) => _lib.Database.FindTrack(path)!;

    [Fact]
    public void An_edit_kept_in_the_library_is_shown_searched_and_lasts_through_scans_even_when_the_file_changes()
    {
        string path = _lib.AddMp3("Swing/Song A.mp3", new TestMp3.Tags(Title: "Song A", Artist: "Basie, Count"));
        _lib.AddMp3("Swing/Song B.mp3", new TestMp3.Tags(Title: "Song B", Artist: "Chick Webb"));
        LibraryRoot root = _lib.AddRootAndScan();
        long size = new FileInfo(path).Length;

        _lib.Database.EditTags([path], new Dictionary<string, string?>
        {
            [SongFields.Artist] = "Count Basie",
            [SongFields.Year] = "1957",
            [SongFields.Bpm] = "156.5",
            [SongFields.ForTag("Dance")] = "Lindy",
        });

        Track song = Find(path);
        Assert.Equal("Count Basie", song.Artist);
        Assert.Equal(1957, song.Year);
        Assert.Equal(156.5, song.TagBpm);
        Assert.Equal("Lindy", song.Tag("dance"));
        Assert.Equal(path, Assert.Single(_lib.Database.Search("count basie")).Path);
        Assert.Equal(path, Assert.Single(_lib.Database.Search("dance:lindy")).Path);
        Assert.Contains(_lib.Database.GetTagNames(), t => t.Key == "dance");
        Assert.Equal(size, new FileInfo(path).Length);   // the file isn't touched

        // Another program changes the file's tags: its new title shows, the edit still wins.
        using (var file = TagLib.File.Create(path))
        {
            file.Tag.Title = "Song A (remastered)";
            file.Tag.Performers = ["Basie, Count"];
            file.Save();
        }
        _lib.Scanner.Scan(root, readAllTags: true);

        song = Find(path);
        Assert.Equal("Song A (remastered)", song.Title);
        Assert.Equal("Count Basie", song.Artist);
        Assert.Equal("Lindy", song.Tag("dance"));
    }

    [Fact]
    public void An_edit_follows_its_song_when_its_renamed()
    {
        string path = _lib.AddMp3("Swing/Song A.mp3", new TestMp3.Tags(Title: "Song A"));
        LibraryRoot root = _lib.AddRootAndScan();
        _lib.Database.EditTags([path], new Dictionary<string, string?> { [SongFields.Title] = "Jumpin' at the Woodside" });

        string renamed = Path.Combine(Path.GetDirectoryName(path)!, "Woodside.mp3");
        File.Move(path, renamed);
        _lib.Scanner.Scan(root);

        Assert.Equal("Jumpin' at the Woodside", Find(renamed).Title);
        Assert.Empty(_lib.Database.GetTagEdits(path));
    }

    [Fact]
    public void Clearing_edits_shows_what_the_file_says_and_setting_them_back_restores_them()
    {
        string path = _lib.AddMp3("Swing/Song A.mp3", new TestMp3.Tags(Title: "Song A", Artist: "Chick Webb", Genre: "Swing"));
        _lib.AddRootAndScan();
        _lib.Database.EditTags([path], new Dictionary<string, string?> { [SongFields.Title] = "Stompin'", [SongFields.Genre] = null });
        Assert.Null(Find(path).Genre);
        var edits = _lib.Database.GetTagEdits(path);

        _lib.Database.ClearTagEdits([path], [SongFields.Genre]);
        Assert.Equal("Swing", Find(path).Genre);
        Assert.Equal("Stompin'", Find(path).Title);

        _lib.Database.ClearTagEdits([path]);
        Assert.Equal("Song A", Find(path).Title);
        Assert.Empty(_lib.Database.GetTagEdits(path));

        _lib.Database.SetTagEdits(path, edits);
        Assert.Equal("Stompin'", Find(path).Title);
        Assert.Null(Find(path).Genre);
    }

    [Fact]
    public void An_other_tag_edited_under_another_spelling_is_one_tag()
    {
        string path = _lib.AddMp3("Swing/Song A.mp3", new TestMp3.Tags(Title: "Song A"));
        _lib.AddRootAndScan();
        _lib.Database.EditTags([path], new Dictionary<string, string?> { [SongFields.ForTag("Dance")] = "Lindy" });
        _lib.Database.EditTags([path], new Dictionary<string, string?> { [SongFields.ForTag("DANCE")] = "Balboa" });

        Assert.Equal([new SongTag("DANCE", "Balboa")], Find(path).Tags);
        Assert.Single(_lib.Database.GetTagEdits(path));
    }

    public static TheoryData<string> Formats => ["tone.mp3", "tone.flac", "tone.opus", "tone-aac.m4a", "tone.wma"];

    [Theory]
    [MemberData(nameof(Formats))]
    public void Edits_are_written_into_each_kind_of_file_where_GZJam_and_other_players_read_them(string sample)
    {
        string path = CopySample(sample);
        var changes = new Dictionary<string, string?>
        {
            [SongFields.Title] = "Shiny Stockings",
            [SongFields.Artist] = "Count Basie; Ella Fitzgerald",
            [SongFields.Album] = "Ella and Basie!",
            [SongFields.AlbumArtist] = "Count Basie",
            [SongFields.Genre] = "Swing",
            [SongFields.Year] = "1963",
            [SongFields.Track] = "4",
            [SongFields.Bpm] = "132",
            [SongFields.Grouping] = "Lindy, medium",
            [SongFields.Comment] = "Great for a jam",
            [SongFields.ForTag("Composer")] = "Frank Foster",
            [SongFields.ForTag("Dance")] = "Lindy",
        };

        Assert.Null(TagWriter.Write(path, changes));

        var read = TagWriter.ReadFields(path, changes.Keys)!;
        Assert.Equal(changes, read);
        Assert.True(SongFile.Read(path)!.SampleRate > 0);   // still audio

        Assert.Null(TagWriter.Write(path, new Dictionary<string, string?>
        {
            [SongFields.ForTag("Dance")] = null,
            [SongFields.Comment] = null,
            [SongFields.ForTag("Composer")] = "Ernie Wilkins",
        }));
        read = TagWriter.ReadFields(path, changes.Keys)!;
        Assert.Null(read[SongFields.ForTag("Dance")]);
        Assert.Null(read[SongFields.Comment]);
        Assert.Equal("Ernie Wilkins", read[SongFields.ForTag("Composer")]);
        Assert.Equal("Shiny Stockings", read[SongFields.Title]);
    }

    [Fact]
    public void An_mp3s_GRP1_grouping_and_decimal_BPM_are_written_as_GZJam_reads_them()
    {
        string path = _lib.AddMp3("Swing/Song A.mp3", new TestMp3.Tags(Title: "Song A"));
        using (var file = TagLib.File.Create(path))
        {
            var id3 = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2, create: true);
            id3.AddFrame(TagWriter.TextFrame(id3, "GRP1", "Balboa"));
            file.Save();
        }
        Assert.Equal("Balboa", TagWriter.ReadFields(path, [SongFields.Grouping])![SongFields.Grouping]);

        Assert.Null(TagWriter.Write(path, new Dictionary<string, string?> { [SongFields.Grouping] = "Lindy", [SongFields.Bpm] = "156.5" }));

        var read = TagWriter.ReadFields(path, [SongFields.Grouping, SongFields.Bpm])!;
        Assert.Equal("Lindy", read[SongFields.Grouping]);
        Assert.Equal("156.5", read[SongFields.Bpm]);
    }

    [Fact]
    public void A_read_only_file_says_so_and_is_left_as_it_was()
    {
        string path = _lib.AddMp3("Swing/Song A.mp3", new TestMp3.Tags(Title: "Song A"));
        byte[] before = File.ReadAllBytes(path);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead);

            Assert.Equal("the file is read-only", TagWriter.Write(path, new Dictionary<string, string?> { [SongFields.Title] = "New" }));
            Assert.Equal(before, File.ReadAllBytes(path));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public void Writing_a_files_tags_keeps_its_analysis()
    {
        string path = _lib.AddMp3("Swing/Song A.mp3", new TestMp3.Tags(Title: "Song A"));
        _lib.AddRootAndScan();
        FileVersion before = FileVersion.Of(path)!.Value;
        _lib.Database.SaveAnalysis(path, before, new StoredAnalysis(1, TimeSpan.FromMinutes(3), TimeSpan.Zero, TimeSpan.FromMinutes(3), 140, null));

        Assert.Null(TagWriter.Write(path, new Dictionary<string, string?> { [SongFields.Comment] = new string('x', 3000) }));
        _lib.Database.TagsWritten(path, before);

        Assert.NotEqual(before, FileVersion.Of(path));
        Assert.NotNull(_lib.Database.GetAnalysis(path, FileVersion.Of(path)!.Value));
    }
}
