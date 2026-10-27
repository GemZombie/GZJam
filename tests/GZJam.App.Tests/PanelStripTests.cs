using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using GZJam.App.Views;

namespace GZJam.App.Tests;

/// <summary>
/// The side panels fold into a strip that a tap opens again, rather than leaving a divider at the
/// screen's edge, where touch screens take swipes for the system.
/// </summary>
public sealed class PanelStripTests
{
    private static T Find<T>(AppHarness app, string name) where T : Control => app.Window.FindControl<T>(name)!;

    private static Point Middle(AppHarness app, string name)
    {
        var control = Find<Control>(app, name);
        return control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), app.Window)!.Value;
    }

    private static void Tap(AppHarness app, string name)
    {
        var at = Middle(app, name);
        app.Window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
        app.Window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
        AppHarness.Settle(150);
    }

    [AvaloniaFact]
    public void Dragged_to_the_edge_the_queue_folds_into_a_strip_and_a_tap_opens_it_as_it_was()
    {
        using var app = new AppHarness();
        double queue = Find<Control>(app, "QueuePanel").Bounds.Width, songs = Find<Control>(app, "TrackGrid").Bounds.Width;

        var from = Middle(app, "QueueSplitter");
        app.MouseDrag(from, new Point(1432, from.Y));
        AppHarness.Settle(150);
        app.Screenshot("queue-folded");

        Assert.True(app.Settings.QueuePanelFolded);   // remembered
        Assert.False(Find<Control>(app, "QueuePanel").IsVisible);
        Assert.True(Find<Control>(app, "QueueStrip").IsVisible);
        Assert.Equal(MainWindow.StripWidth, Find<Control>(app, "QueueStrip").Bounds.Width, 0);
        Assert.True(Find<Control>(app, "TrackGrid").Bounds.Width > songs + queue - MainWindow.StripWidth - 2, "the songs get the room");
        Assert.False(Find<Control>(app, "QueueSplitter").IsVisible);   // nothing to find at the edge

        Tap(app, "QueueStrip");   // a tap anywhere on it, no drag
        Assert.False(app.Settings.QueuePanelFolded);
        Assert.True(Find<Control>(app, "QueuePanel").IsVisible);
        Assert.Equal(queue, Find<Control>(app, "QueuePanel").Bounds.Width, 0);
    }

    [AvaloniaFact]
    public void The_arrows_in_the_headers_fold_the_panels_and_a_dragged_width_is_kept()
    {
        using var app = new AppHarness();
        var from = Middle(app, "TreeSplitter");
        app.MouseDrag(from, from + new Point(60, 0));   // wider: kept, not folded
        double library = Find<Control>(app, "LibraryPanel").Bounds.Width;
        Assert.Equal(library, app.Settings.LibraryPanelWidth!.Value, 0);
        Assert.False(app.Settings.LibraryPanelFolded);

        Find<Button>(app, "FoldLibraryButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Find<Button>(app, "FoldQueueButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        AppHarness.Settle(150);
        app.Screenshot("panels-folded");
        Assert.True(app.Settings.LibraryPanelFolded && app.Settings.QueuePanelFolded);
        Assert.True(Find<Control>(app, "LibraryStrip").IsVisible);
        Assert.True(Find<Control>(app, "QueueStrip").IsVisible);

        Tap(app, "LibraryStrip");
        Assert.Equal(library, Find<Control>(app, "LibraryPanel").Bounds.Width, 0);
    }

    [AvaloniaFact]
    public void GZJam_starts_with_the_panels_as_they_were_left()
    {
        using var app = new AppHarness(settings: new AppSettings { MatchLoudness = false, QueuePanelFolded = true, LibraryPanelWidth = 300 });
        Assert.True(Find<Control>(app, "QueueStrip").IsVisible);
        Assert.False(Find<Control>(app, "QueuePanel").IsVisible);
        Assert.Equal(300, Find<Control>(app, "LibraryPanel").Bounds.Width, 0);
    }
}
