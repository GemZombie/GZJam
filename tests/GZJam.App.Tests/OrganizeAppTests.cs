using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using GZJam.App.ViewModels;
using GZJam.App.Views;
using GZJam.Library;
using GZJam.Testing;

namespace GZJam.App.Tests;

/// <summary>Organize music (#325): two sides, and the library following where the files end up.</summary>
public sealed class OrganizeAppTests
{
    private sealed record Setup(AppHarness App, OrganizeViewModel Vm, string Desktop, string Edits, string Lindy) : IDisposable
    {
        public void Dispose() => App.Dispose();
    }

    /// <summary>A Desktop with a song and an empty Edits folder, and an empty Lindy folder in My music; My music left, Files right.</summary>
    private static Setup WithPanes(bool start = true)
    {
        var app = new AppHarness();
        string desktop = Path.Combine(app.WorkDirectory, "Desktop");
        string edits = Path.Combine(desktop, "Edits");
        Directory.CreateDirectory(edits);
        TestMp3.Write(Path.Combine(desktop, "Voice memo.mp3"), new TestMp3.Tags(Title: "Voice memo"));
        string lindy = Path.Combine(app.MusicPath, "Lindy");
        Directory.CreateDirectory(lindy);
        var files = app.ViewModel.Files;
        files.Places = () => [new FilePlace("Desktop", desktop, FilesKind.Place)];
        files.Drives = () => [];
        files.Sticks = () => [];
        app.Settings.OrganizeMoveWarned = true;
        OrganizeViewModel vm = app.ViewModel.Organize;
        if (start)
            Finish(vm.StartAsync());
        return new Setup(app, vm, desktop, edits, lindy);
    }

    private static void Finish(Task task)
    {
        AppHarness.WaitFor(() => task.IsCompleted, TimeSpan.FromSeconds(20));
        if (task.Exception?.InnerException is { } failure)
            throw failure;
        AppHarness.Settle(150);
    }

    private static void Go(OrganizePane pane, string folder) => Finish(pane.GoToAsync(folder));

    private static void Source(OrganizePane pane, string source)
    {
        pane.SelectedSource = source;
        AppHarness.WaitFor(() => !pane.IsListing);
        AppHarness.Settle(50);
    }

    /// <summary>Selects a song, with those selected already, as Ctrl-clicking does.</summary>
    private static void Pick(OrganizePane pane, string title) => pane.Selection.Select(pane.Items.IndexOf(pane.Items.Single(r => r.Title == title)));

    private static Track Mine(Setup s) => s.App.Library.Results[0];

    [AvaloniaFact]
    public void Files_to_my_music_moved_joins_it_with_its_heart_and_lands_lit()
    {
        using var s = WithPanes();
        string from = Path.Combine(s.Desktop, "Voice memo.mp3"), to = Path.Combine(s.Lindy, "Voice memo.mp3");
        s.App.ViewModel.SetRating([s.App.Database.TrackForFile(from)], Rating.Loved);
        Go(s.Vm.Left, s.Lindy);
        Go(s.Vm.Right, s.Desktop);
        Pick(s.Vm.Right, "Voice memo");
        Assert.Equal("join My music", s.Vm.MoveNote);
        Assert.Equal("←  MOVE", s.Vm.MoveLabel);   // the arrow says which way

        Finish(s.Vm.MoveAsync());

        Assert.False(File.Exists(from));
        Assert.True(File.Exists(to));
        AppHarness.WaitFor(() => s.App.Database.FindTrack(to) is not null);
        Assert.Equal(Rating.Loved, s.App.Database.GetMarks(to).Rating);
        Assert.True(s.Vm.Left.Songs.Single(r => r.Title == "Voice memo").IsFresh);
        Assert.DoesNotContain(s.Vm.Right.Songs, r => r.Title == "Voice memo");

        Finish(s.Vm.Left.GoToAsync(s.Lindy));
        Assert.False(s.Vm.Left.Songs.Single(r => r.Title == "Voice memo").IsFresh);   // gone on to something else
    }

    [AvaloniaFact]
    public void My_music_to_Files_moved_leaves_it_keeps_its_heart_and_comes_back_with_undo()
    {
        using var s = WithPanes();
        Track song = Mine(s);
        string from = song.Path, to = Path.Combine(s.Edits, Path.GetFileName(from));
        s.App.ViewModel.SetRating([song], Rating.Loved);
        PlaylistInfo playlist = s.App.ViewModel.CreatePlaylist("Saturday", [song])!;
        s.App.ViewModel.AddToQueue([song]);
        AppHarness.WaitFor(() => s.App.ViewModel.Queue.Items.Count == 1);
        Go(s.Vm.Left, Path.GetDirectoryName(from)!);
        Go(s.Vm.Right, s.Edits);
        Pick(s.Vm.Left, song.DisplayTitle);
        Assert.Equal("leave My music", s.Vm.MoveNote);
        Assert.Equal("stay in My music", s.Vm.CopyNote);
        Assert.Equal("MOVE  →", s.Vm.MoveLabel);

        Finish(s.Vm.MoveAsync());

        AppHarness.WaitFor(() => s.App.ViewModel.Queue.Items.Single().Track.Path == to);   // its place in the queue follows
        Assert.True(File.Exists(to));
        Assert.False(File.Exists(from));
        AppHarness.WaitFor(() => s.App.Database.FindTrack(from) is null);
        Assert.Null(s.App.Database.FindTrack(to));   // it's left My music
        Assert.Equal(Rating.Loved, s.App.Database.GetMarks(to).Rating);   // with it
        Assert.Equal(Rating.Loved, s.App.Database.GetMarks(from).Rating);   // and for a file put back by hand
        Assert.Equal(to, Assert.Single(s.App.Database.GetPlaylistEntries(playlist.Id)).Path);

        Assert.True(s.Vm.CanUndo);
        Finish(s.Vm.UndoAsync());

        Assert.True(File.Exists(from));
        Assert.False(File.Exists(to));
        AppHarness.WaitFor(() => s.App.Database.FindTrack(from) is not null);
        Assert.Equal(Rating.Loved, s.App.Database.GetMarks(from).Rating);
        Assert.Equal(from, Assert.Single(s.App.Database.GetPlaylistEntries(playlist.Id)).Path);
        Assert.False(s.Vm.CanUndo);
    }

    [AvaloniaFact]
    public void Within_my_music_a_move_is_the_same_song_in_a_new_place()
    {
        using var s = WithPanes();
        Track song = Mine(s);
        int before = s.App.Database.Search(null).Count();
        string from = song.Path, to = Path.Combine(s.Lindy, Path.GetFileName(from));
        s.App.ViewModel.SetRating([song], Rating.Loved);
        Source(s.Vm.Right, OrganizePane.MyMusic);
        Go(s.Vm.Left, Path.GetDirectoryName(from)!);
        Go(s.Vm.Right, s.Lindy);
        Pick(s.Vm.Left, song.DisplayTitle);
        Assert.Equal("same songs, new place", s.Vm.MoveNote);

        Finish(s.Vm.MoveAsync());

        AppHarness.WaitFor(() => s.App.Database.FindTrack(to) is not null && s.App.Database.FindTrack(from) is null);
        Assert.Equal(before, s.App.Database.Search(null).Count());
        Assert.Equal(Rating.Loved, s.App.Database.GetMarks(to).Rating);
    }

    [AvaloniaFact]
    public void My_music_to_Files_copied_stays_and_the_copy_has_what_it_has()
    {
        using var s = WithPanes();
        Track song = Mine(s);
        string from = song.Path, to = Path.Combine(s.Edits, Path.GetFileName(from));
        s.App.ViewModel.SetRating([song], Rating.Loved);
        Go(s.Vm.Left, Path.GetDirectoryName(from)!);
        Go(s.Vm.Right, s.Edits);
        Pick(s.Vm.Left, song.DisplayTitle);

        Finish(s.Vm.CopyAsync());

        Assert.True(File.Exists(from) && File.Exists(to));
        Assert.NotNull(s.App.Database.FindTrack(from));
        Assert.Null(s.App.Database.FindTrack(to));
        Assert.Equal(Rating.Loved, s.App.Database.GetMarks(to).Rating);
        Assert.Equal(Rating.Loved, s.App.Database.GetMarks(from).Rating);
        Assert.False(s.Vm.CanUndo);   // a copy has nothing to put back
    }

    [AvaloniaFact]
    public void Files_to_Files_doesnt_touch_the_library()
    {
        using var s = WithPanes();
        int before = s.App.Database.Search(null).Count();
        Source(s.Vm.Left, OrganizePane.Files);
        Go(s.Vm.Left, s.Desktop);
        Go(s.Vm.Right, s.Edits);
        Pick(s.Vm.Left, "Voice memo");
        Assert.Equal("My music isn't affected", s.Vm.MoveNote);

        Finish(s.Vm.MoveAsync());

        Assert.True(File.Exists(Path.Combine(s.Edits, "Voice memo.mp3")));
        Assert.Equal(before, s.App.Database.Search(null).Count());
    }

    [AvaloniaFact]
    public async Task A_song_on_a_deck_can_be_copied_but_not_moved()
    {
        using var s = WithPanes();
        Track song = Mine(s);
        Assert.True(await s.App.ViewModel.LoadToDeckAsync(s.App.ViewModel.Decks[0], song));
        Go(s.Vm.Left, Path.GetDirectoryName(song.Path)!);
        Go(s.Vm.Right, s.Edits);
        Assert.True(s.Vm.Left.Songs.Single(r => r.Title == song.DisplayTitle).IsLocked);
        Pick(s.Vm.Left, song.DisplayTitle);

        Assert.True(s.Vm.CanCopy);
        Assert.False(s.Vm.CanMove);
        Assert.Contains("on a deck", s.Vm.MoveNote);
        Finish(s.Vm.MoveAsync());

        Assert.True(File.Exists(song.Path));
        Assert.False(File.Exists(Path.Combine(s.Edits, Path.GetFileName(song.Path))));
    }

    [AvaloniaFact]
    public void A_song_never_replaces_another()
    {
        using var s = WithPanes();
        string there = Path.Combine(s.Edits, "Voice memo.mp3");
        TestMp3.Write(there, new TestMp3.Tags(Title: "Another"));
        byte[] bytes = File.ReadAllBytes(there);
        Source(s.Vm.Left, OrganizePane.Files);
        Go(s.Vm.Left, s.Desktop);
        Go(s.Vm.Right, s.Edits);
        Pick(s.Vm.Left, "Voice memo");

        Finish(s.Vm.CopyAsync());

        Assert.Equal(bytes, File.ReadAllBytes(there));
        Assert.True(File.Exists(Path.Combine(s.Edits, "Voice memo (2).mp3")));
    }

    [AvaloniaFact]
    public void A_move_between_drives_that_fails_loses_nothing()
    {
        using var s = WithPanes();
        string from = Path.Combine(s.Desktop, "Voice memo.mp3"), to = Path.Combine(s.Edits, "Voice memo.mp3");
        s.Vm.SameDrive = (_, _) => false;
        s.Vm.CopyFile = (_, partial) =>
        {
            File.WriteAllText(partial, "half");
            throw new IOException("The drive is full.");
        };
        Source(s.Vm.Left, OrganizePane.Files);
        Go(s.Vm.Left, s.Desktop);
        Go(s.Vm.Right, s.Edits);
        Pick(s.Vm.Left, "Voice memo");

        Finish(s.Vm.MoveAsync());

        Assert.True(File.Exists(from));
        Assert.False(File.Exists(to));
        Assert.Empty(Directory.GetFiles(s.Edits));
        Assert.Contains("The drive is full", s.Vm.Status);
        Assert.False(s.Vm.CanUndo);
    }

    [AvaloniaFact]
    public void A_move_between_drives_takes_the_original_away_once_the_copy_is_whole()
    {
        using var s = WithPanes();
        string from = Path.Combine(s.Desktop, "Voice memo.mp3"), to = Path.Combine(s.Edits, "Voice memo.mp3");
        s.Vm.SameDrive = (_, _) => false;
        Source(s.Vm.Left, OrganizePane.Files);
        Go(s.Vm.Left, s.Desktop);
        Go(s.Vm.Right, s.Edits);
        Pick(s.Vm.Left, "Voice memo");

        Finish(s.Vm.MoveAsync());

        Assert.False(File.Exists(from));
        Assert.True(File.Exists(to));
        Assert.Empty(Directory.GetFiles(s.Edits, "*.part"));
    }

    [AvaloniaFact]
    public void The_first_move_warns_about_other_apps_once()
    {
        using var s = WithPanes();
        s.App.Settings.OrganizeMoveWarned = false;
        int asked = 0;
        bool answer = false;
        s.App.ViewModel.ConfirmAsync = (_, message, _) =>
        {
            asked++;
            Assert.Contains("MediaMonkey", message);
            return Task.FromResult(answer);
        };
        Source(s.Vm.Left, OrganizePane.Files);
        Go(s.Vm.Left, s.Desktop);
        Go(s.Vm.Right, s.Edits);
        Pick(s.Vm.Left, "Voice memo");

        Finish(s.Vm.MoveAsync());   // said no: nothing moves, and it asks again
        Assert.True(File.Exists(Path.Combine(s.Desktop, "Voice memo.mp3")));
        Assert.False(s.App.Settings.OrganizeMoveWarned);

        answer = true;
        Finish(s.Vm.MoveAsync());
        Assert.True(File.Exists(Path.Combine(s.Edits, "Voice memo.mp3")));
        Assert.Equal(2, asked);

        Pick(s.Vm.Right, "Voice memo");
        Finish(s.Vm.MoveAsync());
        Assert.Equal(2, asked);   // not again
    }

    [AvaloniaFact]
    public void The_servers_downloads_can_be_copied_from_but_not_moved_or_sent_to()
    {
        using var s = WithPanes();
        string downloads = Path.Combine(s.Desktop, "Downloads");
        TestMp3.Write(Path.Combine(downloads, "synology", "Boogie.mp3"), new TestMp3.Tags(Title: "Boogie"));
        s.App.Settings.ServerDownloadsFolder = downloads;
        Source(s.Vm.Left, OrganizePane.Files);
        Go(s.Vm.Left, Path.Combine(downloads, "synology"));
        Go(s.Vm.Right, s.Edits);
        Pick(s.Vm.Left, "Boogie");
        Assert.True(s.Vm.CanCopy);
        Assert.False(s.Vm.CanMove);
        Assert.Equal("GZJam looks after the downloads", s.Vm.MoveNote);

        Go(s.Vm.Left, s.Desktop);
        Pick(s.Vm.Left, "Voice memo");
        Go(s.Vm.Right, downloads);
        Assert.False(s.Vm.CanCopy);
    }

    [AvaloniaFact]
    public void A_side_goes_into_folders_and_back_up_and_its_path_says_where_it_is()
    {
        using var s = WithPanes();
        OrganizePane right = s.Vm.Right;
        Assert.Equal(s.Desktop, right.Folder);   // the only place there is: straight in
        Assert.Equal("Edits", right.Items[0].Name);   // folders first
        Assert.True(right.Items[0].IsFolder);

        Go(right, s.Edits);
        Assert.Equal("Desktop › Edits", right.FolderText);
        Assert.True(right.IsEmpty);
        Finish(right.UpAsync());
        Assert.Equal(s.Desktop, right.Folder);

        // Two places: the source's top lists them.
        string more = Path.Combine(s.App.WorkDirectory, "More");
        Directory.CreateDirectory(more);
        s.App.ViewModel.Files.Places = () => [new FilePlace("Desktop", s.Desktop, FilesKind.Place), new FilePlace("More", more, FilesKind.Place)];
        Finish(right.GoToAsync(null));
        Assert.Null(right.Folder);
        Assert.Equal(["Desktop", "More"], right.Items.Select(i => i.Name));
        Go(right, s.Edits);
        Assert.Equal(["Files", "Desktop", "Edits"], right.Crumbs.Select(c => c.Name));
    }

    [AvaloniaFact]
    public void Find_narrows_the_folder_by_name_or_artist()
    {
        using var s = WithPanes();
        Track song = Mine(s);
        Go(s.Vm.Left, Path.GetDirectoryName(song.Path)!);
        int all = s.Vm.Left.Items.Count;
        Assert.True(all > 1);

        s.Vm.Left.Filter = song.DisplayTitle;
        Assert.Equal(song.DisplayTitle, Assert.Single(s.Vm.Left.Items).Name);

        s.Vm.Left.Filter = "nothing like this";
        Assert.True(s.Vm.Left.IsEmpty);
        s.Vm.Left.Filter = "";
        Assert.Equal(all, s.Vm.Left.Items.Count);
    }

    [AvaloniaFact]
    public void Selecting_on_one_side_clears_the_other()
    {
        using var s = WithPanes();
        Track song = Mine(s);
        Go(s.Vm.Left, Path.GetDirectoryName(song.Path)!);
        Go(s.Vm.Right, s.Desktop);
        Pick(s.Vm.Left, song.DisplayTitle);
        Assert.True(s.Vm.Left.IsSending);

        Pick(s.Vm.Right, "Voice memo");

        Assert.Empty(s.Vm.Left.Selected);
        Assert.True(s.Vm.Right.IsSending);
        Assert.False(s.Vm.Left.IsSending);
        Assert.False(s.Vm.GoesRight);
    }

    [AvaloniaFact]
    public void Each_side_opens_where_it_was_last_time()
    {
        using var s = WithPanes();
        Go(s.Vm.Left, s.Lindy);
        Go(s.Vm.Right, s.Edits);

        var again = new OrganizeViewModel(s.App.ViewModel, s.App.Database, s.App.Settings);
        Finish(again.StartAsync());
        Assert.Equal((s.Lindy, s.Edits), (again.Left.Folder, again.Right.Folder));

        Directory.Delete(s.Edits);   // a folder gone: its source's start instead
        var later = new OrganizeViewModel(s.App.ViewModel, s.App.Database, s.App.Settings);
        Finish(later.StartAsync());
        Assert.Equal(s.Desktop, later.Right.Folder);
    }

    private static OrganizeView Open(Setup s, string? folder = null)
    {
        OrganizeView view = s.App.Window.ShowOrganize(folder);
        AppHarness.WaitFor(() => !s.Vm.Left.IsListing && !s.Vm.Right.IsListing);
        AppHarness.Settle(250);
        s.App.Window.UpdateLayout();
        return view;
    }

    private static Point RowAt(Setup s, OrganizeView view, string pane, string name)
    {
        var side = view.FindControl<OrganizePaneView>(pane)!;
        var row = side.GetVisualDescendants().OfType<ListBoxItem>().First(i => i.DataContext is OrganizeItem item && item.Name == name);
        return row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), s.App.Window)!.Value;
    }

    private static Point MiddleOf(Setup s, OrganizeView view, string pane)
    {
        var side = view.FindControl<OrganizePaneView>(pane)!;
        return side.TranslatePoint(new Point(side.Bounds.Width / 2, side.Bounds.Height * 0.75), s.App.Window)!.Value;
    }

    private static void Drag(Window window, Point from, Point to, RawInputModifiers keys = RawInputModifiers.None)
    {
        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left, keys);
        AppHarness.Settle(30);
        window.MouseMove(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2), RawInputModifiers.LeftMouseButton | keys);
        window.MouseMove(to, RawInputModifiers.LeftMouseButton | keys);
        AppHarness.Settle(30);
        window.MouseUp(to, MouseButton.Left, keys);
        AppHarness.Settle(300);
    }

    [AvaloniaFact]
    public void Dragging_a_song_across_moves_it_to_the_folder_the_other_side_is_in()
    {
        using var s = WithPanes();
        Go(s.Vm.Left, s.Lindy);
        Go(s.Vm.Right, s.Desktop);
        var view = Open(s);

        Drag(s.App.Window, RowAt(s, view, "RightPane", "Voice memo"), MiddleOf(s, view, "LeftPane"));

        AppHarness.WaitFor(() => File.Exists(Path.Combine(s.Lindy, "Voice memo.mp3")));
        Assert.False(File.Exists(Path.Combine(s.Desktop, "Voice memo.mp3")));
    }

    [AvaloniaFact]
    public void Dragging_with_Ctrl_copies()
    {
        using var s = WithPanes();
        Go(s.Vm.Left, s.Lindy);
        Go(s.Vm.Right, s.Desktop);
        var view = Open(s);

        Drag(s.App.Window, RowAt(s, view, "RightPane", "Voice memo"), MiddleOf(s, view, "LeftPane"), RawInputModifiers.Control);

        AppHarness.WaitFor(() => File.Exists(Path.Combine(s.Lindy, "Voice memo.mp3")));
        Assert.True(File.Exists(Path.Combine(s.Desktop, "Voice memo.mp3")));
    }

    [AvaloniaFact]
    public void Dropping_on_a_folder_row_files_the_song_in_it_even_on_its_own_side()
    {
        using var s = WithPanes();
        Go(s.Vm.Right, s.Desktop);
        var view = Open(s);

        Drag(s.App.Window, RowAt(s, view, "RightPane", "Voice memo"), RowAt(s, view, "RightPane", "Edits"));

        AppHarness.WaitFor(() => File.Exists(Path.Combine(s.Edits, "Voice memo.mp3")));
        Assert.False(File.Exists(Path.Combine(s.Desktop, "Voice memo.mp3")));
    }

    [AvaloniaFact]
    public void It_shows_both_sides_and_the_way_the_songs_go()
    {
        using var s = WithPanes();
        Track song = Mine(s);
        Go(s.Vm.Left, Path.GetDirectoryName(song.Path)!);
        Go(s.Vm.Right, s.Desktop);
        var view = Open(s);
        Pick(s.Vm.Left, song.DisplayTitle);
        AppHarness.Settle(100);

        Assert.True(view.FindControl<Button>("CopyButton")!.IsEnabled);
        Assert.True(view.FindControl<Button>("MoveButton")!.IsEnabled);
        Assert.Equal("leave My music", view.FindControl<TextBlock>("MoveNote")!.Text);
        var rows = view.FindControl<OrganizePaneView>("LeftPane")!.GetVisualDescendants().OfType<ListBoxItem>().ToList();
        Assert.True(rows[0].Bounds.Height <= 28, $"a row is {rows[0].Bounds.Height} tall");   // as close as the song list
        Shot(s, "organize.png");
        s.App.Window.Width = 1024;
        s.App.Window.Height = 680;
        AppHarness.Settle(200);
        s.App.Window.UpdateLayout();
        Shot(s, "organize-smallest.png");
    }

    private static void Shot(Setup s, string name)
    {
        Directory.CreateDirectory(AppHarness.ScreenshotDirectory);
        s.App.Window.CaptureRenderedFrame()?.Save(Path.Combine(AppHarness.ScreenshotDirectory, name), new PngBitmapEncoderOptions());
    }

    private static Control Named(Setup s, string name) => s.App.Window.FindControl<Control>(name)!;

    private static void Press(Setup s, Key key, RawInputModifiers keys, PhysicalKey physical)
    {
        s.App.Window.KeyPress(key, keys, physical, null);
        AppHarness.Settle(50);
    }

    [AvaloniaFact]
    public void It_takes_the_place_of_the_library_and_queue_and_the_decks_stay()
    {
        using var s = WithPanes();
        Assert.True(Named(s, "PanesGrid").IsEffectivelyVisible);

        var view = Open(s);

        Assert.True(view.IsEffectivelyVisible);
        Assert.True(Named(s, "DecksRow").IsEffectivelyVisible);
        Assert.False(Named(s, "PanesGrid").IsEffectivelyVisible);

        view.FindControl<Button>("DoneButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        AppHarness.Settle(50);

        Assert.False(view.IsEffectivelyVisible);
        Assert.True(Named(s, "PanesGrid").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Escape_goes_back_to_the_library()
    {
        using var s = WithPanes();
        Open(s);

        Press(s, Key.Escape, RawInputModifiers.None, PhysicalKey.Escape);

        Assert.False(s.App.ViewModel.IsOrganizeOpen);
    }

    [AvaloniaFact]
    public void Escape_during_a_drag_only_stops_the_drag()
    {
        using var s = WithPanes();
        Go(s.Vm.Right, s.Desktop);
        var view = Open(s);
        Window window = s.App.Window;
        Point from = RowAt(s, view, "RightPane", "Voice memo"), to = MiddleOf(s, view, "LeftPane");
        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left, RawInputModifiers.None);
        AppHarness.Settle(30);
        window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        AppHarness.Settle(30);

        view.FindControl<OrganizePaneView>("RightPane")!.SongList.Focus();
        Press(s, Key.Escape, RawInputModifiers.None, PhysicalKey.Escape);
        window.MouseUp(to, MouseButton.Left, RawInputModifiers.None);
        AppHarness.Settle(200);

        Assert.True(s.App.ViewModel.IsOrganizeOpen);
        Assert.True(File.Exists(Path.Combine(s.Desktop, "Voice memo.mp3")));
    }

    [AvaloniaFact]
    public void With_the_decks_hidden_the_air_line_stands_in_while_organizing()
    {
        using var s = WithPanes();
        s.App.ViewModel.DecksHidden = true;
        Assert.True(s.App.ViewModel.DecksInQueue);   // on top of the queue, which is about to go

        Open(s);

        Assert.False(s.App.ViewModel.DecksInQueue);
        Assert.True(s.App.ViewModel.ShowAirLine);
        Assert.True(Named(s, "AirLine").IsEffectivelyVisible);

        s.App.Window.CloseOrganize();
        AppHarness.Settle(50);
        Assert.True(s.App.ViewModel.DecksInQueue);
        Assert.False(s.App.ViewModel.ShowAirLine);
    }

    [AvaloniaFact]
    public void The_first_time_it_opens_each_side_is_where_it_was_left()
    {
        using var s = WithPanes(start: false);   // as the app does: the view is built before the sides have opened
        s.App.Settings.OrganizeLeftSource = OrganizePane.Files;
        s.App.Settings.OrganizeLeftFolder = s.Edits;
        s.App.Settings.OrganizeRightSource = OrganizePane.MyMusic;
        s.App.Settings.OrganizeRightFolder = s.Lindy;

        Open(s);

        Assert.Equal((OrganizePane.Files, s.Edits), (s.Vm.Left.SelectedSource, s.Vm.Left.Folder));
        Assert.Equal((OrganizePane.MyMusic, s.Lindy), (s.Vm.Right.SelectedSource, s.Vm.Right.Folder));
    }

    [AvaloniaFact]
    public void Clicking_a_source_tab_opens_that_source()
    {
        using var s = WithPanes();
        var view = Open(s);
        var side = view.FindControl<OrganizePaneView>("RightPane")!;
        Assert.Equal(OrganizePane.Files, s.Vm.Right.SelectedSource);

        side.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.TabStrip>().Single().SelectedItem = OrganizePane.MyMusic;
        AppHarness.WaitFor(() => !s.Vm.Right.IsListing);
        AppHarness.Settle(50);

        Assert.Equal(OrganizePane.MyMusic, s.Vm.Right.SelectedSource);
        Assert.Equal(s.App.MusicPath, s.Vm.Right.Folder);
    }

    [AvaloniaFact]
    public void A_right_clicked_folder_opens_on_the_left()
    {
        using var s = WithPanes();

        Open(s, s.Lindy);

        Assert.Equal(s.Lindy, s.Vm.Left.Folder);
    }

    [AvaloniaFact]
    public void A_move_can_still_be_undone_after_going_back_to_the_library()
    {
        using var s = WithPanes();
        string from = Path.Combine(s.Desktop, "Voice memo.mp3");
        Source(s.Vm.Left, OrganizePane.Files);
        Go(s.Vm.Left, s.Desktop);
        Go(s.Vm.Right, s.Edits);
        Open(s);
        Pick(s.Vm.Left, "Voice memo");
        Finish(s.Vm.MoveAsync());
        Assert.False(File.Exists(from));

        s.App.Window.CloseOrganize();
        Open(s);

        Assert.True(s.Vm.CanUndo);
        Finish(s.Vm.UndoAsync());
        Assert.True(File.Exists(from));
    }

    [AvaloniaFact]
    public void Going_back_to_the_library_mid_copy_carries_on_and_says_when_its_done()
    {
        using var s = WithPanes();
        using var release = new ManualResetEventSlim();
        s.Vm.SameDrive = (_, _) => false;
        s.Vm.CopyFile = (from, partial) =>
        {
            release.Wait(TimeSpan.FromSeconds(20));
            File.Copy(from, partial, overwrite: true);
        };
        Source(s.Vm.Left, OrganizePane.Files);
        Go(s.Vm.Left, s.Desktop);
        Go(s.Vm.Right, s.Edits);
        Open(s);
        Pick(s.Vm.Left, "Voice memo");
        Task copying = s.Vm.CopyAsync();
        AppHarness.WaitFor(() => s.Vm.IsBusy);

        s.App.Window.CloseOrganize();
        Assert.Contains("still on their way", s.App.ViewModel.Notice);

        release.Set();
        Finish(copying);
        Assert.True(File.Exists(Path.Combine(s.Edits, "Voice memo.mp3")));
        Assert.Contains("Copied 1 song", s.App.ViewModel.Notice);
    }

    [AvaloniaFact]
    public void Ctrl_F_finds_in_the_folder_not_in_the_hidden_library()
    {
        using var s = WithPanes();
        var view = Open(s);
        RawInputModifiers command = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

        Press(s, Key.F, command, PhysicalKey.F);

        Assert.True(view.FindControl<OrganizePaneView>("LeftPane")!.FilterBox.IsFocused);
    }
}
