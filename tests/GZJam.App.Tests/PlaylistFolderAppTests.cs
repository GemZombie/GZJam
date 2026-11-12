using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>
/// Folders in the Playlists panel (#354): made and nested, folded and remembered, playlists and
/// folders moved by Move to and by dragging, a folder deleted after asking, new playlists going in
/// the folder chosen, Add to playlist reaching into folders, and a folder of M3U files imported
/// with its folders.
/// </summary>
public sealed class PlaylistFolderAppTests
{
    private static ListBox List(AppHarness app)
    {
        PlaylistAppTests.ShowPlaylists(app);   // the Playlists source's column (#423)
        return app.Window.FindControl<ListBox>("PlaylistList")!;
    }

    /// <summary>The panel's rows as text: "▸ Folder (n)" or "▾ Folder (n)" for a folder, its name for a playlist, indented two spaces a level.</summary>
    private static List<string> Rows(AppHarness app) => app.Library.PlaylistRows.Select(r => r switch
    {
        PlaylistFolderRow f => new string(' ', f.Folder.Depth * 2) + (f.IsExpanded ? "▾ " : "▸ ") + $"{f.Name} ({f.Count})",
        PlaylistInfo p => new string(' ', p.Depth * 2) + p.Name,
        _ => "?",
    }).ToList();

    private static PlaylistFolderRow FolderRow(AppHarness app, string name) =>
        app.Library.PlaylistRows.OfType<PlaylistFolderRow>().Single(f => f.Name == name);

    private static ListBoxItem Container(AppHarness app, object row) => (ListBoxItem)List(app).ContainerFromItem(row)!;

    private static Point PointOf(AppHarness app, object row)
    {
        ListBoxItem item = Container(app, row);
        return item.TranslatePoint(new Point(60, item.Bounds.Height / 2), app.Window)!.Value;
    }

    /// <summary>Where a playlist's name starts, across the panel.</summary>
    private static double NameX(AppHarness app, string name) =>
        List(app).GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == name).TranslatePoint(default, List(app))!.Value.X;

    private static PlaylistFolder Folder(AppHarness app, string name, long? parent = null)
    {
        app.ViewModel.PromptAsync = (_, _, _, _) => Task.FromResult<string?>(name);
        var task = app.ViewModel.CreatePlaylistFolderAsync(parent);
        AppHarness.Await(task);
        var folder = task.Result!;
        AppHarness.Settle(100);
        return folder;
    }

    private static PlaylistInfo Playlist(AppHarness app, string name, long? folder = null)
    {
        var playlist = app.ViewModel.CreatePlaylist(name, null, folder)!;
        AppHarness.Settle(100);
        return playlist;
    }

    /// <summary>A row's right-click menu, filled as it opens; then its Move to › <paramref name="to"/> chosen.</summary>
    private static void MoveTo(AppHarness app, object row, string to)
    {
        Control target = Container(app, row).GetVisualDescendants().OfType<Control>().First(c => c.ContextMenu is not null);
        target.RaiseEvent(new ContextRequestedEventArgs { RoutedEvent = Control.ContextRequestedEvent, Source = target });
        AppHarness.Settle(50);
        MenuItem move = target.ContextMenu!.Items.OfType<MenuItem>().Single(i => i.Tag is "move");
        MenuItem choice = move.Items.OfType<MenuItem>().Single(i => (string?)i.Header == to);
        Assert.True(choice.IsEnabled);
        target.ContextMenu.Close();
        choice.Command!.Execute(null);
        AppHarness.Settle(100);
    }

    [AvaloniaFact]
    public void Folders_come_first_nest_count_their_playlists_and_stay_folded_as_left()
    {
        var settings = new AppSettings { MatchLoudness = false, OnAirPause = OnAirPause.Tap };
        string work = Directory.CreateTempSubdirectory("gzjam-app-test-").FullName;   // kept between the two runs
        using (var app = new AppHarness(settings: settings, workDirectory: work))
        {
            var setlists = Folder(app, "Setlists");
            var y2026 = Folder(app, "2026", setlists.Id);
            Playlist(app, "Nov 8", y2026.Id);
            Playlist(app, "Warm-ups", setlists.Id);
            Playlist(app, "Anything goes");

            Assert.Equal(["▾ Setlists (2)", "  ▾ 2026 (1)", "    Nov 8", "  Warm-ups", "Anything goes"], Rows(app));
            // Each row indented for its place, as the folder tree is.
            Assert.True(NameX(app, "Nov 8") > NameX(app, "Warm-ups"));
            Assert.True(NameX(app, "Warm-ups") > NameX(app, "Anything goes"));
            app.Screenshot("playlist-folders");

            FolderRow(app, "2026").IsExpanded = false;
            AppHarness.Settle(100);
            Assert.Equal(["▾ Setlists (2)", "  ▸ 2026 (1)", "  Warm-ups", "Anything goes"], Rows(app));
        }

        try
        {
            using var again = new AppHarness(settings: settings, workDirectory: work);
            Assert.Equal(["▾ Setlists (2)", "  ▸ 2026 (1)", "  Warm-ups", "Anything goes"], Rows(again));
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (IOException) { }
        }
    }

    [AvaloniaFact]
    public void Move_to_puts_a_playlist_or_a_folder_elsewhere_by_touch_and_never_a_folder_into_itself()
    {
        using var app = new AppHarness();
        var setlists = Folder(app, "Setlists");
        Folder(app, "Practice");
        Folder(app, "2026", setlists.Id);
        var friday = Playlist(app, "Friday");

        MoveTo(app, friday, "Setlists › 2026");
        Assert.Equal(["▾ Practice (0)", "▾ Setlists (1)", "  ▾ 2026 (1)", "    Friday"], Rows(app));

        // Its own folders aren't offered: a folder can't go inside itself.
        Control menuOwner = Container(app, FolderRow(app, "Setlists")).GetVisualDescendants().OfType<Control>().First(c => c.ContextMenu is not null);
        menuOwner.RaiseEvent(new ContextRequestedEventArgs { RoutedEvent = Control.ContextRequestedEvent, Source = menuOwner });
        AppHarness.Settle(50);
        var offered = menuOwner.ContextMenu!.Items.OfType<MenuItem>().Single(i => i.Tag is "move").Items.OfType<MenuItem>().Select(i => (string?)i.Header);
        Assert.Equal(["The top", "Practice"], offered);
        menuOwner.ContextMenu.Close();

        MoveTo(app, FolderRow(app, "Setlists"), "Practice");
        Assert.Equal(["▾ Practice (1)", "  ▾ Setlists (1)", "    ▾ 2026 (1)", "      Friday"], Rows(app));

        MoveTo(app, app.Library.Playlists.Single(), "The top");
        Assert.Equal(["▾ Practice (0)", "  ▾ Setlists (0)", "    ▾ 2026 (0)", "Friday"], Rows(app));   // still open, as left
    }

    [AvaloniaFact]
    public void A_playlist_dragged_onto_a_folder_goes_in_it_and_onto_the_empty_space_to_the_top()
    {
        using var app = new AppHarness();
        Folder(app, "Setlists");
        var friday = Playlist(app, "Friday");

        app.MouseDrag(PointOf(app, friday), PointOf(app, FolderRow(app, "Setlists")), over: () =>
            Assert.Contains("drop-target", Container(app, FolderRow(app, "Setlists")).Classes));
        AppHarness.Settle(100);
        Assert.Equal(["▾ Setlists (1)", "  Friday"], Rows(app));

        // The panel off the rows (its header): back to the top.
        var header = app.Window.FindControl<DockPanel>("PlaylistsSection")!.Children.OfType<Border>().First();
        Point off = header.TranslatePoint(new Point(30, header.Bounds.Height / 2), app.Window)!.Value;
        app.MouseDrag(PointOf(app, app.Library.Playlists.Single()), off);
        AppHarness.Settle(100);
        Assert.Equal(["▾ Setlists (0)", "Friday"], Rows(app));
    }

    [AvaloniaFact]
    public void Deleting_a_folder_asks_says_how_many_go_and_takes_them_with_it()
    {
        using var app = new AppHarness();
        var song = app.Library.Results[0];
        var setlists = Folder(app, "Setlists");
        var y2026 = Folder(app, "2026", setlists.Id);
        var nov8 = app.ViewModel.CreatePlaylist("Nov 8", [song], y2026.Id)!;
        Playlist(app, "Warm-ups", setlists.Id);
        Playlist(app, "Keep me");
        app.Library.OpenPlaylist(nov8.Id);
        AppHarness.Settle(100);
        string? asked = null;
        app.ViewModel.ConfirmAsync = (heading, message, _) =>
        {
            asked = heading + " " + message;
            return Task.FromResult(false);
        };

        AppHarness.Await(app.ViewModel.DeletePlaylistFolderAsync(FolderRow(app, "Setlists")));
        Assert.Contains("The 2 playlists in it go with it", asked);
        Assert.Equal(3, app.Library.Playlists.Count);   // "no": nothing went

        app.ViewModel.ConfirmAsync = (_, _, _) => Task.FromResult(true);
        AppHarness.Await(app.ViewModel.DeletePlaylistFolderAsync(FolderRow(app, "Setlists")));
        AppHarness.Settle(100);
        Assert.Equal(["Keep me"], Rows(app));
        Assert.Null(app.Library.SelectedPlaylist);   // the open one was in it
        Assert.Contains(app.Library.Results, t => t.Path == song.Path);   // its song is still in the library
    }

    [AvaloniaFact]
    public void A_new_playlist_goes_in_the_folder_chosen_and_add_to_playlist_reaches_into_folders()
    {
        using var app = new AppHarness();
        var song = app.Library.Results[0];
        var setlists = Folder(app, "Setlists");
        Folder(app, "Empty");
        Playlist(app, "Nov 8", setlists.Id);
        Playlist(app, "Top");

        // A click on the folder chooses it: + New › Playlist… makes one in it.
        Container(app, FolderRow(app, "Setlists")).GetVisualDescendants().OfType<DockPanel>().First()
            .RaiseEvent(new TappedEventArgs(InputElement.TappedEvent, null!));
        AppHarness.Settle(50);
        app.ViewModel.PromptAsync = (_, message, _, _) =>
        {
            Assert.Contains("In Setlists.", message);
            return Task.FromResult<string?>("Nov 15");
        };
        AppHarness.Await(app.ViewModel.CreatePlaylistAsync());
        AppHarness.Settle(100);
        Assert.Equal(setlists.Id, app.Library.Playlists.Single(p => p.Name == "Nov 15").FolderId);

        // Add to playlist: the folder is a submenu, an empty one isn't shown.
        var menu = new MenuItem();
        GZJam.App.Views.PlaylistsMenu.Fill(menu, app.ViewModel, [song]);
        var top = menu.Items.OfType<MenuItem>().Select(i => (string?)i.Header).ToList();
        Assert.Equal(["Setlists", "Top", "New playlist…"], top);
        var inside = menu.Items.OfType<MenuItem>().First().Items.OfType<MenuItem>().Select(i => (string?)i.Header);
        Assert.Equal(["Nov 15", "Nov 8"], inside);
    }

    [AvaloniaFact]
    public void A_folder_of_playlist_files_comes_in_with_its_folders_and_again_adds_nothing()
    {
        using var app = new AppHarness();
        var songs = app.Library.Results.Take(3).ToList();
        string picked = Directory.CreateTempSubdirectory("gzjam-m3u-").FullName;
        Directory.CreateDirectory(Path.Combine(picked, "WCS", "Setlists"));
        File.WriteAllLines(Path.Combine(picked, "Top.m3u"), [songs[0].Path]);
        File.WriteAllLines(Path.Combine(picked, "WCS", "Setlists", "Nov 8.m3u8"), [songs[1].Path, songs[2].Path]);
        File.WriteAllText(Path.Combine(picked, "WCS", "readme.txt"), "not a playlist");
        app.ViewModel.ConfirmImportAsync = null;

        AppHarness.Await(app.ViewModel.ImportPlaylistFolderAsync(picked));
        AppHarness.Settle(150);
        // Closed, so a big import doesn't fill the panel: open, it holds what the folder did.
        Assert.Equal(["▸ WCS (1)", "Top"], Rows(app));
        FolderRow(app, "WCS").IsExpanded = true;
        FolderRow(app, "Setlists").IsExpanded = true;
        AppHarness.Settle(100);
        Assert.Equal(["▾ WCS (1)", "  ▾ Setlists (1)", "    Nov 8", "Top"], Rows(app));
        var nov8 = app.Library.Playlists.Single(p => p.Name == "Nov 8");
        Assert.Equal(2, nov8.Count);

        AppHarness.Await(app.ViewModel.ImportPlaylistFolderAsync(picked));
        AppHarness.Settle(150);
        Assert.Equal(2, app.Library.Playlists.Count);
        Assert.Single(app.Library.PlaylistFolders, f => f.Name == "WCS");
        Directory.Delete(picked, recursive: true);
    }
}
