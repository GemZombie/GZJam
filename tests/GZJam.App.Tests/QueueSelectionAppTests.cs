using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>
/// Several songs selected in the queue (#409): Ctrl-click and Shift-click pick them, and dragging
/// any of them takes them all, onto a playlist or to a new place in the queue.
/// </summary>
public sealed class QueueSelectionAppTests
{
    private static ListBox Queue(AppHarness app) => app.Window.FindControl<ListBox>("QueueList")!;

    private static Point QueueRow(AppHarness app, int index)
    {
        var row = (Control)Queue(app).ContainerFromIndex(index)!;
        return row.TranslatePoint(new Point(80, row.Bounds.Height / 2), app.Window)!.Value;
    }

    private static void Click(AppHarness app, int index, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        app.Window.MouseDown(QueueRow(app, index), MouseButton.Left, modifiers);
        app.Window.MouseUp(QueueRow(app, index), MouseButton.Left, modifiers);
        AppHarness.Settle(50);
    }

    private static List<int> Selected(AppHarness app) => [.. Queue(app).Selection.SelectedIndexes.Order()];

    private static List<Track> Queued(AppHarness app, int count)
    {
        var songs = app.Library.Results.Take(count).ToList();
        app.ViewModel.Queue.Add(songs);
        AppHarness.Settle(150);
        return songs;
    }

    private static Point PlaylistPoint(AppHarness app, string name)
    {
        PlaylistAppTests.ShowPlaylists(app);
        var item = app.Window.FindControl<ListBox>("PlaylistList")!.GetVisualDescendants().OfType<ListBoxItem>()
            .First(i => ((PlaylistInfo)i.DataContext!).Name == name);
        return item.TranslatePoint(new Point(40, item.Bounds.Height / 2), app.Window)!.Value;
    }

    [AvaloniaFact]
    public void Several_queue_songs_dragged_onto_a_playlist_all_go_in_queue_order()
    {
        using var app = new AppHarness();
        var songs = Queued(app, 5);
        var saturday = app.ViewModel.CreatePlaylist("Saturday", null)!;
        AppHarness.Settle(150);

        Click(app, 3);
        Click(app, 1, RawInputModifiers.Control);
        Click(app, 4, RawInputModifiers.Control);
        Assert.Equal([1, 3, 4], Selected(app));

        // Pressing one of them to drag it doesn't narrow the selection: all three go.
        app.MouseDrag(QueueRow(app, 3), PlaylistPoint(app, "Saturday"));

        app.Library.OpenPlaylist(saturday.Id);
        AppHarness.Settle();
        Assert.Equal([songs[1].Path, songs[3].Path, songs[4].Path], app.Library.Results.Select(t => t.Path));
        Assert.Equal(5, app.ViewModel.Queue.Items.Count);   // copied, not moved out of the queue
        Assert.Equal([1, 3, 4], Selected(app));

        // Into the open playlist's song list too.
        var grid = app.Window.FindControl<DataGrid>("TrackGrid")!;
        Point songList = grid.TranslatePoint(new Point(grid.Bounds.Width / 2, grid.Bounds.Height - 30), app.Window)!.Value;
        app.MouseDrag(QueueRow(app, 1), songList);
        AppHarness.Settle();
        Assert.Equal(new[] { songs[1], songs[3], songs[4], songs[1], songs[3], songs[4] }.Select(s => s.Path), app.Library.Results.Select(t => t.Path));
    }

    [AvaloniaFact]
    public void Shift_click_selects_a_range_and_a_drag_moves_them_all_while_a_click_picks_one()
    {
        using var app = new AppHarness();
        var songs = Queued(app, 6);

        Click(app, 1);
        Click(app, 3, RawInputModifiers.Shift);
        Assert.Equal([1, 2, 3], Selected(app));

        // Dragged by the middle one to the top of the queue: all three move, in order.
        var first = (Control)Queue(app).ContainerFromIndex(0)!;
        app.MouseDrag(QueueRow(app, 2), first.TranslatePoint(new Point(40, 4), app.Window)!.Value);
        Assert.Equal([songs[1], songs[2], songs[3], songs[0], songs[4], songs[5]], app.ViewModel.Queue.Items.Select(i => i.Track));

        // A click without a drag on one of several selected picks that one alone, as in a file manager.
        // (Rows apart: two quick clicks on one row would be a double-click, playing it.)
        Click(app, 0);
        Click(app, 2, RawInputModifiers.Shift);
        Assert.Equal([0, 1, 2], Selected(app));
        Click(app, 1);
        Assert.Equal([1], Selected(app));
        Assert.Equal(6, app.ViewModel.Queue.Items.Count);
    }
}
