using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.Library;
using GZJam.Testing;

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
}
