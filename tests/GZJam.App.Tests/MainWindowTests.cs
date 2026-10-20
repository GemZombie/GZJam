using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.VisualTree;
using GZJam.App.ViewModels;
using GZJam.App.Views;
using GZJam.Library;

namespace GZJam.App.Tests;

public sealed class MainWindowTests
{
    private static FolderNodeViewModel Folder(AppHarness app, string relativePath) =>
        app.Library.Roots.SelectMany(r => r.SelfAndDescendants()).Single(n => n.Node.RelativePath == relativePath);

    [AvaloniaFact]
    public void Shows_the_folder_tree_and_every_track_after_the_first_scan()
    {
        using var app = new AppHarness();

        FolderNodeViewModel root = Assert.Single(app.Library.Roots);
        Assert.Equal(15, root.Node.TrackCount);
        Assert.Equal(["Blues", "Swing"], root.Children.Select(c => c.Name));
        Assert.Equal(15, app.Library.Results.Count);
        Assert.Equal("15 tracks", app.Library.ResultSummary);
        Assert.False(app.Library.HasScope);
    }

    [AvaloniaFact]
    public void Selecting_a_folder_lists_everything_beneath_it()
    {
        using var app = new AppHarness();

        app.Library.SelectedFolder = Folder(app, "Swing/Lindy Hop");
        AppHarness.Settle();

        Assert.Equal(11, app.Library.Results.Count);
        Assert.All(app.Library.Results, t => Assert.StartsWith("Swing/Lindy Hop/", t.Folder));
        Assert.Equal("in Music › Swing › Lindy Hop · all subfolders", app.Library.ScopeText);
    }

    [AvaloniaFact]
    public void Typing_a_search_filters_within_the_selected_folder()
    {
        using var app = new AppHarness();
        app.Library.SelectedFolder = Folder(app, "Swing/Lindy Hop");
        AppHarness.Settle();

        app.Window.FindControl<TextBox>("SearchBox")!.Focus();
        app.Window.KeyTextInput("basie jump");
        AppHarness.Settle();

        Assert.Equal("basie jump", app.Library.SearchText);
        Assert.Equal(["One O'Clock Jump", "Jumpin' at the Woodside"], app.Library.Results.Select(t => t.DisplayTitle));
    }

    [AvaloniaFact]
    public void Clearing_the_scope_searches_the_whole_library()
    {
        using var app = new AppHarness();
        app.Library.SelectedFolder = Folder(app, "Swing/Balboa");
        app.Library.SearchText = "blues";
        AppHarness.Settle();
        Assert.Empty(app.Library.Results);
        Assert.True(app.Library.HasNoResults);
        Assert.Equal("Nothing matches \"blues\" in Balboa.", app.Library.EmptyMessage);

        app.Library.ClearScopeCommand.Execute(null);
        AppHarness.Settle();

        Assert.Equal(["Confessin' the Blues", "Hootie Blues", "Swingin' the Blues"],
            app.Library.Results.Select(t => t.DisplayTitle).Order(StringComparer.Ordinal));
    }

    [AvaloniaFact]
    public void Enter_queues_the_selection_and_shift_enter_plays_it_next()
    {
        using var app = new AppHarness();
        var grid = app.Window.FindControl<DataGrid>("TrackGrid")!;

        grid.SelectedIndex = 0;
        grid.Focus();
        app.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        grid.SelectedIndex = 1;
        app.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        grid.SelectedIndex = 2;
        app.Window.KeyPress(Key.Enter, RawInputModifiers.Shift, PhysicalKey.Enter, null);
        AppHarness.Settle(100);

        var results = app.Library.Results;
        Assert.Equal([results[2], results[0], results[1]], app.ViewModel.Queue.Items.Select(i => i.Track));
        Assert.Equal([1, 2, 3], app.ViewModel.Queue.Items.Select(i => i.Number));
    }

    [AvaloniaFact]
    public void Dropping_tracks_on_the_queue_inserts_them_where_they_land()
    {
        using var app = new AppHarness();
        var queue = app.ViewModel.Queue;
        var tracks = app.Library.Results;
        queue.Add([tracks[0], tracks[1], tracks[2]]);
        AppHarness.Settle(150);

        var list = app.Window.FindControl<ListBox>("QueueList")!;
        var second = (Control)list.ContainerFromIndex(1)!;
        // Just above the middle of the second item = insert before it.
        Point target = second.TranslatePoint(new Point(40, 4), app.Window)!.Value;

        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(MainWindow.TrackFormat, new MainWindow.TrackDrag([tracks[5]], null)));
        app.Window.DragDrop(target, RawDragEventType.DragEnter, data, DragDropEffects.Copy, RawInputModifiers.None);
        app.Window.DragDrop(target, RawDragEventType.DragOver, data, DragDropEffects.Copy, RawInputModifiers.None);
        Assert.True(app.Window.FindControl<Border>("QueueDropLine")!.IsVisible);
        app.Window.DragDrop(target, RawDragEventType.Drop, data, DragDropEffects.Copy, RawInputModifiers.None);
        AppHarness.Settle(100);

        Assert.Equal([tracks[0], tracks[5], tracks[1], tracks[2]], queue.Items.Select(i => i.Track));
        Assert.False(app.Window.FindControl<Border>("QueueDropLine")!.IsVisible);
    }

    [AvaloniaFact]
    public void Dragging_within_the_queue_reorders_it()
    {
        using var app = new AppHarness();
        var queue = app.ViewModel.Queue;
        var tracks = app.Library.Results;
        queue.Add([tracks[0], tracks[1], tracks[2]]);
        AppHarness.Settle(150);

        var list = app.Window.FindControl<ListBox>("QueueList")!;
        var first = (Control)list.ContainerFromIndex(0)!;
        Point top = first.TranslatePoint(new Point(40, 4), app.Window)!.Value;

        var data = new DataTransfer();
        var moving = queue.Items[2];
        data.Add(DataTransferItem.Create(MainWindow.TrackFormat, new MainWindow.TrackDrag([moving.Track], [moving])));
        app.Window.DragDrop(top, RawDragEventType.DragOver, data, DragDropEffects.Move, RawInputModifiers.None);
        app.Window.DragDrop(top, RawDragEventType.Drop, data, DragDropEffects.Move, RawInputModifiers.None);
        AppHarness.Settle(100);

        Assert.Equal([tracks[2], tracks[0], tracks[1]], queue.Items.Select(i => i.Track));
    }

    [AvaloniaFact]
    public void Remembers_the_selected_folder_between_sessions()
    {
        AppSettings settings;
        string musicPath;
        using (var first = new AppHarness())
        {
            first.Library.SelectedFolder = Folder(first, "Swing/Balboa");
            AppHarness.Settle();
            first.ViewModel.SaveState();
            settings = first.Settings;
            musicPath = first.MusicPath;
            Assert.Equal("Swing/Balboa", settings.SelectedFolder);
            Assert.Equal(musicPath, settings.SelectedRootPath);

            using var second = new AppHarness(musicPath, settings);
            Assert.Equal("Swing/Balboa", second.Library.SelectedFolder?.Node.RelativePath);
            Assert.True(second.Library.SelectedFolder!.Parent!.IsExpanded);
            Assert.Equal(2, second.Library.Results.Count);
        }
    }

    [AvaloniaFact]
    public void Show_in_folder_tree_jumps_to_the_tracks_album()
    {
        using var app = new AppHarness();
        app.Library.SearchText = "liza";
        AppHarness.Settle();
        Track track = Assert.Single(app.Library.Results);

        app.Library.RevealTrack(track);
        AppHarness.Settle();

        Assert.Equal("", app.Library.SearchText);
        Assert.Equal(track.Folder, app.Library.SelectedFolder?.Node.RelativePath);
        Assert.Equal(3, app.Library.Results.Count);
    }

    [AvaloniaFact]
    public void New_files_appear_without_a_manual_rescan()
    {
        using var app = new AppHarness();
        Assert.Equal(15, app.Library.Results.Count);

        GZJam.Testing.TestMp3.Write(Path.Combine(app.MusicPath, "Swing", "Shag", "New Band", "New Album", "01 - Fresh Track.mp3"),
            new GZJam.Testing.TestMp3.Tags("Fresh Track", "New Band"));

        // The watcher waits for the folder to go quiet (3 s), then rescans.
        AppHarness.WaitFor(() => app.Library.Results.Count == 16, TimeSpan.FromSeconds(20));
        Assert.Contains(app.Library.Roots[0].SelfAndDescendants(), n => n.Name == "Shag");
    }
}
