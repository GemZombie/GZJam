using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.VisualTree;
using GZJam.App.Views;

namespace GZJam.App.Tests;

/// <summary>
/// Dragging with a finger: raw touch events through the window's input, as a touchscreen sends
/// them. (The operating system's drag and drop, used before, ignored a finger on Windows.)
/// </summary>
public sealed class TouchDragTests
{
    private sealed class Finger(MainWindow window)
    {
        /// <summary>Puts the finger down, slides it to <paramref name="to"/> in steps, and lifts it.</summary>
        public void Swipe(Point from, Point to, Action? over = null)
        {
            var finger = window.TouchBegin(from, RawInputModifiers.None);
            for (int i = 1; i <= 10; i++)
            {
                window.TouchMove(finger, from + (to - from) * (i / 10.0), RawInputModifiers.None);
                AppHarness.Settle(10);
            }
            over?.Invoke();
            window.TouchEnd(finger, to, RawInputModifiers.None);
            AppHarness.Settle(100);
        }
    }

    private static Point RowPoint(AppHarness app, object track)
    {
        var grid = app.Window.FindControl<DataGrid>("TrackGrid")!;
        var row = grid.GetVisualDescendants().OfType<DataGridRow>().First(r => r.DataContext == track);
        return row.TranslatePoint(new Point(300, row.Bounds.Height / 2), app.Window)!.Value;
    }

    /// <summary>
    /// A real finger: moves arrive a couple of pixels at a time and wobble up and down, so the
    /// list starts reacting (its touch scrolling starts after 5 px) before a drag is recognised.
    /// </summary>
    private static void RealSwipe(MainWindow window, Point from, Point to, int holdMs = 0, Action? over = null)
    {
        var finger = window.TouchBegin(from, RawInputModifiers.None);
        AppHarness.Settle(30);
        if (holdMs > 0)
            AppHarness.Settle(holdMs);
        int steps = (int)Math.Max(10, ((Vector)(to - from)).Length / 2.5);
        for (int i = 1; i <= steps; i++)
        {
            double wobble = Math.Sin(i * 0.9) * 3 + (i < 6 ? i * 0.7 : 0);   // drifts down a little at first
            window.TouchMove(finger, from + (to - from) * (i / (double)steps) + new Vector(0, wobble), RawInputModifiers.None);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        AppHarness.Settle(30);
        over?.Invoke();
        window.TouchEnd(finger, to, RawInputModifiers.None);
        AppHarness.Settle(100);
    }

    [AvaloniaFact]
    public void A_real_finger_drags_a_song_from_the_title_into_the_queue()
    {
        using var app = new AppHarness();
        var song = app.Library.Results[4];
        var queueArea = app.Window.FindControl<Panel>("QueueArea")!;
        Point queue = queueArea.TranslatePoint(new Point(queueArea.Bounds.Width / 2, 60), app.Window)!.Value;
        var grid = app.Window.FindControl<DataGrid>("TrackGrid")!;
        var row = grid.GetVisualDescendants().OfType<DataGridRow>().First(r => r.DataContext == song);
        Point title = row.TranslatePoint(new Point(110, row.Bounds.Height / 2), app.Window)!.Value;

        RealSwipe(app.Window, title, new Point(queue.X, title.Y - 30));
        Assert.Equal([song], app.ViewModel.Queue.Items.Select(i => i.Track));
    }

    [AvaloniaFact]
    public void A_finger_held_on_a_song_picks_it_up_to_drag_anywhere_or_opens_the_menu_if_lifted()
    {
        using var app = new AppHarness();
        var song = app.Library.Results[2];
        Point from = RowPoint(app, song);

        // Held, then dragged straight up onto Deck 1 (straight up would otherwise scroll the list).
        var deck = app.Window.FindControl<DeckView>("Deck1View")!;
        Point deckPoint = deck.TranslatePoint(new Point(deck.Bounds.Width / 2, deck.Bounds.Height / 2), app.Window)!.Value;
        RealSwipe(app.Window, from, new Point(from.X, deckPoint.Y), holdMs: 600, over: () => Assert.True(app.Window.IsDragging));
        AppHarness.WaitFor(() => app.ViewModel.Deck1!.Track == song);

        // Held and lifted without moving: the song's menu, as a right-click would.
        var menu = app.Window.FindControl<ContextMenu>("TrackMenu")!;
        var finger = app.Window.TouchBegin(RowPoint(app, app.Library.Results[5]), RawInputModifiers.None);
        AppHarness.Settle(700);
        Assert.False(menu.IsOpen);   // not while the finger is still down: it may yet drag
        app.Window.TouchEnd(finger, RowPoint(app, app.Library.Results[5]), RawInputModifiers.None);
        AppHarness.Settle(200);
        Assert.True(menu.IsOpen);
        Assert.False(app.Window.IsDragging);
        menu.Close();
    }

    [AvaloniaFact]
    public void A_real_finger_swiping_up_still_scrolls_the_library()
    {
        using var app = new AppHarness();
        var grid = app.Window.FindControl<DataGrid>("TrackGrid")!;
        var bar = grid.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>()
            .First(b => b.Orientation == Avalonia.Layout.Orientation.Vertical);
        Assert.Equal(0, bar.Value);

        Point from = RowPoint(app, app.Library.Results[8]);
        RealSwipe(app.Window, from, from - new Vector(0, 200));
        Assert.True(bar.Value > 10, $"scrolled {bar.Value} px");
        Assert.Empty(app.ViewModel.Queue.Items);
    }

    [AvaloniaFact]
    public void A_finger_drags_a_song_from_the_library_into_the_queue()
    {
        using var app = new AppHarness();
        var song = app.Library.Results[3];
        var queueArea = app.Window.FindControl<Panel>("QueueArea")!;
        Point queue = queueArea.TranslatePoint(new Point(queueArea.Bounds.Width / 2, 40), app.Window)!.Value;
        Point from = RowPoint(app, song);

        // Sideways, towards the queue: a drag. The tag rides above the finger.
        new Finger(app.Window).Swipe(from, new Point(queue.X, from.Y + (queue.Y - from.Y) * 0.2), over: null);
        Assert.Equal([song], app.ViewModel.Queue.Items.Select(i => i.Track));
        Assert.False(app.Window.IsDragging);
    }

    [AvaloniaFact]
    public void A_finger_drags_a_song_onto_a_deck()
    {
        using var app = new AppHarness();
        var song = app.Library.Results[2];
        var deck = app.Window.FindControl<DeckView>("Deck2View")!;
        Point to = deck.TranslatePoint(new Point(deck.Bounds.Width / 2, deck.Bounds.Height / 2), app.Window)!.Value;
        Point from = RowPoint(app, song);

        // First mostly sideways (so it's a drag, not a scroll), then up to the deck.
        var finger = new Finger(app.Window);
        finger.Swipe(from, to, over: () =>
        {
            Assert.True(app.Window.IsDragging);
            Assert.Contains("drop-ok", deck.FindControl<Border>("Frame")!.Classes);
        });
        AppHarness.WaitFor(() => app.ViewModel.Deck2!.Track == song);
    }

    [AvaloniaFact]
    public void Holding_a_song_over_the_queue_tab_opens_the_queue()
    {
        using var app = new AppHarness();
        app.ViewModel.ShowHistory = true;
        AppHarness.Settle(100);
        var song = app.Library.Results[1];
        var tab = app.Window.FindControl<Avalonia.Controls.Primitives.ToggleButton>("QueueTab")!;
        Point overTab = tab.TranslatePoint(new Point(tab.Bounds.Width / 2, tab.Bounds.Height / 2), app.Window)!.Value;
        var queueArea = app.Window.FindControl<Panel>("QueueArea")!;

        new Finger(app.Window).Swipe(RowPoint(app, song), overTab, over: () =>
        {
            Assert.False(app.ViewModel.ShowHistory);
            AppHarness.Settle(100);
            Assert.True(queueArea.IsEffectivelyVisible);
        });
        Assert.Empty(app.ViewModel.Queue.Items);   // let go on the tab, not in the queue: nothing added
    }

    [AvaloniaFact]
    public void An_upward_swipe_on_the_library_scrolls_rather_than_drags()
    {
        using var app = new AppHarness();
        Point from = RowPoint(app, app.Library.Results[6]);
        new Finger(app.Window).Swipe(from, from - new Vector(4, 120));
        Assert.False(app.Window.IsDragging);
        Assert.Empty(app.ViewModel.Queue.Items);
    }
}
