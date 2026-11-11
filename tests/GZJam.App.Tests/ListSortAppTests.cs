using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using GZJam.App.ViewModels;
using GZJam.Library;
using Track = GZJam.Library.Track;

namespace GZJam.App.Tests;

/// <summary>The song list keeps its sort (#376): the library one, each playlist its own, between runs too.</summary>
public sealed class ListSortAppTests : IDisposable
{
    private readonly string _work = Directory.CreateTempSubdirectory("gzjam-sorts-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_work, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static DataGrid Grid(AppHarness app) => app.Window.FindControl<DataGrid>("TrackGrid")!;

    /// <summary>The songs in the order the list shows them.</summary>
    private static List<string> Shown(AppHarness app) => Grid(app).CollectionView!.Cast<Track>().Select(t => t.DisplayTitle).ToList();

    /// <summary>The list's sort: its path and direction, or null in its own order.</summary>
    private static (string? Path, ListSortDirection Direction)? Sort(AppHarness app) =>
        Grid(app).CollectionView!.SortDescriptions is [var only] ? (only.PropertyPath, only.Direction) : null;

    private static void ClickHeader(AppHarness app, string name)
    {
        var header = Grid(app).GetVisualDescendants().OfType<DataGridColumnHeader>().First(h => (string?)h.Content == name);
        var at = header.TranslatePoint(new Point(10, header.Bounds.Height / 2), app.Window)!.Value;
        app.Window.MouseDown(at, MouseButton.Left);
        app.Window.MouseUp(at, MouseButton.Left);
        AppHarness.Settle(100);
    }

    private static void Open(AppHarness app, long? playlist)
    {
        app.Library.OpenPlaylist(playlist);
        AppHarness.WaitFor(() => !app.Library.IsSearching);
        AppHarness.Settle(150);
    }

    [AvaloniaFact]
    public void The_library_keeps_its_sort_through_a_playlist_and_each_playlist_keeps_its_own_after_a_restart()
    {
        List<string> planned, other;
        using (var app = new AppHarness(workDirectory: _work))
        {
            var songs = app.Library.Results.ToList();
            planned = [songs[4].DisplayTitle, songs[0].DisplayTitle, songs[2].DisplayTitle];
            other = [songs[1].DisplayTitle, songs[3].DisplayTitle];
            var set = app.ViewModel.CreatePlaylist("Saturday set", [songs[4], songs[0], songs[2]])!;
            var warmup = app.ViewModel.CreatePlaylist("Warm-up", [songs[1], songs[3]])!;
            Open(app, null);

            // The library sorted by BPM, then a playlist: in its own order, a planned set.
            ClickHeader(app, "BPM");
            Assert.Equal(("Bpm", ListSortDirection.Ascending), Sort(app));
            var byBpm = app.Library.Results.OrderBy(t => t.Bpm).Select(t => t.DisplayTitle).ToList();
            Assert.Equal(byBpm, Shown(app));
            Open(app, set.Id);
            Assert.Null(Sort(app));
            Assert.Equal(planned, Shown(app));
            Assert.True(app.Library.CanReorderPlaylist);

            // Sorted by Artist, descending: this playlist alone; the other stays in its order.
            ClickHeader(app, "Artist");
            ClickHeader(app, "Artist");
            Assert.Equal(("DisplayArtist", ListSortDirection.Descending), Sort(app));
            Assert.False(app.Library.CanReorderPlaylist);   // dragging rows means nothing in a sorted list
            Open(app, warmup.Id);
            Assert.Null(Sort(app));
            Assert.Equal(other, Shown(app));

            // Back to the library, and a folder in it: still by BPM.
            Open(app, null);
            Assert.Equal(("Bpm", ListSortDirection.Ascending), Sort(app));
            app.Library.SelectedFolder = app.Library.Roots.First(r => r.IsFolder);
            AppHarness.WaitFor(() => !app.Library.IsSearching);
            AppHarness.Settle(150);
            Assert.Equal(("Bpm", ListSortDirection.Ascending), Sort(app));

            // A search keeps it too.
            app.Library.SearchText = "Count Basie";
            AppHarness.WaitFor(() => !app.Library.IsSearching);
            AppHarness.Settle(150);
            Assert.Equal(("Bpm", ListSortDirection.Ascending), Sort(app));
            app.Library.SearchText = "";
            AppHarness.WaitFor(() => !app.Library.IsSearching);

            Open(app, set.Id);
            Assert.Equal(("DisplayArtist", ListSortDirection.Descending), Sort(app));
            app.ViewModel.SaveState();
            app.Settings.Save(Path.Combine(_work, "settings.json"));
        }

        // Started again: each list as it was left.
        var settings = AppSettings.Load(Path.Combine(_work, "settings.json"));
        using (var app = new AppHarness(workDirectory: _work, settings: settings))
        {
            AppHarness.Settle(200);
            Assert.True(app.Library.IsPlaylistOpen);
            Assert.Equal(("DisplayArtist", ListSortDirection.Descending), Sort(app));

            // A third click: back to the playlist's own order, which can be dragged again.
            ClickHeader(app, "Artist");
            Assert.Null(Sort(app));
            Assert.Equal(planned, Shown(app));
            Assert.True(app.Library.CanReorderPlaylist);

            Open(app, null);
            Assert.Equal(("Bpm", ListSortDirection.Ascending), Sort(app));
            ClickHeader(app, "BPM");
            ClickHeader(app, "BPM");
            Assert.Null(Sort(app));   // the library's own order
            Assert.Empty(settings.ListSorts);
        }
    }

    [AvaloniaFact]
    public void A_deleted_playlist_takes_its_sort_with_it()
    {
        using var app = new AppHarness(workDirectory: _work);
        var gone = app.ViewModel.CreatePlaylist("Tonight only", app.Library.Results.Take(2).ToList())!;
        Open(app, gone.Id);
        ClickHeader(app, "Title");
        Assert.True(app.Settings.ListSorts.ContainsKey(LibraryViewModel.PlaylistSortKey(gone.Id)));

        app.ViewModel.ConfirmAsync = (_, _, _) => Task.FromResult(true);
        AppHarness.Await(app.ViewModel.DeletePlaylistAsync(app.Library.Playlists.Single(p => p.Id == gone.Id)));
        Assert.Empty(app.Settings.ListSorts);
    }
}
