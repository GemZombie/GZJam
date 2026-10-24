using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>Playlists in the app: made, filled, opened in the song list, reordered, played.</summary>
public sealed class PlaylistAppTests
{
    private static DataGrid Grid(AppHarness app) => app.Window.FindControl<DataGrid>("TrackGrid")!;

    private static Point RowPoint(AppHarness app, Track track)
    {
        var row = Grid(app).GetVisualDescendants().OfType<DataGridRow>().First(r => r.DataContext == track);
        return row.TranslatePoint(new Point(200, row.Bounds.Height / 2), app.Window)!.Value;
    }

    private static Point PlaylistPoint(AppHarness app, string name)
    {
        var list = app.Window.FindControl<ListBox>("PlaylistList")!;
        var item = list.GetVisualDescendants().OfType<ListBoxItem>().First(i => ((PlaylistInfo)i.DataContext!).Name == name);
        return item.TranslatePoint(new Point(40, item.Bounds.Height / 2), app.Window)!.Value;
    }

    private static PlaylistInfo Make(AppHarness app, string name, params Track[] songs)
    {
        var playlist = app.ViewModel.CreatePlaylist(name, songs)!;
        AppHarness.Settle(150);
        return playlist;
    }

    private static List<string> Shown(AppHarness app) => app.Library.Results.Select(t => t.DisplayTitle).ToList();

    [AvaloniaFact]
    public void A_new_playlist_is_named_listed_and_opened_empty()
    {
        using var app = new AppHarness();
        app.ViewModel.PromptAsync = (_, _, _, _) => Task.FromResult<string?>("Saturday Hot Jam");

        app.Window.FindControl<Button>("NewPlaylistButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        AppHarness.Settle(300);

        Assert.Equal(["Saturday Hot Jam"], app.Library.Playlists.Select(p => p.Name));
        Assert.True(app.Library.IsPlaylistOpen);
        Assert.True(app.Window.FindControl<Border>("PlaylistBar")!.IsVisible);
        Assert.Empty(app.Library.Results);
        Assert.Contains("Drag songs onto its name", app.Library.EmptyMessage);
    }

    [AvaloniaFact]
    public void Songs_dragged_onto_a_playlists_name_are_added_and_it_shows_them_in_order_numbered()
    {
        using var app = new AppHarness();
        var songs = app.Library.Results.Take(4).ToList();
        Make(app, "Saturday");
        Make(app, "Warm-up", songs[3]);

        // Drag a song from the library onto "Saturday", then another.
        app.MouseDrag(RowPoint(app, songs[2]), PlaylistPoint(app, "Saturday"), over: () =>
            Assert.Contains(app.Window.FindControl<ListBox>("PlaylistList")!.GetVisualDescendants().OfType<ListBoxItem>(),
                i => i.Classes.Contains("drop-target")));
        app.MouseDrag(RowPoint(app, songs[0]), PlaylistPoint(app, "Saturday"));
        Assert.Equal(2, app.Library.Playlists.Single(p => p.Name == "Saturday").Count);

        app.Library.OpenPlaylist(app.Library.Playlists.Single(p => p.Name == "Saturday").Id);
        AppHarness.Settle();
        Assert.Equal([songs[2].DisplayTitle, songs[0].DisplayTitle], Shown(app));
        Assert.Equal([1, 2], app.Library.Results.Select(t => t.PlaylistNumber!.Value));
        Assert.Null(app.Library.SelectedFolder);   // a playlist or a folder, not both
        Assert.Equal("in playlist Saturday", app.Library.ScopeText);
        // Each song's number in the playlist shows before its title.
        var numbers = Grid(app).GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("playlist-number") && t.IsEffectivelyVisible);
        Assert.Equal(["1", "2"], numbers.OrderBy(t => t.TranslatePoint(default, app.Window)!.Value.Y).Select(t => t.Text));
        Assert.All(numbers, t => Assert.True(t.Bounds.Width > 0));
        app.Screenshot("playlist-open");

        // Picking a folder goes back to the library, unnumbered.
        app.Library.SelectedFolder = app.Library.Roots[0];
        AppHarness.Settle();
        Assert.False(app.Library.IsPlaylistOpen);
        Assert.DoesNotContain(Grid(app).GetVisualDescendants().OfType<TextBlock>(), t => t.Classes.Contains("playlist-number") && t.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void The_songs_menu_adds_to_a_playlist_or_a_new_one()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var songs = app.Library.Results.Take(3).ToList();
        Make(app, "Saturday");
        var grid = Grid(app);
        grid.SelectedItems.Clear();
        grid.SelectedItems.Add(songs[0]);
        grid.SelectedItems.Add(songs[1]);
        var menu = app.Window.FindControl<ContextMenu>("TrackMenu")!;
        menu.Open(grid);
        AppHarness.Settle(150);

        var add = app.Window.FindControl<MenuItem>("TrackPlaylistsMenu")!;
        Assert.Equal(["Saturday", "New playlist…"], add.Items.OfType<MenuItem>().Select(i => (string)i.Header!));
        Assert.False(app.Window.FindControl<MenuItem>("RemoveFromPlaylistMenuItem")!.IsVisible);   // not in the library
        add.Items.OfType<MenuItem>().First().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        menu.Close();
        Assert.Equal(2, app.Library.Playlists.Single().Count);

        vm.PromptAsync = (_, message, _, _) => Task.FromResult<string?>(message == "With 2 songs" ? "Slow ones" : null);
        menu.Open(grid);
        AppHarness.Settle(150);
        add.Items.OfType<MenuItem>().Last().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        menu.Close();
        AppHarness.Settle(200);
        Assert.Equal(["Saturday", "Slow ones"], app.Library.Playlists.Select(p => p.Name));
        Assert.False(app.Library.IsPlaylistOpen);   // made from the menu: stays where the DJ is
    }

    [AvaloniaFact]
    public void Rows_of_an_open_playlist_are_dragged_into_a_new_order_and_removed()
    {
        using var app = new AppHarness();
        var songs = app.Library.Results.Take(4).ToList();
        var playlist = Make(app, "Order", songs[0], songs[1], songs[2], songs[3]);
        app.Library.OpenPlaylist(playlist.Id);
        AppHarness.Settle();
        var rows = app.Library.Results.ToList();

        // The last song, dragged to just above the second row.
        var second = Grid(app).GetVisualDescendants().OfType<DataGridRow>().First(r => r.DataContext == rows[1]);
        Point target = second.TranslatePoint(new Point(200, 3), app.Window)!.Value;
        app.MouseDrag(RowPoint(app, rows[3]), target, over: () =>
            Assert.True(app.Window.FindControl<Border>("PlaylistDropLine")!.IsVisible));
        AppHarness.Settle();
        Assert.Equal(new[] { songs[0], songs[3], songs[1], songs[2] }.Select(s => s.DisplayTitle), Shown(app));
        Assert.False(app.Window.FindControl<Border>("PlaylistDropLine")!.IsVisible);

        // Delete takes the selected row out of the playlist; the file stays.
        Grid(app).SelectedItem = app.Library.Results[0];
        app.ViewModel.RemoveFromPlaylist([app.Library.Results[0]]);
        AppHarness.Settle();
        Assert.Equal(new[] { songs[3], songs[1], songs[2] }.Select(s => s.DisplayTitle), Shown(app));
        Assert.True(File.Exists(songs[0].Path));

        // With a search typed, rows can't be reordered (the numbers would be ambiguous).
        app.Library.SearchText = songs[1].DisplayTitle;
        AppHarness.Settle(600);
        Assert.False(app.Library.CanReorderPlaylist);
    }

    [AvaloniaFact]
    public void A_playlist_goes_onto_the_end_of_the_queue_whole_or_from_a_song_on()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var songs = app.Library.Results.Take(5).ToList();
        vm.Queue.Add([songs[4]]);
        var playlist = Make(app, "Set", songs[0], songs[1], songs[2]);

        vm.AddPlaylistToQueue(playlist);
        Assert.Equal(new[] { songs[4], songs[0], songs[1], songs[2] }.Select(s => s.Path), vm.Queue.Items.Select(i => i.Track.Path));

        vm.Queue.Clear();
        app.Library.OpenPlaylist(playlist.Id);
        AppHarness.Settle();
        Grid(app).SelectedItem = app.Library.Results[1];
        app.Window.FindControl<MenuItem>("TrackFromHereMenuItem")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(new[] { songs[1], songs[2] }.Select(s => s.Path), vm.Queue.Items.Select(i => i.Track.Path));
    }

    [AvaloniaFact]
    public void The_queue_saves_as_a_playlist_and_a_deleted_playlist_leaves_the_songs()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var songs = app.Library.Results.Take(3).ToList();
        vm.Queue.Add([songs[2], songs[0], songs[2]]);
        vm.PromptAsync = (_, _, _, _) => Task.FromResult<string?>("Tonight's plan");
        AppHarness.Await(vm.SaveQueueAsPlaylistAsync());
        AppHarness.Settle();

        var saved = app.Library.Playlists.Single();
        Assert.Equal(("Tonight's plan", 3), (saved.Name, saved.Count));
        app.Library.OpenPlaylist(saved.Id);
        AppHarness.Settle();
        Assert.Equal(new[] { songs[2], songs[0], songs[2] }.Select(s => s.DisplayTitle), Shown(app));   // in order, twice over

        string? asked = null;
        vm.ConfirmAsync = (heading, _, _) =>
        {
            asked = heading;
            return Task.FromResult(true);
        };
        AppHarness.Await(vm.DeletePlaylistAsync(saved));
        AppHarness.Settle();
        Assert.Equal("Delete the playlist \"Tonight's plan\"?", asked);
        Assert.Empty(app.Library.Playlists);
        Assert.False(app.Library.IsPlaylistOpen);
        Assert.True(app.Library.Results.Count >= 3);
    }

    [AvaloniaFact]
    public void The_open_playlist_is_remembered()
    {
        using var app = new AppHarness();
        var playlist = Make(app, "Remembered", app.Library.Results[0]);
        app.Library.OpenPlaylist(playlist.Id);
        app.ViewModel.SaveState();
        Assert.Equal(playlist.Id, app.Settings.OpenPlaylist);
    }
}
