using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using System.ComponentModel;
using Avalonia.VisualTree;
using Track = GZJam.Library.Track;

namespace GZJam.App.Tests;

/// <summary>Choosing the library's columns from a header's right-click menu.</summary>
public sealed class ColumnsTests
{
    private static DataGridColumn Column(AppHarness app, string header) =>
        app.Window.FindControl<DataGrid>("TrackGrid")!.Columns.Single(c => (string?)c.Header == header);

    [AvaloniaFact]
    public void Genre_starts_hidden_and_the_played_column_shows()
    {
        using var app = new AppHarness();
        Assert.False(Column(app, "Genre").IsVisible);
        Assert.True(Column(app, "Played").IsVisible);
        Assert.True(Column(app, "Year").IsVisible);
    }

    [AvaloniaFact]
    public void Right_clicking_a_header_chooses_columns_and_it_is_remembered()
    {
        using var app = new AppHarness();
        var grid = app.Window.FindControl<DataGrid>("TrackGrid")!;
        var header = grid.GetVisualDescendants().OfType<DataGridColumnHeader>().First(h => (string?)h.Content == "Year");
        var at = header.TranslatePoint(new Point(10, header.Bounds.Height / 2), app.Window)!.Value;

        app.Window.MouseDown(at, MouseButton.Right);
        app.Window.MouseUp(at, MouseButton.Right);
        AppHarness.Settle(200);

        var menu = app.Window.ColumnsMenu();
        Assert.True(menu.IsOpen);
        Assert.False(app.Window.FindControl<ContextMenu>("TrackMenu")!.IsOpen);   // not the track menu
        var items = menu.Items.OfType<MenuItem>().Skip(1).ToList();
        // In the list's order, with the columns that always show listed too (#379)
        Assert.Equal(["♥ Rating", "Album art", "Title", "Artist", "Time", "Album", "Genre", "Grouping", "Comment", "Year", "BPM", "Energy", "Played", "Widths as they came"],
            items.Select(i => (string)i.Header!));
        Assert.False(items.Single(i => (string)i.Header! == "Genre").IsChecked);
        foreach (string always in new[] { "Title", "Artist", "Time" })
        {
            var item = items.Single(i => (string)i.Header! == always);
            Assert.True(item.IsChecked, always);
            Assert.False(item.IsEnabled, always);
            Assert.Equal("Always shown", ToolTip.GetTip(item));
        }

        items.Single(i => (string)i.Header! == "Year").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        menu.Close();

        Assert.False(Column(app, "Year").IsVisible);
        Assert.Contains("Year", app.Settings.HiddenColumns);

        app.Window.SetColumnShown("Genre", true);
        Assert.True(Column(app, "Genre").IsVisible);
        Assert.DoesNotContain("Genre", app.Settings.HiddenColumns);
    }

    [AvaloniaFact]
    public void Time_is_next_to_Artist_and_in_view_on_a_laptop_screen()
    {
        using var app = new AppHarness();
        app.Window.Width = 1366;
        app.Window.Height = 768;
        AppHarness.Settle(300);
        var grid = app.Window.FindControl<DataGrid>("TrackGrid")!;
        var shown = grid.Columns.Where(c => c.IsVisible).Select(c => c.Header as string).ToList();
        Assert.Equal(shown.IndexOf("Artist") + 1, shown.IndexOf("Time"));

        // With the columns a new DJ starts with, every one is inside the list, none past the right edge
        // or squeezed (Time was 20 px wide), so moving Time up didn't push Played off instead.
        foreach (var header in grid.GetVisualDescendants().OfType<DataGridColumnHeader>().Where(h => h.Content is string { Length: > 1 } && h.IsEffectivelyVisible))
        {
            string name = (string)header.Content!;
            var right = header.TranslatePoint(new Point(header.Bounds.Width, 0), grid)!.Value.X;
            Assert.True(header.Bounds.Width >= 56, $"{name} is {header.Bounds.Width} px wide");
            Assert.True(right <= grid.Bounds.Width, $"{name} ends at {right}, the list at {grid.Bounds.Width}");
        }
        app.Screenshot("library-time-column-1366");
    }

    [Fact]
    public void Settings_from_before_Grouping_and_Comment_start_with_them_hidden_once()
    {
        var old = AppSettings.Migrate(new AppSettings { HiddenColumns = ["Genre"], SeenColumns = ["Art"] });
        Assert.Equal(["Genre", "Grouping", "Comment"], old.HiddenColumns);

        old.HiddenColumns.Remove("Comment");   // the DJ shows it
        Assert.Equal(["Genre", "Grouping"], AppSettings.Migrate(old).HiddenColumns);   // and it stays shown
        Assert.True(new AppSettings().HiddenColumns.Contains("Grouping"), "hidden for new settings too");
    }

    [AvaloniaFact]
    public void Every_column_with_a_written_header_resizes_by_dragging_the_line_after_it()
    {
        using var app = new AppHarness();
        var grid = app.Window.FindControl<DataGrid>("TrackGrid")!;
        // Only the icon columns (the play key, ♥, Art) keep their width.
        Assert.Equal(["Title", "Artist", "Time", "Added", "Album", "Genre", "Grouping", "Comment", "Year", "BPM", "Energy", "Played"],
            grid.Columns.Where(c => c.CanUserResize).Select(c => (string)c.Header!));

        // The line between Energy and Played (#175)
        var energy = Column(app, "Energy");
        double before = energy.ActualWidth;
        var header = grid.GetVisualDescendants().OfType<DataGridColumnHeader>().First(h => (string?)h.Content == "Energy");
        var from = header.TranslatePoint(new Point(header.Bounds.Width - 1, header.Bounds.Height / 2), app.Window)!.Value;
        app.Window.MouseMove(from);
        app.Window.MouseDown(from, MouseButton.Left);
        app.Window.MouseMove(from + new Point(30, 0), RawInputModifiers.LeftMouseButton);
        app.Window.MouseUp(from + new Point(30, 0), MouseButton.Left);
        AppHarness.Settle(100);
        Assert.Equal(before + 30, energy.ActualWidth, 1);

        // Narrowed, it stops where its icon still fits.
        energy.Width = new DataGridLength(10);
        AppHarness.Settle(100);
        Assert.Equal(energy.MinWidth, energy.ActualWidth, 1);
    }

    [AvaloniaFact]
    public void A_narrowed_column_keeps_its_name_and_the_sort_arrow_takes_room_only_where_it_shows()
    {
        using var app = new AppHarness();
        var grid = app.Window.FindControl<DataGrid>("TrackGrid")!;
        Column(app, "Year").Width = new DataGridLength(48);
        Column(app, "BPM").Width = new DataGridLength(48);
        Column(app, "Artist").Width = new DataGridLength(100);
        Column(app, "BPM").Sort(ListSortDirection.Ascending);
        AppHarness.Settle(300);
        app.Screenshot("library-narrow-columns");

        TextBlock Name(string header) => grid.GetVisualDescendants().OfType<DataGridColumnHeader>()
            .First(h => (string?)h.Content == header).GetVisualDescendants().OfType<TextBlock>().Single();

        // Unsorted: the whole name fits, as it did not before (#186: 0 px left at 48 px wide).
        foreach (string header in new[] { "Year", "Artist" })
        {
            var name = Name(header);
            Assert.Equal(TextTrimming.CharacterEllipsis, name.TextTrimming);
            Assert.True(name.Bounds.Width >= Natural(name), $"{header}: {name.Bounds.Width} px for {Natural(name)}");
        }
        // Sorted: the arrow shows, and the name still has most of its room.
        var bpm = Name("BPM");
        var arrow = grid.GetVisualDescendants().OfType<DataGridColumnHeader>().First(h => (string?)h.Content == "BPM")
            .GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(p => p.Name == "SortIcon");
        Assert.True(arrow.IsEffectivelyVisible);
        Assert.True(bpm.Bounds.Width >= 12, $"BPM sorted: {bpm.Bounds.Width} px");

        // The cells get the column's room less its padding (the grid's own text margin took 24 px
        // more), and end in "…" rather than cutting a letter in half.
        var artist = grid.GetVisualDescendants().OfType<DataGridCell>().SelectMany(c => c.GetVisualDescendants().OfType<TextBlock>())
            .First(t => t.Text == app.Library.Results[0].Artist);
        Assert.Equal(TextTrimming.CharacterEllipsis, artist.TextTrimming);
        Assert.True(artist.Bounds.Width >= 100 - 2 * 8 - 1, $"Artist cell: {artist.Bounds.Width} px");
    }

    [AvaloniaFact]
    public void Times_line_up_on_the_right_after_the_list_shows_longer_ones()
    {
        using var app = new AppHarness();
        var grid = app.Window.FindControl<DataGrid>("TrackGrid")!;
        Column(app, "Time").Width = new DataGridLength(214);
        static Track Song(int seconds)
        {
            var song = new Track { Path = $"C:/nowhere/{seconds}.mp3", Folder = "", FileName = $"{seconds}.mp3", Title = $"Song {seconds}", Artist = "Band" };
            song.Duration = TimeSpan.FromSeconds(seconds);
            return song;
        }

        // Rows that showed an hour-long and a ten-minute recording, reused for shorter songs (#459)
        var rows = app.ViewModel.Library.ResultRows;
        rows.Replace(Enumerable.Range(0, 12).Select(i => Song(i % 2 == 0 ? 3723 : 605)).ToList());
        AppHarness.Settle(200);
        int[] lengths = [322, 270, 349, 378, 430, 366, 365, 477, 214];
        rows.Replace(lengths.Select(Song).ToList());
        AppHarness.Settle(200);

        var times = grid.GetVisualDescendants().OfType<DataGridCell>()
            .Where(c => c.IsEffectivelyVisible && c.Classes.Contains("num"))
            .SelectMany(c => c.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Text is { Length: 4 } && t.Text[1] == ':').Select(t => (Cell: c, Text: t)))
            .ToList();
        Assert.Equal(lengths.Length, times.Count);
        foreach (var (cell, text) in times)
        {
            // The text's box spans the cell, less its padding, and the text ends at the box's right edge
            Assert.Equal(cell.Bounds.Width - cell.Padding.Left - cell.Padding.Right, text.Bounds.Width, 1);
            var line = text.TextLayout.TextLines[0];
            Assert.Equal(text.Bounds.Width, line.Start + line.Width, 1);
        }
    }

    private static double Natural(TextBlock text)
    {
        var copy = new TextBlock { Text = text.Text, FontSize = text.FontSize, FontWeight = text.FontWeight, FontFamily = text.FontFamily };
        copy.Measure(Size.Infinity);
        return Math.Floor(copy.DesiredSize.Width);
    }
}
