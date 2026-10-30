using GZJam.Testing;

namespace GZJam.Library.Tests;

public sealed class ScanningTests : IDisposable
{
    private readonly TempLibrary _lib = new();

    public void Dispose() => _lib.Dispose();

    [Fact]
    public void Reads_tags_including_decimal_bpm_and_duration()
    {
        _lib.AddMp3("Swing/Lindy Hop/Count Basie/Atomic/01 Kid from Red Bank.mp3", new TestMp3.Tags(
            Title: "The Kid from Red Bank", Artist: "Count Basie", Album: "The Atomic Mr. Basie",
            Genre: "Swing", Year: 1958, Track: 1, Bpm: "156.5", Comment: "Great opener",
            Duration: TimeSpan.FromSeconds(183)));

        _lib.AddRootAndScan();
        Track t = Assert.Single(_lib.Database.Search(null));

        Assert.Equal("The Kid from Red Bank", t.Title);
        Assert.Equal("Count Basie", t.Artist);
        Assert.Equal("The Atomic Mr. Basie", t.Album);
        Assert.Equal("Swing", t.Genre);
        Assert.Equal(1958, t.Year);
        Assert.Equal(1, t.TrackNumber);
        Assert.Equal(156.5, t.Bpm);
        Assert.Equal("Great opener", t.Comment);
        Assert.Equal("Swing/Lindy Hop/Count Basie/Atomic", t.Folder);
        Assert.InRange(t.Duration.TotalSeconds, 182, 184);
    }

    [Theory]
    [InlineData("13200", 132.0)]
    [InlineData("26000", 260.0)]
    [InlineData("1320", 132.0)]
    [InlineData("1180", 118.0)]
    [InlineData("180", 180.0)]
    [InlineData("98.5", 98.5)]
    public void Bpm_stored_times_10_or_100_is_scaled_back(string tagValue, double expected)
    {
        _lib.AddMp3("Swing/a.mp3", new TestMp3.Tags(Title: "A", Bpm: tagValue));
        _lib.AddRootAndScan();
        Assert.Equal(expected, Assert.Single(_lib.Database.Search(null)).Bpm);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("0.4")]
    [InlineData("39")]
    public void A_tag_bpm_too_slow_to_be_real_is_left_out(string tagValue)
    {
        // #206: junk like "1" outranked the detected BPM.
        _lib.AddMp3("Swing/a.mp3", new TestMp3.Tags(Title: "A", Bpm: tagValue));
        _lib.AddRootAndScan();
        Assert.Null(Assert.Single(_lib.Database.Search(null)).TagBpm);
    }

    [Fact]
    public void ITunes_data_frames_are_not_shown_as_comments()
    {
        string path = _lib.AddMp3("Swing/a.mp3", new TestMp3.Tags(Title: "A"));
        using (var file = TagLib.File.Create(path))
        {
            var id3 = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2);
            var norm = TagLib.Id3v2.CommentsFrame.Get(id3, "iTunNORM", "eng", true)!;
            norm.Text = " 00000A2B 00000C1D";
            var real = TagLib.Id3v2.CommentsFrame.Get(id3, "", "eng", true)!;
            real.Text = "Great for a slow start";
            file.Save();
        }

        _lib.AddRootAndScan();
        Assert.Equal("Great for a slow start", Assert.Single(_lib.Database.Search(null)).Comment);
    }

    [Fact]
    public void Untagged_file_is_still_listed_by_file_name()
    {
        _lib.AddMp3("Blues/mystery track.mp3");
        _lib.AddRootAndScan();

        Track t = Assert.Single(_lib.Database.Search(null));
        Assert.Null(t.Title);
        Assert.Equal("mystery track", t.DisplayTitle);
    }

    [Fact]
    public void Ignores_non_audio_files()
    {
        _lib.AddMp3("Swing/a.mp3");
        File.WriteAllText(Path.Combine(_lib.MusicPath, "Swing", "folder.jpg"), "not audio");
        File.WriteAllText(Path.Combine(_lib.MusicPath, "Swing", "notes.txt"), "not audio");
        File.WriteAllText(Path.Combine(_lib.MusicPath, "Swing", "._a.mp3"), "macOS metadata");

        _lib.AddRootAndScan();
        Assert.Single(_lib.Database.Search(null));
    }

    [Fact]
    public void Rescan_picks_up_added_changed_and_removed_files()
    {
        string keep = _lib.AddMp3("Swing/keep.mp3", new TestMp3.Tags(Title: "Keep"));
        string change = _lib.AddMp3("Swing/change.mp3", new TestMp3.Tags(Title: "Before"));
        string remove = _lib.AddMp3("Swing/remove.mp3", new TestMp3.Tags(Title: "Remove"));
        LibraryRoot root = _lib.AddRootAndScan();

        _lib.AddMp3("Swing/new.mp3", new TestMp3.Tags(Title: "New"));
        TestMp3.Write(change, new TestMp3.Tags(Title: "After", Artist: "Someone"));
        File.SetLastWriteTimeUtc(change, DateTime.UtcNow.AddMinutes(1));
        File.Delete(remove);

        ScanResult result = _lib.Scanner.Scan(root);

        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.Updated);
        Assert.Equal(1, result.Removed);
        Assert.Equal(1, result.Unchanged);
        Assert.Equal(["After", "Keep", "New"], _lib.Database.Search(null).Select(t => t.Title).Order());
        Assert.Single(_lib.Database.Search("after"));
        Assert.Empty(_lib.Database.Search("before"));
        Assert.True(File.Exists(keep));
    }

    [Fact]
    public void Tags_edited_without_changing_the_files_date_are_read_when_asked()
    {
        string song = _lib.AddMp3("Swing/song.mp3", new TestMp3.Tags(Title: "Before"));
        LibraryRoot root = _lib.AddRootAndScan();

        // A tag editor set to keep the file's date (Mp3tag can), with a title of the same length.
        DateTime date = File.GetLastWriteTimeUtc(song);
        long size = new FileInfo(song).Length;
        TestMp3.Write(song, new TestMp3.Tags(Title: "Afters"));
        File.SetLastWriteTimeUtc(song, date);
        Assert.Equal(size, new FileInfo(song).Length);

        Assert.Equal(1, _lib.Scanner.Scan(root).Unchanged);   // a normal scan can't tell
        Assert.Equal("Before", Assert.Single(_lib.Database.Search(null)).Title);

        Assert.Equal(1, _lib.Scanner.Scan(root, readAllTags: true).Updated);
        Assert.Equal("Afters", Assert.Single(_lib.Database.Search(null)).Title);
    }

    [Fact]
    public void Songs_read_before_tags_were_read_better_are_read_again()
    {
        _lib.AddMp3("Swing/song.mp3", new TestMp3.Tags(Title: "Song"));
        LibraryRoot root = _lib.AddRootAndScan();
        Assert.Equal(1, _lib.Scanner.Scan(root).Unchanged);

        // As if read by an older GZJam.
        using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_lib.Database.DatabasePath}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE tracks SET tag_version = 0;";
            cmd.ExecuteNonQuery();
        }

        Assert.Equal(1, _lib.Scanner.Scan(root).Updated);     // read again once
        Assert.Equal(1, _lib.Scanner.Scan(root).Unchanged);   // and then left alone
    }

    [Fact]
    public void Missing_root_does_not_wipe_the_library()
    {
        _lib.AddMp3("Swing/a.mp3");
        LibraryRoot root = _lib.AddRootAndScan();

        // Like an unplugged USB drive.
        var gone = root with { Path = Path.Combine(_lib.Directory, "not-there") };
        _lib.Scanner.Scan(gone);

        Assert.Single(_lib.Database.Search(null));
    }

    [Fact]
    public void Removing_a_root_forgets_its_tracks_but_not_the_files()
    {
        string file = _lib.AddMp3("Swing/a.mp3", new TestMp3.Tags(Title: "A"));
        LibraryRoot root = _lib.AddRootAndScan();

        _lib.Database.RemoveRoot(root.Id);

        Assert.Empty(_lib.Database.Search(null));
        Assert.Empty(_lib.Database.Search("a"));
        Assert.True(File.Exists(file));
    }
}

public sealed class SearchTests : IDisposable
{
    private readonly TempLibrary _lib = new();
    private readonly LibraryRoot _root;

    public SearchTests()
    {
        _lib.AddMp3("Swing/Lindy Hop/Count Basie/Decca/Jumpin.mp3", new TestMp3.Tags("Jumpin' at the Woodside", "Count Basie", "Decca", Year: 1938));
        _lib.AddMp3("Swing/Lindy Hop/Count Basie/Decca/One OClock.mp3", new TestMp3.Tags("One O'Clock Jump", "Count Basie", "Decca", Year: 1937));
        _lib.AddMp3("Swing/Lindy Hop/Chick Webb/Stompin.mp3", new TestMp3.Tags("Stompin' at the Savoy", "Chick Webb"));
        _lib.AddMp3("Swing/Balboa/Benny Goodman/Sing.mp3", new TestMp3.Tags("Sing, Sing, Sing", "Benny Goodman"));
        _lib.AddMp3("Swing/Balboa/Café Society/Song.mp3", new TestMp3.Tags("Café Song", "Café Society"));
        _lib.AddMp3("Swing Era/Count Basie/Basie Era.mp3", new TestMp3.Tags("Basie Era Track", "Count Basie"));
        _root = _lib.AddRootAndScan();
    }

    public void Dispose() => _lib.Dispose();

    private string[] Titles(string? text, string? folder = null) =>
        _lib.Database.Search(text, folder is null ? null : new FolderScope(_root.Id, folder))
            .Select(t => t.DisplayTitle).Order().ToArray();

    [Fact]
    public void Folder_scope_includes_everything_beneath_it()
    {
        Assert.Equal(["Jumpin' at the Woodside", "One O'Clock Jump", "Stompin' at the Savoy"], Titles(null, "Swing/Lindy Hop"));
    }

    [Fact]
    public void Search_within_a_folder_is_recursive()
    {
        Assert.Equal(["Jumpin' at the Woodside", "One O'Clock Jump"], Titles("basie", "Swing/Lindy Hop"));
    }

    [Fact]
    public void Folder_scope_does_not_leak_into_similarly_named_siblings()
    {
        // "Swing" must not match "Swing Era".
        Assert.DoesNotContain("Basie Era Track", Titles("basie", "Swing"));
        Assert.Contains("Basie Era Track", Titles("basie", "Swing Era"));
    }

    [Fact]
    public void Root_scope_and_no_scope_search_everything()
    {
        Assert.Equal(3, Titles("basie").Length);
        Assert.Equal(3, Titles("basie", "").Length);
    }

    [Fact]
    public void Folder_names_are_searchable()
    {
        // Neither track's tags mention Balboa; only the folder does.
        Assert.Equal(["Café Song", "Sing, Sing, Sing"], Titles("balboa"));
    }

    [Fact]
    public void Words_are_prefixes_and_all_must_match()
    {
        Assert.Equal(["Jumpin' at the Woodside"], Titles("basi jump wood"));
        Assert.Empty(Titles("basie savoy"));
    }

    [Fact]
    public void Punctuation_in_queries_matches_how_tags_are_indexed()
    {
        Assert.Equal(["One O'Clock Jump"], Titles("o'clock"));
        Assert.Equal(["Stompin' at the Savoy"], Titles("stompin'"));
    }

    [Fact]
    public void Accents_are_ignored()
    {
        Assert.Equal(["Café Song"], Titles("cafe song"));
    }

    [Theory]
    [InlineData("\"")]
    [InlineData("NEAR(")]
    [InlineData("* OR -")]
    [InlineData("title:basie")]
    public void Odd_input_never_breaks_search(string input)
    {
        var ex = Record.Exception(() => _lib.Database.Search(input));
        Assert.Null(ex);
    }

    [Fact]
    public void Blank_query_lists_everything_in_scope()
    {
        Assert.Equal(6, Titles("   ").Length);
    }
}

public sealed class FolderTreeTests : IDisposable
{
    private readonly TempLibrary _lib = new();

    public void Dispose() => _lib.Dispose();

    [Fact]
    public void Tree_mirrors_folders_with_recursive_counts_and_natural_order()
    {
        _lib.AddMp3("Swing/Lindy Hop/Basie/Disc 10/a.mp3");
        _lib.AddMp3("Swing/Lindy Hop/Basie/Disc 2/a.mp3");
        _lib.AddMp3("Swing/Lindy Hop/Basie/Disc 2/b.mp3");
        _lib.AddMp3("Swing/Balboa/c.mp3");
        _lib.AddMp3("loose.mp3");
        Directory.CreateDirectory(Path.Combine(_lib.MusicPath, "Artwork Only"));
        _lib.AddRootAndScan();

        FolderNode root = Assert.Single(FolderTree.Build(_lib.Database.GetRoots(), _lib.Database.GetFolderCounts()));

        Assert.Equal(5, root.TrackCount);
        FolderNode swing = Assert.Single(root.Children);   // folders without audio are hidden
        Assert.Equal("Swing", swing.Name);
        Assert.Equal(4, swing.TrackCount);
        Assert.Equal(["Balboa", "Lindy Hop"], swing.Children.Select(c => c.Name));

        FolderNode basie = swing.Children[1].Children[0];
        Assert.Equal("Swing/Lindy Hop/Basie", basie.RelativePath);
        Assert.Equal(3, basie.TrackCount);
        Assert.Equal(["Disc 2", "Disc 10"], basie.Children.Select(c => c.Name));
        Assert.Equal(new FolderScope(root.RootId, "Swing/Lindy Hop/Basie"), basie.Scope);
    }

    [Fact]
    public void Natural_comparer_orders_numbers_by_value()
    {
        string[] names = ["Track 10", "track 2", "Track 1", "Track 02b", "Alpha"];
        Assert.Equal(["Alpha", "Track 1", "track 2", "Track 02b", "Track 10"], names.Order(NaturalComparer.Instance));
    }
}

public sealed class WatcherTests : IDisposable
{
    private readonly TempLibrary _lib = new();

    public void Dispose() => _lib.Dispose();

    [Fact]
    public async Task Raises_one_change_after_files_settle()
    {
        LibraryRoot root = _lib.Database.AddRoot(_lib.MusicPath);
        using var watcher = new LibraryWatcher(TimeSpan.FromMilliseconds(300));
        int raised = 0;
        var changed = new TaskCompletionSource();
        watcher.RootChanged += r =>
        {
            Interlocked.Increment(ref raised);
            changed.TrySetResult();
        };
        watcher.Watch(root);

        for (int i = 0; i < 5; i++)
            _lib.AddMp3($"New Album/{i}.mp3");

        await changed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await Task.Delay(600, TestContext.Current.CancellationToken);
        Assert.Equal(1, raised);
    }
}

public sealed class MatchExpressionTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    [InlineData("'\"*", null)]
    [InlineData("basie", "\"basie\"*")]
    [InlineData("count  basie", "\"count\"* \"basie\"*")]
    [InlineData("o'clock", "\"o\"* \"clock\"*")]
    public void Builds_safe_prefix_terms(string? input, string? expected)
    {
        Assert.Equal(expected, LibraryDatabase.BuildMatchExpression(input));
    }
}
