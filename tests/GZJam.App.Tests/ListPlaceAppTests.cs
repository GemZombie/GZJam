using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using GZJam.App.ViewModels;
using GZJam.Library;
using GZJam.Testing;

namespace GZJam.App.Tests;

/// <summary>
/// The song list keeps its place when GZJam reads the same list again by itself
/// (a scan, each song a sync brings down): before, it went back to the top every few seconds
/// during a sync, so a song far down a long list couldn't be reached.
/// </summary>
public sealed class ListPlaceAppTests
{
    private static AppHarness Long()
    {
        string music = Path.Combine(Path.GetTempPath(), "gzjam-long-" + Guid.NewGuid().ToString("N"));
        for (int i = 1; i <= 150; i++)
            TestMp3.Write(Path.Combine(music, $"Song {i:000}.mp3"), new TestMp3.Tags(Title: $"Song {i:000}", Artist: "Basie"));
        return new AppHarness(musicFolder: music);
    }

    private static DataGrid Grid(AppHarness app) => app.Window.FindControl<DataGrid>("TrackGrid")!;

    /// <summary>The title of the topmost song in view.</summary>
    private static string? TopInView(AppHarness app)
    {
        var grid = Grid(app);
        grid.UpdateLayout();
        return grid.GetVisualDescendants().OfType<DataGridRow>()
            .Where(r => r.IsVisible && r.DataContext is Track)
            .Select(r => (Track: (Track)r.DataContext!, Y: r.TranslatePoint(new Point(0, 0), grid)?.Y ?? double.MaxValue))
            .Where(r => r.Y >= -2)
            .OrderBy(r => r.Y)
            .FirstOrDefault().Track?.Title;
    }

    [AvaloniaFact]
    public void Read_again_by_itself_the_list_stays_where_it_was_with_the_songs_selected()
    {
        using var app = Long();
        var grid = Grid(app);
        AppHarness.WaitFor(() => app.Library.Results.Count == 150);
        grid.ScrollIntoView(app.Library.Results[119], null);
        grid.ScrollIntoView(app.Library.Results[100], null);
        grid.SelectedItem = app.Library.Results[110];
        AppHarness.Settle(100);
        string? before = TopInView(app);
        Assert.NotEqual("Song 001", before);

        app.Library.RefreshResults();   // as after a scan
        AppHarness.Settle(400);

        Assert.Equal(before, TopInView(app));
        Assert.Equal("Song 111", Assert.IsType<Track>(grid.SelectedItem).Title);
    }

    [AvaloniaFact]
    public void A_new_search_starts_at_the_top()
    {
        using var app = Long();
        var grid = Grid(app);
        AppHarness.WaitFor(() => app.Library.Results.Count == 150);
        grid.ScrollIntoView(app.Library.Results[140], null);
        AppHarness.Settle(100);

        app.Library.SearchText = "basie";
        AppHarness.WaitFor(() => !app.Library.IsSearching);
        AppHarness.Settle(400);

        Assert.Equal("Song 001", TopInView(app));
    }
}
