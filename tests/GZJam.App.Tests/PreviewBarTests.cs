using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using GZJam.App.ViewModels;
using GZJam.App.Views;
using Track = GZJam.Library.Track;

namespace GZJam.App.Tests;

/// <summary>
/// The song in the headphones, acted on from the preview bar (#386): previewed in one folder, the
/// song list moved on to another, it can still be queued, dragged or loaded from the bar.
/// </summary>
public sealed class PreviewBarTests
{
    private static FolderNodeViewModel Folder(AppHarness app, string name) =>
        app.Library.Roots.SelectMany(r => r.SelfAndDescendants()).First(n => n.Name == name);

    private static void Open(AppHarness app, string name)
    {
        app.Library.SelectedFolder = Folder(app, name);
        AppHarness.WaitFor(() => !app.Library.IsSearching);
        AppHarness.Settle(100);
    }

    private static PreviewBar Bar(AppHarness app) => app.Window.FindControl<PreviewBar>("LibraryPreview")!;

    private static Point Center(AppHarness app, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), app.Window)!.Value;

    /// <summary>A song of Swing's in the headphones, and the song list moved on to Blues.</summary>
    private static Track PreviewThenMoveOn(AppHarness app)
    {
        Open(app, "Swing");
        Track song = app.Library.Results[0];
        app.ViewModel.Preview!.Toggle(song);
        Open(app, "Blues");
        Assert.DoesNotContain(song, app.Library.Results);
        Assert.Same(song, app.ViewModel.Preview.Song);
        return song;
    }

    [AvaloniaFact]
    public void QUEUE_on_the_bar_queues_the_song_in_the_headphones_after_the_list_has_moved_on()
    {
        using var app = new AppHarness();
        Track song = PreviewThenMoveOn(app);
        var bar = Bar(app);
        Assert.True(bar.QueueKey.IsEffectivelyVisible);
        app.Screenshot("preview-bar-actions");

        bar.QueueKey.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        AppHarness.Settle(100);

        Assert.Equal(song.Path, Assert.Single(app.ViewModel.Queue.Items).Track.Path);
        Assert.Same(song, app.ViewModel.Preview!.Track);   // still in the headphones
        Assert.Contains(song.DisplayTitle, app.ViewModel.Notice);

        // Again: it's in the queue already, so the usual notice says so, with Undo.
        bar.QueueKey.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        AppHarness.Settle(100);
        Assert.Equal(2, app.ViewModel.Queue.Items.Count);
        Assert.Contains("already", app.ViewModel.Notice, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void The_song_drags_from_the_bar_into_the_queue()
    {
        using var app = new AppHarness();
        Track song = PreviewThenMoveOn(app);
        var queue = app.Window.FindControl<ListBox>("QueueList")!;

        app.MouseDrag(Center(app, Bar(app).Grip), Center(app, queue));

        Assert.Equal(song.Path, Assert.Single(app.ViewModel.Queue.Items).Track.Path);
        Assert.Same(song, app.ViewModel.Preview!.Track);
        Assert.False(app.Window.IsDragging);
    }

    [AvaloniaFact]
    public void The_bars_menu_plays_it_next_loads_it_and_finds_its_folder()
    {
        using var app = new AppHarness();
        Track song = PreviewThenMoveOn(app);
        app.ViewModel.AddToQueue([app.Library.Results[0]]);

        var menu = app.Window.PreviewMenu();
        var items = menu.Items.OfType<MenuItem>().ToList();
        Assert.Equal(["Add to queue", "Play next", "Load to Deck 1", "Load to Deck 2", "Add to playlist", "Song info…", "Show in folder tree"],
            items.Select(i => (string)i.Header!));
        void Click(string header) =>
            items.Single(i => (string?)i.Header == header).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Click("Play next");
        AppHarness.Settle(100);
        Assert.Equal(song.Path, app.ViewModel.Queue.Items[0].Track.Path);   // ahead of the song queued before

        Click("Load to Deck 2");
        AppHarness.WaitFor(() => app.ViewModel.Decks[1].Track?.Path == song.Path);

        Click("Show in folder tree");
        AppHarness.WaitFor(() => !app.Library.IsSearching);
        Assert.EndsWith(app.Library.SelectedFolder!.Name, song.Folder);   // its own folder, in Swing
        Assert.Contains(app.Library.Results, t => t.Path == song.Path);
    }

    [AvaloniaFact]
    public void Right_clicking_the_bar_opens_the_songs_menu_and_nothing_previewing_has_none()
    {
        using var app = new AppHarness();
        PreviewThenMoveOn(app);
        var bar = Bar(app);
        bar.Grip.RaiseEvent(new ContextRequestedEventArgs());
        AppHarness.Settle(100);
        Assert.True(app.Window.PreviewMenu().IsOpen);
        app.Window.PreviewMenu().Close();

        app.ViewModel.Preview!.Stop();
        Assert.Null(app.ViewModel.Preview.Song);
        Assert.Empty(app.Window.PreviewMenu().Items);
    }
}
