using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace GZJam.App.Tests;

/// <summary>The song list remembers the widths its columns are dragged to (#390), as the queue does (#188).</summary>
public sealed class SongColumnWidthTests
{
    private static DataGrid Grid(AppHarness app) => app.Window.FindControl<DataGrid>("TrackGrid")!;

    private static DataGridColumn Column(AppHarness app, string header) =>
        Grid(app).Columns.Single(c => c.Header as string == header);

    /// <summary>Drags the line after a column's name, as a DJ would.</summary>
    private static void DragEdge(AppHarness app, string header, double by)
    {
        var name = Grid(app).GetVisualDescendants().OfType<DataGridColumnHeader>().First(h => (string?)h.Content == header);
        var from = name.TranslatePoint(new Point(name.Bounds.Width - 1, name.Bounds.Height / 2), app.Window)!.Value;
        app.Window.MouseMove(from);
        app.Window.MouseDown(from, MouseButton.Left);
        app.Window.MouseMove(from + new Point(by, 0), RawInputModifiers.LeftMouseButton);
        app.Window.MouseUp(from + new Point(by, 0), MouseButton.Left);
        AppHarness.Settle(150);
    }

    private static MenuItem WidthsItem(AppHarness app) =>
        app.Window.ColumnsMenu().Items.OfType<MenuItem>().Single(i => (string?)i.Header == "Widths as they came");

    [AvaloniaFact]
    public void Dragged_widths_come_back_after_a_restart_and_go_back_to_the_defaults_from_the_menu()
    {
        DataGridLength energy, title, artist;
        using var app = new AppHarness();
        {
            Assert.False(WidthsItem(app).IsEnabled);   // nothing dragged yet
            DragEdge(app, "Energy", 30);
            DragEdge(app, "Title", 60);
            energy = Column(app, "Energy").Width;
            title = Column(app, "Title").Width;
            artist = Column(app, "Artist").Width;
            Assert.Equal(94, energy.Value, 0.5);
            Assert.True(title.IsStar);
            Assert.True(title.Value > 2);   // a bigger share of the room than it started with

            Assert.Equal("94", app.Settings.SongColumnWidths["Energy"]);
            Assert.EndsWith("*", app.Settings.SongColumnWidths["Title"]);
            Assert.DoesNotContain("Year", app.Settings.SongColumnWidths.Keys);   // as it came: not kept
            // A click on a name sorts, and changes no width.
            int kept = app.Settings.SongColumnWidths.Count;
            var year = Grid(app).GetVisualDescendants().OfType<DataGridColumnHeader>().First(h => (string?)h.Content == "Year");
            var middle = year.TranslatePoint(new Point(year.Bounds.Width / 2, year.Bounds.Height / 2), app.Window)!.Value;
            app.Window.MouseDown(middle, MouseButton.Left);
            app.Window.MouseUp(middle, MouseButton.Left);
            AppHarness.Settle(150);
            Assert.Equal(kept, app.Settings.SongColumnWidths.Count);
        }

        // GZJam started again with these settings (the first one's music still there).
        using var again = new AppHarness(app.MusicPath, app.Settings);
        Assert.Equal(energy.Value, Column(again, "Energy").Width.Value, 0.5);
        Assert.True(Column(again, "Title").Width.IsStar);
        Assert.Equal(title.Value, Column(again, "Title").Width.Value, 0.01);
        Assert.Equal(artist.Value, Column(again, "Artist").Width.Value, 0.01);

        // Hidden and shown again, a column keeps its width.
        again.Window.SetColumnShown("Energy", false);
        again.Window.SetColumnShown("Energy", true);
        AppHarness.Settle(100);
        Assert.Equal(energy.Value, Column(again, "Energy").ActualWidth, 0.5);

        var widths = WidthsItem(again);
        Assert.True(widths.IsEnabled);
        widths.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        AppHarness.Settle(150);
        Assert.Empty(again.Settings.SongColumnWidths);
        Assert.Equal(64, Column(again, "Energy").Width.Value, 0.5);
        Assert.True(Column(again, "Title").Width.IsStar);
        Assert.Equal(2, Column(again, "Title").Width.Value, 0.001);
        Assert.False(WidthsItem(again).IsEnabled);
    }

    [AvaloniaFact]
    public void A_tag_columns_width_is_kept_by_its_tag_through_hiding_and_a_restart()
    {
        using var app = TagColumnsTests.Tagged();
        {
            app.Window.SetTagColumnShown("dance", true);
            AppHarness.Settle(150);
            DragEdge(app, "Dance", 40);
            Assert.Equal("160", app.Settings.SongColumnWidths["tag:dance"]);

            app.Window.SetTagColumnShown("dance", false);
            app.Window.SetTagColumnShown("dance", true);
            AppHarness.Settle(150);
            Assert.Equal(160, Column(app, "Dance").Width.Value, 0.5);
        }

        using var again = new AppHarness(app.MusicPath, app.Settings);
        Assert.Equal(160, Column(again, "Dance").Width.Value, 0.5);
    }

    [Fact]
    public void The_widths_are_saved_with_the_settings()
    {
        string folder = Directory.CreateTempSubdirectory("gzjam-widths-").FullName;
        try
        {
            string path = Path.Combine(folder, "settings.json");
            new AppSettings { SongColumnWidths = { ["Energy"] = "94", ["Title"] = "2.657*", ["tag:dance"] = "160" } }.Save(path);
            var loaded = AppSettings.Load(path).SongColumnWidths;
            Assert.Equal("94", loaded["Energy"]);
            Assert.Equal("2.657*", loaded["Title"]);
            Assert.Equal("160", loaded["tag:dance"]);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
