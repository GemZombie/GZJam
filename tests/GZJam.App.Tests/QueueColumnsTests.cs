using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GZJam.App.Controls;
using GZJam.App.Views;

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
    public void However_narrow_the_queue_nothing_overlaps_and_the_parts_give_way_in_order()
    {
        using var app = new AppHarness();
        app.ViewModel.Queue.Add(app.Library.Results.Take(4).ToList());
        foreach (var (name, _) in MainWindow.QueueColumns)
            app.Window.SetQueueColumnShown(name, true);
        var panes = app.Window.FindControl<Grid>("PanesGrid")!;

        List<FitRow> Rows() =>
        [
            app.Window.FindControl<FitRow>("AutoPlayRow")!, app.Window.FindControl<FitRow>("QueueTabsRow")!,
            .. app.Window.FindControl<ListBox>("QueueList")!.GetVisualDescendants().OfType<FitRow>(),
        ];
        // From where a dragged panel folds (MainWindow.FoldBelow) to the widest it goes.
        foreach (int width in new[] { 120, 150, 180, 220, 260, 320, 420, 560 })
        {
            panes.ColumnDefinitions[4].Width = new GridLength(width);
            AppHarness.Settle(150);
            if (width == 220)
                app.Screenshot("queue-narrow");
            Assert.Equal(6, Rows().Count);
            foreach (var row in Rows())
            {
                var shown = row.Children.Where(c => c.IsVisible && !row.IsDropped(c)).OrderBy(c => c.Bounds.X).ToList();
                for (int i = 1; i < shown.Count; i++)
                    Assert.True(shown[i].Bounds.X >= shown[i - 1].Bounds.Right - 0.5,
                        $"{width} px, {row.Name}: {shown[i]} over {shown[i - 1]}");
                Assert.True(shown[^1].Bounds.Right <= row.Bounds.Width + 0.5, $"{width} px, {row.Name}: past the end");
            }
        }

        // Narrowed, the album gives way before the BPM, and the title keeps its room while anything can still go.
        panes.ColumnDefinitions[4].Width = new GridLength(320);
        AppHarness.Settle(150);
        var song = Rows()[2];
        bool Dropped(string part) => song.IsDropped(song.Children.Single(c => c.Classes.Contains(part)));
        Assert.True(Dropped("q-album"));
        Assert.False(Dropped("q-bpm"));
        Assert.True(song.Children.Single(c => c.Classes.Contains("q-title")).Bounds.Width >= song.FillMinWidth);

        // At the default width with the default columns, everything shows, as before.
        using var plain = new AppHarness();
        plain.ViewModel.Queue.Add(plain.Library.Results.Take(1).ToList());
        AppHarness.Settle(200);
        var first = plain.Window.FindControl<ListBox>("QueueList")!.GetVisualDescendants().OfType<FitRow>().First();
        Assert.DoesNotContain(first.Children, c => c.IsVisible && first.IsDropped(c));
        foreach (string name in new[] { "AutoPlayRow", "QueueTabsRow" })
        {
            var row = plain.Window.FindControl<FitRow>(name)!;
            Assert.DoesNotContain(row.Children, c => row.IsDropped(c));
        }
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
