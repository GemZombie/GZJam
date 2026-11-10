using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using GZJam.Library;
using GZJam.Testing;

namespace GZJam.App.Tests;

/// <summary>
/// The song list keeps its rows when a search or a folder changes what it shows (#343). It used to
/// be handed a new list each time, so the grid threw every row away and built new ones: tens of
/// MB of garbage a second while browsing, and the garbage collections stopped the music.
/// </summary>
public sealed class SongListRowsAppTests
{
    private static AppHarness Long()
    {
        string music = Path.Combine(Path.GetTempPath(), "gzjam-rows-" + Guid.NewGuid().ToString("N"));
        for (int i = 1; i <= 150; i++)
            TestMp3.Write(Path.Combine(music, $"Song {i:000}.mp3"), new TestMp3.Tags(Title: $"Song {i:000}", Artist: i % 2 == 0 ? "Basie" : "Ellington"));
        return new AppHarness(musicFolder: music);
    }

    private static DataGrid Grid(AppHarness app) => app.Window.FindControl<DataGrid>("TrackGrid")!;

    private static List<DataGridRow> RowsInView(AppHarness app)
    {
        var grid = Grid(app);
        grid.UpdateLayout();
        return grid.GetVisualDescendants().OfType<DataGridRow>().Where(r => r.IsVisible && r.DataContext is Track).ToList();
    }

    private static string? TopInView(AppHarness app)
    {
        var grid = Grid(app);
        return RowsInView(app)
            .Select(r => (Track: (Track)r.DataContext!, Y: r.TranslatePoint(new Point(0, 0), grid)?.Y ?? double.MaxValue))
            .Where(r => r.Y >= -2)
            .OrderBy(r => r.Y)
            .FirstOrDefault().Track?.Title;
    }

    private static void Search(AppHarness app, string text)
    {
        app.Library.SearchText = text;
        AppHarness.WaitFor(() => !app.Library.IsSearching);
        AppHarness.Settle(300);
    }

    [AvaloniaFact]
    public void A_new_search_shows_its_songs_in_the_rows_already_made()
    {
        using var app = Long();
        AppHarness.WaitFor(() => app.Library.Results.Count == 150);
        var grid = Grid(app);
        var source = grid.ItemsSource;
        var before = RowsInView(app).ToHashSet();
        Assert.NotEmpty(before);

        Search(app, "basie");
        Assert.Equal(75, app.Library.Results.Count);

        var after = RowsInView(app);
        Assert.NotEmpty(after);
        Assert.Same(source, grid.ItemsSource);
        Assert.All(after, row => Assert.Contains(row, before));
        Assert.All(after, row => Assert.Equal("Basie", ((Track)row.DataContext!).Artist));
    }

    [AvaloniaFact]
    public void The_list_stays_sorted_by_the_column_chosen_through_a_new_search()
    {
        using var app = Long();
        AppHarness.WaitFor(() => app.Library.Results.Count == 150);
        Grid(app).Columns.Single(c => (string?)c.Header == "Title").Sort(ListSortDirection.Descending);
        AppHarness.Settle(200);
        Assert.Equal("Song 150", TopInView(app));

        Search(app, "ellington");

        Assert.Equal("Song 149", TopInView(app));
    }
}
