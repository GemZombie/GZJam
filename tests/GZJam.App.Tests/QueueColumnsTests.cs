using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GZJam.App.Controls;

namespace GZJam.App.Tests;

/// <summary>Choosing what the queue shows of each song, from its menu (#131).</summary>
public sealed class QueueColumnsTests
{
    private static List<T> Shown<T>(AppHarness app, string part) where T : Control =>
        app.Window.FindControl<ListBox>("QueueList")!.GetVisualDescendants().OfType<T>()
            .Where(c => c.Classes.Contains(part) && c.IsEffectivelyVisible).ToList();

    private static MenuItem Item(AppHarness app, string header)
    {
        var menu = app.Window.FindControl<ContextMenu>("QueueMenu")!;
        menu.Open(app.Window.FindControl<ListBox>("QueueList")!);
        AppHarness.Settle(100);
        var item = app.Window.FindControl<MenuItem>("QueueColumnsMenu")!.Items.OfType<MenuItem>().Single(i => (string)i.Header! == header);
        menu.Close();
        return item;
    }

    [AvaloniaFact]
    public void The_queue_shows_what_the_DJ_ticks_and_remembers_it()
    {
        using var app = new AppHarness();
        app.ViewModel.Queue.Add(app.Library.Results.Take(4).ToList());
        AppHarness.Settle(200);

        // As before: BPM and time, no album.
        Assert.NotEmpty(Shown<TextBlock>(app, "q-bpm"));
        Assert.Empty(Shown<TextBlock>(app, "q-album"));
        Assert.False(Item(app, "Album").IsChecked);
        Assert.True(Item(app, "Time").IsChecked);

        Item(app, "Album").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Item(app, "Time").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        AppHarness.Settle(150);
        Assert.Equal(4, Shown<TextBlock>(app, "q-album").Count);
        Assert.Contains(Shown<TextBlock>(app, "q-album"), t => t.Text == app.ViewModel.Queue.Items[0].Track.Album);
        Assert.Empty(Shown<TextBlock>(app, "q-time"));
        Assert.Equal(["Genre", "Year", "Played", "Time"], app.Settings.HiddenQueueColumns);   // remembered
        Assert.True(Item(app, "Album").IsChecked);

        // Everything, in a wide queue (#178): a picture to look at.
        foreach (string name in app.Settings.HiddenQueueColumns.ToList())
            app.Window.SetQueueColumnShown(name, true);
        app.Window.ToggleQueueWide();
        AppHarness.Settle(200);
        app.Screenshot("queue-every-column");
        Assert.NotEmpty(Shown<TextBlock>(app, "q-year"));
        Assert.NotEmpty(Shown<AlbumArt>(app, "q-art"));
    }

    [AvaloniaFact]
    public void GZJam_starts_with_the_queue_showing_what_it_did()
    {
        using var app = new AppHarness(settings: new AppSettings { MatchLoudness = false, HiddenQueueColumns = ["Art", "BPM"] });
        app.ViewModel.Queue.Add(app.Library.Results.Take(2).ToList());
        AppHarness.Settle(200);
        Assert.Empty(Shown<AlbumArt>(app, "q-art"));
        Assert.Empty(Shown<TextBlock>(app, "q-bpm"));
        Assert.Equal(2, Shown<TextBlock>(app, "q-album").Count);
    }
}
