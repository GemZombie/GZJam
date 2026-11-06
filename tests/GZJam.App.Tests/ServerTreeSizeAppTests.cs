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
/// window stopped answering for seconds), and Show in folder tree still finds one far down.
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

    private static TreeView Tree(AppHarness app) => app.Window.FindControl<TreeView>("FolderTree")!;

    [AvaloniaFact]
    public void Opening_a_server_of_thousands_of_folders_makes_rows_only_for_those_in_view()
    {
        using var app = new AppHarness();
        var server = BigServer(app);
        Assert.Equal(Artists, server.Children.Count);

        server.IsExpanded = true;
        AppHarness.Settle(100);
        app.Window.UpdateLayout();

        int rows = Tree(app).GetVisualDescendants().OfType<TreeViewItem>().Count();
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
        var row = Assert.IsAssignableFrom<TreeViewItem>(tree.TreeContainerFromItem(far));
        Point? top = row.TranslatePoint(new Point(0, 0), tree);
        Assert.NotNull(top);
        Assert.InRange(top.Value.Y, -1, tree.Bounds.Height);   // in view
    }
}
