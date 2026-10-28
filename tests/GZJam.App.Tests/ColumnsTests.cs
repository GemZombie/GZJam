using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using System.ComponentModel;
using Avalonia.VisualTree;

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
        Assert.Equal(["♥ Rating", "Album art", "Album", "Genre", "Grouping", "Comment", "Year", "BPM", "Energy", "Played"], items.Select(i => (string)i.Header!));
        Assert.False(items.Single(i => (string)i.Header! == "Genre").IsChecked);

        items.Single(i => (string)i.Header! == "Year").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        menu.Close();

        Assert.False(Column(app, "Year").IsVisible);
        Assert.Contains("Year", app.Settings.HiddenColumns);

        app.Window.SetColumnShown("Genre", true);
        Assert.True(Column(app, "Genre").IsVisible);
        Assert.DoesNotContain("Genre", app.Settings.HiddenColumns);
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
        Assert.Equal(["Title", "Artist", "Album", "Genre", "Grouping", "Comment", "Year", "BPM", "Energy", "Played", "Time"],
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

    private static double Natural(TextBlock text)
    {
        var copy = new TextBlock { Text = text.Text, FontSize = text.FontSize, FontWeight = text.FontWeight, FontFamily = text.FontFamily };
        copy.Measure(Size.Infinity);
        return Math.Floor(copy.DesiredSize.Width);
    }
}
