using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using GZJam.App.ViewModels;
using GZJam.Library;
using GZJam.Testing;
using Track = GZJam.Library.Track;

namespace GZJam.App.Tests;

/// <summary>
/// Files, the library's third source (#318): the computer's places and any USB stick, as folders.
/// A song from a stick is read into GZJam when it's loaded or queued; Add to my music copies songs
/// into the DJ's music, under Added.
/// </summary>
public sealed class FilesAppTests
{
    private sealed record Setup(AppHarness App, string Desktop, string Stick, List<FilePlace> Sticks) : IDisposable
    {
        public LibraryViewModel Library => App.Library;
        public void Dispose() => App.Dispose();
    }

    /// <summary>A Desktop with a song and a folder of two, and a stick ("SANDISK") with a folder of requests, not yet plugged in.</summary>
    private static Setup WithFiles()
    {
        var app = new AppHarness();
        string desktop = Path.Combine(app.WorkDirectory, "Desktop");
        TestMp3.Write(Path.Combine(desktop, "voice memo.mp3"), new TestMp3.Tags(Title: "Voice memo"));
        TestMp3.Write(Path.Combine(desktop, "Edits", "Sing Sing Sing (Sam's edit).mp3"), new TestMp3.Tags(Title: "Sing Sing Sing", Artist: "Benny Goodman"));
        TestMp3.Write(Path.Combine(desktop, "Edits", "Shiny Stockings (short).mp3"), new TestMp3.Tags(Title: "Shiny Stockings", Artist: "Count Basie"));
        File.WriteAllText(Path.Combine(desktop, "Edits", "notes.txt"), "not a song");
        string stick = Path.Combine(app.WorkDirectory, "Stick");
        TestMp3.Write(Path.Combine(stick, "Gig Nov 14", "Request - Uptown Funk.mp3"), new TestMp3.Tags(Title: "Uptown Funk", Artist: "Mark Ronson"));
        TestMp3.Write(Path.Combine(stick, "Gig Nov 14", "At Last.mp3"), new TestMp3.Tags(Title: "At Last", Artist: "Etta James"));

        var sticks = new List<FilePlace>();
        var files = app.ViewModel.Files;
        files.Places = () => [new FilePlace("Desktop", desktop, FilesKind.Place)];
        files.Drives = () => [];
        files.Sticks = () => sticks.ToList();
        files.ReadInFolder = Path.Combine(app.WorkDirectory, "Read in");
        app.Library.RefreshFiles();
        app.Settings.ServerKeepFolder = app.MusicPath;
        app.ViewModel.ConfirmAsync = (_, _, _) => Task.FromResult(true);
        AppHarness.Settle(100);
        return new Setup(app, desktop, stick, sticks);
    }

    private static void PlugIn(Setup s)
    {
        s.Sticks.Add(new FilePlace("SANDISK (E:)", s.Stick, FilesKind.Stick));
        s.App.ViewModel.Files.CheckSticks();
        AppHarness.Settle(100);
    }

    private static FolderNodeViewModel Node(Setup s, string name) =>
        s.Library.Roots.Where(r => r.IsFiles).SelectMany(r => r.SelfAndDescendants()).First(n => n.Name == name);

    /// <summary>Opens a folder in Files (its folders read), and shows its songs.</summary>
    private static FolderNodeViewModel Open(Setup s, string name, string? inside = null)
    {
        if (inside is not null)
            Node(s, inside).IsExpanded = true;
        FolderNodeViewModel folder = Node(s, name);
        s.Library.SelectedFolder = folder;
        AppHarness.WaitFor(() => !s.Library.IsSearching);
        AppHarness.Settle(100);
        return folder;
    }

    private static void Finish(Task task)
    {
        AppHarness.WaitFor(() => task.IsCompleted, TimeSpan.FromSeconds(20));
        if (task.Exception?.InnerException is { } failure)
            throw failure;
        AppHarness.Settle(150);
    }

    [AvaloniaFact]
    public void Files_shows_the_computer_and_opens_a_folder_to_its_songs()
    {
        using var s = WithFiles();
        var library = s.Library;
        Assert.Equal(["My music", "Files"], library.Sources.Select(x => x.Name));

        library.ChooseSource(LibrarySource.Files);
        AppHarness.Settle(150);
        Assert.Equal(["This computer", "Desktop"], library.FolderRows.Select(r => r.Name));   // the computer opened, the Desktop not yet
        Assert.Empty(library.Results);
        Assert.StartsWith("Files is this computer", library.EmptyMessage);
        s.App.Screenshot("files-empty");   // the message wraps, never cut off
        Assert.Equal("Filter this folder's songs by name", library.SearchPlaceholder);

        Open(s, "Desktop");
        Assert.Equal(["Voice memo"], library.Results.Select(t => t.DisplayTitle));   // the folder's own songs, not its folders'
        Node(s, "Desktop").IsExpanded = true;
        AppHarness.Settle(50);
        Assert.Contains(library.FolderRows, r => r.Name == "Edits");

        Open(s, "Edits");
        Assert.Equal(["Shiny Stockings", "Sing Sing Sing"], library.Results.Select(t => t.DisplayTitle));   // songs only, by file name
        Assert.All(library.Results, t => Assert.True(t.IsOutsideLibrary));
        Assert.Contains("Edits", library.ScopeText);

        library.SearchText = "sam";   // by name: the file's, its title's or its artist's
        AppHarness.WaitFor(() => library.Results.Count == 1);
        Assert.Equal("Sing Sing Sing", library.Results[0].DisplayTitle);
        library.SearchText = "basie";
        AppHarness.WaitFor(() => library.Results.Count == 1 && library.Results[0].DisplayTitle == "Shiny Stockings");
        s.App.Screenshot("files-folder");
    }

    [AvaloniaFact]
    public void A_stick_shows_when_its_plugged_in_and_goes_when_its_pulled_out()
    {
        using var s = WithFiles();
        var library = s.Library;
        library.ChooseSource(LibrarySource.Files);
        AppHarness.Settle(100);
        Assert.DoesNotContain(library.FolderRows, r => r.IsStick);

        PlugIn(s);
        FolderNodeViewModel stick = Assert.Single(library.FolderRows, r => r.IsStick);
        Assert.Equal("SANDISK (E:)", stick.Name);
        Open(s, "Gig Nov 14", inside: "SANDISK (E:)");
        Assert.Equal(2, library.Results.Count);

        s.Sticks.Clear();
        s.App.ViewModel.Files.CheckSticks();
        AppHarness.Settle(150);
        Assert.DoesNotContain(library.FolderRows, r => r.IsStick);
        Assert.Null(library.SelectedFolder);
        Assert.Empty(library.Results);
    }

    [AvaloniaFact]
    public async Task A_song_from_a_stick_is_read_in_when_its_loaded_and_keeps_its_heart()
    {
        using var s = WithFiles();
        PlugIn(s);
        s.Library.ChooseSource(LibrarySource.Files);
        Open(s, "Gig Nov 14", inside: "SANDISK (E:)");
        Track funk = s.Library.Results.Single(t => t.DisplayTitle == "Uptown Funk");
        s.App.ViewModel.SetRating([funk], Rating.Loved);   // on the stick, before it's read in

        Assert.True(await s.App.ViewModel.LoadToDeckAsync(s.App.ViewModel.Decks[0], funk));

        string copy = s.App.ViewModel.Decks[0].Track!.Path;
        Assert.StartsWith(Path.Combine(s.App.WorkDirectory, "Read in", "SANDISK (E_)"), copy);
        Assert.True(File.Exists(copy));
        Assert.Equal(Rating.Loved, s.App.Database.GetMarks(copy).Rating);
        Directory.Delete(s.Stick, recursive: true);   // pulled out: the deck has its own copy
        Assert.True(File.Exists(copy));

        // The stick back in: its song is the copy, with the heart, and isn't read in twice.
        TestMp3.Write(Path.Combine(s.Stick, "Gig Nov 14", "Request - Uptown Funk.mp3"), new TestMp3.Tags(Title: "Uptown Funk", Artist: "Mark Ronson"));
        s.Library.RefreshResults();
        AppHarness.WaitFor(() => !s.Library.IsSearching);
        AppHarness.Settle(100);
        Track again = s.Library.Results.Single(t => t.DisplayTitle == "Uptown Funk");
        Assert.Equal((copy, Rating.Loved), (again.Path, again.Rating));
    }

    [AvaloniaFact]
    public void Songs_from_a_stick_are_read_in_before_they_join_the_queue()
    {
        using var s = WithFiles();
        PlugIn(s);
        s.Library.ChooseSource(LibrarySource.Files);
        Open(s, "Gig Nov 14", inside: "SANDISK (E:)");

        s.App.ViewModel.AddToQueue(s.Library.Results.ToList());
        AppHarness.WaitFor(() => s.App.ViewModel.Queue.Items.Count == 2);

        Assert.All(s.App.ViewModel.Queue.Items, i => Assert.StartsWith(Path.Combine(s.App.WorkDirectory, "Read in"), i.Track.Path));
        Assert.All(s.App.ViewModel.Queue.Items, i => Assert.True(File.Exists(i.Track.Path)));
    }

    [AvaloniaFact]
    public void Add_to_my_music_copies_a_folders_songs_under_Added_with_what_the_DJ_gave_them()
    {
        using var s = WithFiles();
        s.Library.ChooseSource(LibrarySource.Files);
        FolderNodeViewModel edits = Open(s, "Edits", inside: "Desktop");
        Track sing = s.Library.Results.Single(t => t.DisplayTitle == "Sing Sing Sing");
        s.App.ViewModel.SetRating([sing], Rating.Loved);

        Finish(s.App.ViewModel.Files.AddFolderToMyMusicAsync(edits));

        string added = Path.Combine(s.App.MusicPath, "Added", "Edits");
        string copy = Path.Combine(added, "Sing Sing Sing (Sam's edit).mp3");
        Assert.True(File.Exists(copy));
        Assert.True(File.Exists(Path.Combine(added, "Shiny Stockings (short).mp3")));
        Assert.True(File.Exists(sing.Path));   // the original stays
        AppHarness.WaitFor(() => s.App.Database.FindTrack(copy) is not null);
        Assert.Equal(Rating.Loved, s.App.Database.GetMarks(copy).Rating);

        // In My music now: found by its search, and in Files it's the library's own song.
        s.Library.ChooseSource(LibrarySource.MyMusic);
        s.Library.SearchText = "sing sing";
        AppHarness.WaitFor(() => s.Library.Results.Any(t => t.Path == copy));
    }

    [AvaloniaFact]
    public void A_songs_menu_in_Files_offers_Add_to_my_music_and_says_when_its_there_already()
    {
        using var s = WithFiles();
        var files = s.App.ViewModel.Files;
        string album = Path.GetDirectoryName(s.Library.Results[0].Path)!;   // a folder of My music's
        files.Places = () => [new FilePlace("Desktop", s.Desktop, FilesKind.Place), new FilePlace("An album", album, FilesKind.Place)];
        s.Library.RefreshFiles();
        s.Library.ChooseSource(LibrarySource.Files);
        var grid = s.App.Window.FindControl<DataGrid>("TrackGrid")!;
        var menu = s.App.Window.FindControl<ContextMenu>("TrackMenu")!;
        var item = s.App.Window.FindControl<MenuItem>("TrackKeepItem")!;
        (bool, bool, object?) Menu()
        {
            grid.SelectedIndex = 0;
            menu.Open(grid);
            AppHarness.Settle(100);
            var shown = (item.IsVisible, item.IsEnabled, item.Header);
            menu.Close();
            return shown;
        }

        Open(s, "Desktop");
        Assert.Equal((true, true, "Add to my music…"), Menu());

        Open(s, "An album");   // My music's own folder, seen in Files: nothing to add
        Assert.NotEmpty(s.Library.Results);
        Assert.Equal((true, false, "Already in My music"), Menu());
    }

    [AvaloniaFact]
    public void Add_to_my_music_on_songs_from_a_stick_puts_them_under_its_folders_name()
    {
        using var s = WithFiles();
        PlugIn(s);
        s.Library.ChooseSource(LibrarySource.Files);
        Open(s, "Gig Nov 14", inside: "SANDISK (E:)");

        Finish(s.App.ViewModel.Files.AddTracksToMyMusicAsync([s.Library.Results.Single(t => t.DisplayTitle == "At Last")], s.Library.SelectedFolder));

        string copy = Path.Combine(s.App.MusicPath, "Added", "Gig Nov 14", "At Last.mp3");
        Assert.True(File.Exists(copy));
        AppHarness.WaitFor(() => s.App.Database.FindTrack(copy) is not null);
    }

    // ───────────── When each file arrived (#383) ─────────────

    private static IReadOnlyList<string> Shown(Setup s) =>
        s.App.Window.FindControl<DataGrid>("TrackGrid")!.CollectionView!.Cast<Track>().Select(t => t.DisplayTitle).ToList();

    private static void ClickHeader(Setup s, string name)
    {
        var grid = s.App.Window.FindControl<DataGrid>("TrackGrid")!;
        var header = grid.GetVisualDescendants().OfType<DataGridColumnHeader>().First(h => (string?)h.Content == name);
        var at = header.TranslatePoint(new Point(10, header.Bounds.Height / 2), s.App.Window)!.Value;
        s.App.Window.MouseDown(at, MouseButton.Left);
        s.App.Window.MouseUp(at, MouseButton.Left);
        AppHarness.Settle(100);
    }

    [AvaloniaFact]
    public void Files_sorts_a_folder_newest_first_by_when_each_file_arrived_not_when_it_was_modified()
    {
        using var s = WithFiles();
        // Shiny Stockings arrives after Sing Sing Sing, but its file says it was last changed in
        // 2019, as a copied file does; Sing Sing Sing's says yesterday.
        string sing = Path.Combine(s.Desktop, "Edits", "Sing Sing Sing (Sam's edit).mp3");
        string shiny = Path.Combine(s.Desktop, "Edits", "Shiny Stockings (short).mp3");
        File.Delete(shiny);
        Thread.Sleep(50);
        TestMp3.Write(shiny, new TestMp3.Tags(Title: "Shiny Stockings", Artist: "Count Basie"));
        File.SetLastWriteTimeUtc(shiny, new DateTime(2019, 6, 1, 12, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(sing, DateTime.UtcNow.AddDays(-1));

        var grid = s.App.Window.FindControl<DataGrid>("TrackGrid")!;
        DataGridColumn added = grid.Columns.Single(c => (string?)c.Header == "Added");
        Assert.False(added.IsVisible);   // My music's songs have no such date

        s.Library.ChooseSource(LibrarySource.Files);
        Open(s, "Desktop");
        Open(s, "Edits", inside: "Desktop");
        Assert.True(added.IsVisible);
        Assert.Equal(["Shiny Stockings", "Sing Sing Sing"], Shown(s));   // by file name, to start with
        Track shinyTrack = s.Library.Results.Single(t => t.DisplayTitle == "Shiny Stockings");
        Assert.Equal(FileSources.ArrivedUtc(new FileInfo(shiny)), shinyTrack.AddedUtc);

        // Where the drive keeps when a file was created (Windows, macOS, most Linux), that's when it
        // arrived: today, though its modified date says 2019. Elsewhere .NET gives the older of its
        // modified and changed times, and the order follows that.
        bool keepsCreated = new FileInfo(shiny).CreationTimeUtc.Year > 2019;
        if (keepsCreated)
            Assert.StartsWith("Today ", Format.Added(shinyTrack.AddedUtc));
        var newestFirst = s.Library.Results.OrderByDescending(t => t.AddedUtc).Select(t => t.DisplayTitle).ToList();
        if (keepsCreated)
            Assert.Equal(["Shiny Stockings", "Sing Sing Sing"], newestFirst);

        ClickHeader(s, "Added");   // newest first
        Assert.Equal(newestFirst, Shown(s));
        Assert.Equal(ListSortDirection.Descending, grid.CollectionView!.SortDescriptions.Single().Direction);
        ClickHeader(s, "Added");   // then oldest first
        Assert.Equal(newestFirst.AsEnumerable().Reverse(), Shown(s));
        ClickHeader(s, "Added");
        Assert.Equal(newestFirst, Shown(s));
        s.App.Screenshot("files-added-newest-first");

        // Listed in the column menu while it shows, grayed: it can't be hidden.
        var item = s.App.Window.ColumnsMenu().Items.OfType<MenuItem>().Single(i => (string?)i.Header == "Added");
        Assert.True(item.IsChecked);
        Assert.False(item.IsEnabled);

        // Filtering by name keeps the order.
        s.Library.SearchText = "s";
        AppHarness.WaitFor(() => !s.Library.IsSearching);
        AppHarness.Settle(100);
        Assert.Equal(newestFirst, Shown(s));

        // Back in My music: no Added column, and no sort by it left behind.
        s.Library.SearchText = "";
        s.Library.ChooseSource(LibrarySource.MyMusic);
        AppHarness.Settle(200);
        Assert.False(added.IsVisible);
        Assert.DoesNotContain(grid.CollectionView!.SortDescriptions, d => d.PropertyPath == nameof(Track.AddedUtc));
        Assert.DoesNotContain(s.App.Window.ColumnsMenu().Items.OfType<MenuItem>(), i => (string?)i.Header == "Added");
    }

    [Theory]
    [InlineData(0, 20, 42, "Today 8:42 PM")]
    [InlineData(1, 18, 10, "Yesterday 6:10 PM")]
    [InlineData(7, 9, 5, "11/3/2026 9:05 AM")]
    public void Added_says_when_as_a_file_manager_does(int daysAgo, int hour, int minute, string expected)
    {
        var now = new DateTime(2026, 11, 10, 21, 30, 0, DateTimeKind.Local);
        var then = new DateTime(2026, 11, 10, hour, minute, 0, DateTimeKind.Local).AddDays(-daysAgo);
        // Linux writes a narrow no-break space before "PM" (as its time format has it): fine on
        // screen, so compared as a plain one.
        string shown = Format.Added(then.ToUniversalTime(), now.ToUniversalTime()).Replace('\u202F', ' ').Replace('\u00A0', ' ');
        Assert.Equal(expected, shown);
        Assert.Equal("", Format.Added(null));
    }
}
