using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using GZJam.App.ViewModels;
using GZJam.Library.Servers;

namespace GZJam.App.Tests;

/// <summary>
/// A big server in the folder tree (#290): thousands of folders in one, as a server of a whole
/// library has its artists. Opening it must not make a row for every one (Danny's Navidrome: the
/// window stopped answering for seconds), and Show in folder tree still finds one far down. And
/// with a folder in it opened, the rows go on with no gap, and scrolling never leaves the tree
/// blank (1.4.0-beta.4: the tree's panels, one inside another, guessed the heights of rows they
/// hadn't made).
/// </summary>
public sealed class ServerTreeSizeAppTests
{
    private const int Artists = 2000;

    private static FolderNodeViewModel BigServer(AppHarness app)
    {
        var server = app.Database.AddServer("navidrome", "http://nas:4533", "danny");
        var songs = new List<ServerSong>();
        for (int a = 0; a < Artists; a++)
            for (int t = 1; t <= 3; t++)
                songs.Add(new ServerSong($"s{a}-{t}", $"Song {t}", $"Artist {a:0000}", "Album", $"Artist {a:0000}", t, 1, 2000,
                    $"/music/Artist {a:0000}/Album/{t:00} - Song {t}.mp3", "mp3", 4_000_000, TimeSpan.FromMinutes(3)));
        app.Database.ReplaceServerCatalog(server.Id, songs, DateTime.UtcNow);
        app.ViewModel.Servers.Refresh();
        AppHarness.Settle(200);
        return app.Library.Roots.Single(r => r.IsServerItself);
    }

    private static ListBox Tree(AppHarness app) => app.Window.FindControl<ListBox>("FolderTree")!;

    /// <summary>The rows made, top to bottom, where they are in the tree's view.</summary>
    private static List<(FolderNodeViewModel Folder, double Top, double Bottom)> Shown(AppHarness app)
    {
        var tree = Tree(app);
        return tree.GetVisualDescendants().OfType<ListBoxItem>().Where(r => r.IsVisible && r.DataContext is FolderNodeViewModel)
            .Select(r => ((FolderNodeViewModel)r.DataContext!, r.TranslatePoint(new Point(0, 0), tree)!.Value.Y,
                r.TranslatePoint(new Point(0, r.Bounds.Height), tree)!.Value.Y))
            .Where(r => r.Item3 > 0 && r.Item2 < tree.Bounds.Height)
            .OrderBy(r => r.Item2).ToList();
    }

    [AvaloniaFact]
    public void Opening_a_server_of_thousands_of_folders_makes_rows_only_for_those_in_view()
    {
        using var app = new AppHarness();
        var server = BigServer(app);
        Assert.Equal(Artists, server.Children.Count);

        server.IsExpanded = true;
        AppHarness.Settle(100);
        app.Window.UpdateLayout();

        int rows = Tree(app).GetVisualDescendants().OfType<ListBoxItem>().Count();
        Assert.InRange(rows, 2, 300);   // the folders in view, not two thousand
    }

    [AvaloniaFact]
    public void A_folder_far_down_is_scrolled_to_and_shown()
    {
        using var app = new AppHarness();
        var server = BigServer(app);
        var far = server.Children.Single(c => c.Name == "Artist 1900").Children.Single();
        server.IsExpanded = true;
        far.Parent!.IsExpanded = true;
        app.Library.SelectedFolder = far;
        AppHarness.Settle(100);

        app.Window.BringFolderIntoView(far);
        AppHarness.Settle(100);

        var tree = Tree(app);
        var row = Assert.IsAssignableFrom<ListBoxItem>(tree.ContainerFromItem(far));
        Point? top = row.TranslatePoint(new Point(0, 0), tree);
        Assert.NotNull(top);
        Assert.InRange(top.Value.Y, -1, tree.Bounds.Height);   // in view
    }

    [AvaloniaFact]
    public void With_a_folder_opened_far_down_the_rows_go_on_with_no_gap_and_scrolling_never_leaves_the_tree_blank()
    {
        using var app = new AppHarness();
        var server = BigServer(app);
        server.IsExpanded = true;
        var artist = server.Children.Single(c => c.Name == "Artist 1200");
        app.Window.BringFolderIntoView(artist);
        AppHarness.Settle(100);
        artist.IsExpanded = true;   // its one album under it
        AppHarness.Settle(100);
        app.Window.BringFolderIntoView(server.Children.Single(c => c.Name == "Artist 1201"));   // the next one in view too
        AppHarness.Settle(100);
        app.Window.UpdateLayout();
        app.Screenshot("folder-rows-server-open");

        var rows = Shown(app);
        int at = rows.FindIndex(r => r.Folder == artist);
        Assert.True(at >= 0 && at + 2 < rows.Count, "the artist, its album and the next artist in view");
        Assert.Same(artist.Children[0], rows[at + 1].Folder);
        Assert.Equal("Artist 1201", rows[at + 2].Folder.Name);
        for (int i = 1; i < rows.Count; i++)
            Assert.Equal(rows[i - 1].Bottom, rows[i].Top, 1);   // no gap, no overlap

        // Scrolled a long way each way, from there: rows fill the view every time.
        var scroller = Tree(app).GetVisualDescendants().OfType<ScrollViewer>().First();
        foreach (double step in (double[])[2500, -4000, 9000, -600, 30000, -45000])
        {
            scroller.Offset = new Vector(0, Math.Clamp(scroller.Offset.Y + step, 0, scroller.Extent.Height - scroller.Viewport.Height));
            AppHarness.Settle(50);
            app.Window.UpdateLayout();
            var shown = Shown(app);
            Assert.True(shown.Count > 5, $"blank at {scroller.Offset.Y:0}");
            Assert.True(shown[0].Top <= 1 && shown[^1].Bottom >= Tree(app).Bounds.Height - 1 || scroller.Offset.Y + scroller.Viewport.Height >= scroller.Extent.Height - 1,
                $"rows from {shown[0].Top:0} to {shown[^1].Bottom:0} of {Tree(app).Bounds.Height:0} at {scroller.Offset.Y:0}");
        }
    }
}
