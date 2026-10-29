using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GZJam.App.Controls;
using GZJam.App.Views;

namespace GZJam.App.Tests;

/// <summary>Choosing what the queue shows of each song, from its menu (#131), and its column names (#188).</summary>
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
                AssertFits(row, $"{width} px");
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
            Assert.False(row.Children.Any(row.IsDropped), $"{name}, {row.Bounds.Width} px: "
                + string.Join(", ", row.Children.Select(c => $"{c.GetType().Name}{(row.IsDropped(c) ? " (gone)" : "")} {c.DesiredSize.Width}")));
        }
    }

    /// <summary>No shown part of <paramref name="row"/> is over another, or past its end.</summary>
    internal static void AssertFits(FitRow row, string at)
    {
        var shown = row.Children.Where(c => c.IsVisible && !row.IsDropped(c)).OrderBy(c => c.Bounds.X).ToList();
        for (int i = 1; i < shown.Count; i++)
            Assert.True(shown[i].Bounds.X >= shown[i - 1].Bounds.Right - 0.5, $"{at}, {row.Name}: {shown[i]} over {shown[i - 1]}");
        if (shown.Count > 0)
            Assert.True(shown[^1].Bounds.Right <= row.Bounds.Width + 0.5, $"{at}, {row.Name}: past the end");
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

    private static FitRow Header(AppHarness app) => app.Window.FindControl<FitRow>("QueueHeader")!;

    private static FitRow FirstRow(AppHarness app) =>
        app.Window.FindControl<ListBox>("QueueList")!.GetVisualDescendants().OfType<FitRow>().First();

    private static Control Part(FitRow row, string key) => row.Children.Single(c => FitRow.GetKey(c) == key);

    private static double Left(AppHarness app, Control part) => part.TranslatePoint(default, app.Window)!.Value.X;

    private static Point At(AppHarness app, Control part, double x) =>
        part.TranslatePoint(new Point(x, part.Bounds.Height / 2), app.Window)!.Value;

    [AvaloniaFact]
    public void The_column_names_line_up_with_the_songs_and_give_way_alike()
    {
        using var app = new AppHarness();
        app.ViewModel.Queue.Add(app.Library.Results.Take(3).ToList());
        foreach (var (name, _) in MainWindow.QueueColumns)
            app.Window.SetQueueColumnShown(name, true);
        var panes = app.Window.FindControl<Grid>("PanesGrid")!;
        foreach (int width in new[] { 180, 260, 320, 420, 560 })
        {
            panes.ColumnDefinitions[4].Width = new GridLength(width);
            AppHarness.Settle(150);
            AssertFits(Header(app), $"{width} px");
            foreach (string key in MainWindow.QueueColumnOrder.Where(k => k != "Title"))
            {
                Control name = Part(Header(app), key), part = Part(FirstRow(app), key);
                Assert.Equal(FirstRow(app).IsDropped(part), Header(app).IsDropped(name));
                if (!Header(app).IsDropped(name))
                    Assert.Equal(Left(app, part), Left(app, name), 1);
            }
        }
        app.Screenshot("queue-column-names");
        var names = Header(app).Children.OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !Header(app).IsDropped(t) && t.Text is { Length: > 0 });
        Assert.All(names.Where(t => FitRow.GetKey(t) != "Title"), t =>
        {
            Assert.False(t.TextLayout.TextLines.Any(l => l.HasCollapsed), $"{t.Text} cut short");
            Assert.True(t.TextLayout.WidthIncludingTrailingWhitespace <= t.Bounds.Width + Header(app).Spacing,
                $"{t.Text}: {t.TextLayout.WidthIncludingTrailingWhitespace:0.0} px in {t.Bounds.Width} and the gap");
        });
    }

    [AvaloniaFact]
    public void Dragging_a_name_moves_its_column_and_dragging_the_line_beside_it_resizes_it_both_remembered()
    {
        using var app = new AppHarness();
        app.ViewModel.Queue.Add(app.Library.Results.Take(3).ToList());
        app.Window.SetQueueColumnShown("Album", true);
        app.Window.FindControl<Grid>("PanesGrid")!.ColumnDefinitions[4].Width = new GridLength(560);
        AppHarness.Settle(200);

        // BPM dragged in front of the title.
        Control bpm = Part(Header(app), "BPM"), title = Part(Header(app), "Title");
        Point from = At(app, bpm, 5), to = At(app, title, 3);
        app.Window.MouseMove(from);
        app.Window.MouseDown(from, MouseButton.Left);
        app.Window.MouseMove(from - new Point(20, 0), RawInputModifiers.LeftMouseButton);
        app.Window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        Assert.True(app.Window.FindControl<Border>("QueueColumnDropLine")!.IsVisible);
        app.Window.MouseUp(to, MouseButton.Left);
        AppHarness.Settle(150);
        Assert.True(Left(app, Part(FirstRow(app), "BPM")) < Left(app, Part(FirstRow(app), "Title")));
        Assert.True(Left(app, Part(Header(app), "BPM")) < Left(app, Part(Header(app), "Title")));
        Assert.Equal(["Number", "Art", "BPM", "Title"], app.Settings.QueueColumnOrder!.Take(4));
        Assert.False(app.Window.FindControl<Border>("QueueColumnDropLine")!.IsVisible);

        // Album, after the title, is resized by the line before it (#198): dragged 40 to the left, the
        // line stays under the pointer and Album is wider in the names and the songs alike, its end
        // where it was.
        var line = app.Window.FindControl<Border>("QueueColumnDropLine")!;
        Control album = Part(Header(app), "Album");
        double before = album.Bounds.Width, end = At(app, album, album.Bounds.Width).X;
        Point edge = At(app, album, -album.Margin.Left - Header(app).Spacing / 2);
        app.Window.MouseMove(edge);
        app.Window.MouseDown(edge, MouseButton.Left);
        app.Window.MouseMove(edge - new Point(40, 0), RawInputModifiers.LeftMouseButton);
        app.Window.UpdateLayout();
        Assert.True(line.IsVisible);
        Assert.Equal(edge.X - 40, line.TranslatePoint(new Point(1, 0), app.Window)!.Value.X, 1);
        app.Window.MouseUp(edge - new Point(40, 0), MouseButton.Left);
        AppHarness.Settle(150);
        album = Part(Header(app), "Album");
        Assert.Equal(before + 40, album.Bounds.Width, 1);
        Assert.Equal(end, At(app, album, album.Bounds.Width).X, 1);
        Assert.Equal(album.Bounds.Width, Part(FirstRow(app), "Album").Bounds.Width, 1);
        Assert.Equal(before + 40 + 4, app.Settings.QueueColumnWidths["Album"], 1);   // its margin included
        Assert.False(line.IsVisible);

        // BPM, now before the title, by the line after it: 10 to the right, its start where it was.
        Control bpmName = Part(Header(app), "BPM");
        double bpmWidth = bpmName.Bounds.Width, bpmStart = Left(app, bpmName);
        edge = At(app, bpmName, bpmName.Bounds.Width + Header(app).Spacing / 2);
        app.Window.MouseMove(edge);
        app.Window.MouseDown(edge, MouseButton.Left);
        app.Window.MouseMove(edge + new Point(10, 0), RawInputModifiers.LeftMouseButton);
        app.Window.MouseUp(edge + new Point(10, 0), MouseButton.Left);
        AppHarness.Settle(150);
        Assert.Equal(bpmWidth + 10, Part(Header(app), "BPM").Bounds.Width, 1);
        Assert.Equal(bpmStart, Left(app, Part(Header(app), "BPM")), 1);

        // Dragged far, Album takes no more than the title can spare: nothing gives way under the pointer.
        album = Part(Header(app), "Album");
        edge = At(app, album, -album.Margin.Left - Header(app).Spacing / 2);
        app.Window.MouseMove(edge);
        app.Window.MouseDown(edge, MouseButton.Left);
        app.Window.MouseMove(edge - new Point(400, 0), RawInputModifiers.LeftMouseButton);
        app.Window.MouseUp(edge - new Point(400, 0), MouseButton.Left);
        AppHarness.Settle(150);
        Assert.DoesNotContain(Header(app).Children, Header(app).IsDropped);
        Assert.DoesNotContain(FirstRow(app).Children, FirstRow(app).IsDropped);
        Assert.True(Part(Header(app), "Title").Bounds.Width >= Header(app).FillMinWidth - 1);
        app.Window.ResizeQueueColumn("Album", 2000);
        Assert.Equal(MainWindow.QueueColumnSizes["Album"].Most, app.Settings.QueueColumnWidths["Album"]);
        app.Screenshot("queue-columns-moved");

        // As they came, from the menu.
        app.Window.ResetQueueColumns();
        AppHarness.Settle(150);
        Assert.Null(app.Settings.QueueColumnOrder);
        Assert.Empty(app.Settings.QueueColumnWidths);
        Assert.True(Left(app, Part(FirstRow(app), "Title")) < Left(app, Part(FirstRow(app), "BPM")));
    }

    [AvaloniaFact]
    public void The_lines_to_drag_show_between_the_names_and_light_up_under_the_pointer()
    {
        using var app = new AppHarness();
        app.ViewModel.Queue.Add(app.Library.Results.Take(3).ToList());
        app.Window.SetQueueColumnShown("Album", true);
        app.Window.FindControl<Grid>("PanesGrid")!.ColumnDefinitions[4].Width = new GridLength(560);
        AppHarness.Settle(200);

        // A line beside each name that resizes, and only those: in the gap on the title's side.
        var header = Header(app);
        Assert.Equal(MainWindow.QueueColumnSizes.Keys.Order(), header.Children.Where(FitRow.GetDivided).Select(c => FitRow.GetKey(c)!).Order());
        var shown = header.Ordered().Where(c => c.IsVisible && !header.IsDropped(c)).ToList();
        Assert.Contains(shown, FitRow.GetDivided);
        for (int i = 1; i < shown.Count; i++)
        {
            if (!FitRow.GetDivided(shown[i]))
                continue;
            var line = header.DividerOf(shown[i])!.Value;
            Assert.True(line.Left);
            Assert.InRange(line.X, shown[i - 1].Bounds.Right, shown[i].Bounds.Left);
        }

        // Under the pointer, Album's line lights up; away from it, it doesn't.
        Control album = Part(header, "Album");
        Point onLine = header.TranslatePoint(new Point(header.DividerOf(album)!.Value.X, header.Bounds.Height / 2), app.Window)!.Value;
        app.Window.MouseMove(onLine);
        Assert.Equal("Album", app.Window.FindControl<FitRowLines>("QueueColumnLines")!.Hot);
        app.Screenshot("queue-column-lines");
        app.Window.MouseMove(At(app, Part(header, "Title"), 10));
        Assert.Null(app.Window.FindControl<FitRowLines>("QueueColumnLines")!.Hot);
    }

    [AvaloniaFact]
    public void The_column_names_can_be_switched_off_and_GZJam_starts_as_it_was_left()
    {
        using var app = new AppHarness(settings: new AppSettings
        {
            MatchLoudness = false, QueueColumnOrder = ["Time", "Title"], QueueColumnWidths = { ["Time"] = 50 },
        });
        app.ViewModel.Queue.Add(app.Library.Results.Take(1).ToList());
        AppHarness.Settle(200);
        Assert.True(Left(app, Part(FirstRow(app), "Time")) < Left(app, Part(FirstRow(app), "Title")));
        Assert.Equal(47, Part(FirstRow(app), "Time").Bounds.Width, 1);   // 50 less its margin
        Assert.True(app.Window.FindControl<Border>("QueueHeaderBar")!.IsVisible);
        app.ViewModel.ShowsQueueColumnNames = false;
        AppHarness.Settle(100);
        Assert.False(app.Window.FindControl<Border>("QueueHeaderBar")!.IsVisible);
        Assert.False(app.Settings.ShowQueueColumnNames);
    }
}
