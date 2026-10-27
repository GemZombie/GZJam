using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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
        Assert.Equal(["♥ Rating", "Album art", "Album", "Genre", "Year", "BPM", "Energy", "Played"], items.Select(i => (string)i.Header!));
        Assert.False(items.Single(i => (string)i.Header! == "Genre").IsChecked);

        items.Single(i => (string)i.Header! == "Year").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        menu.Close();

        Assert.False(Column(app, "Year").IsVisible);
        Assert.Contains("Year", app.Settings.HiddenColumns);

        app.Window.SetColumnShown("Genre", true);
        Assert.True(Column(app, "Genre").IsVisible);
        Assert.DoesNotContain("Genre", app.Settings.HiddenColumns);
    }

    [AvaloniaFact]
    public void Every_column_with_a_written_header_resizes_by_dragging_the_line_after_it()
    {
        using var app = new AppHarness();
        var grid = app.Window.FindControl<DataGrid>("TrackGrid")!;
        // Only the icon columns (the play key, ♥, Art) keep their width.
        Assert.Equal(["Title", "Artist", "Album", "Genre", "Year", "BPM", "Energy", "Played", "Time"],
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
}
