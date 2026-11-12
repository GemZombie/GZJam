using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using GZJam.App.ViewModels;
using GZJam.App.Views;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>
/// Playlists as a source (#423): with the whole column, in place of the folder tree, not sharing it
/// with the tree. Opening a playlist from anywhere shows it; songs dragged and held over the
/// Playlists source open it, the song list staying as it was.
/// </summary>
public sealed class LibraryColumnTests
{
    private static ListBox Playlists(AppHarness app) => app.Window.FindControl<ListBox>("PlaylistList")!;

    private static Control Tree(AppHarness app) => app.Window.FindControl<Control>("FolderTree")!;

    private static ListBoxItem SourceRow(AppHarness app, LibrarySource kind) =>
        app.Window.FindControl<ListBox>("SourceList")!.GetVisualDescendants().OfType<ListBoxItem>()
            .First(i => i.DataContext is SourceViewModel s && s.Kind == kind);

    private static void MakePlaylists(AppHarness app, int count)
    {
        for (int i = 1; i <= count; i++)
            app.ViewModel.CreatePlaylist($"Set {i:00}", []);
        AppHarness.Settle(150);
    }

    [AvaloniaFact]
    public void Playlists_is_a_source_with_the_whole_column_in_place_of_the_folder_tree()
    {
        using var app = new AppHarness();
        MakePlaylists(app, 30);

        // My music: the folder tree, and no playlists under it.
        Assert.True(Tree(app).IsEffectivelyVisible);
        Assert.False(Playlists(app).IsEffectivelyVisible);
        double treeHeight = Tree(app).Bounds.Height;

        app.Library.ChooseSource(LibrarySource.Playlists);
        AppHarness.Settle(150);

        Assert.False(Tree(app).IsEffectivelyVisible);
        Assert.True(Playlists(app).IsEffectivelyVisible);
        // As tall as the tree was, near enough: the column, less the playlists' header.
        Assert.True(Playlists(app).Bounds.Height > treeHeight - 60, $"playlists {Playlists(app).Bounds.Height} px, the tree had {treeHeight}");
        // With none open, the song list is My music's, to drop songs from onto them.
        Assert.Equal(15, app.Library.Results.Count);
        Assert.Contains("Search", app.Library.SearchPlaceholder);
        app.Screenshot("playlists-source");
    }

    [AvaloniaFact]
    public void Opening_a_playlist_from_anywhere_shows_the_playlists_and_a_folder_shows_the_tree_again()
    {
        using var app = new AppHarness();
        PlaylistInfo friday = app.ViewModel.CreatePlaylist("Friday", [app.Library.Results[0]])!;
        Assert.Equal(LibrarySource.MyMusic, app.Library.Source);

        app.Library.OpenPlaylist(friday.Id);   // as making one, or "Open in my playlists", does
        AppHarness.Settle(150);
        Assert.Equal(LibrarySource.Playlists, app.Library.Source);
        Assert.True(Playlists(app).IsEffectivelyVisible);
        Assert.Single(app.Library.Results);

        app.Library.SelectedFolder = app.Library.Roots.First(r => r.IsFolder);
        AppHarness.Settle(150);
        Assert.Equal(LibrarySource.MyMusic, app.Library.Source);
        Assert.True(Tree(app).IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void The_playlists_source_comes_back_next_time()
    {
        var settings = new AppSettings { MatchLoudness = false, OnAirPause = OnAirPause.Tap };
        string work = Directory.CreateTempSubdirectory("gzjam-app-test-").FullName;
        try
        {
            using (var app = new AppHarness(settings: settings, workDirectory: work))
            {
                app.Library.ChooseSource(LibrarySource.Playlists);
                AppHarness.Settle(100);
                app.ViewModel.SaveState();
            }
            using var again = new AppHarness(settings: settings, workDirectory: work);
            Assert.Equal(LibrarySource.Playlists, again.Library.Source);
            Assert.True(Playlists(again).IsEffectivelyVisible);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>A song dragged from a folder and held over Playlists: it opens, the folder's songs still showing, and the song drops onto a playlist.</summary>
    [AvaloniaFact]
    public void Songs_held_over_the_playlists_source_open_it_and_drop_onto_a_playlist()
    {
        using var app = new AppHarness();
        app.ViewModel.CreatePlaylist("Friday", []);
        app.Library.SelectedFolder = app.Library.Roots.First(r => r.IsFolder).Children.First(c => c.Name == "Swing");
        AppHarness.Settle(300);
        var shown = app.Library.Results;
        Track song = shown[0];
        var grid = app.Window.FindControl<DataGrid>("TrackGrid")!;
        var row = grid.GetVisualDescendants().OfType<DataGridRow>().First(r => r.DataContext == song);
        Point from = row.TranslatePoint(new Point(200, row.Bounds.Height / 2), app.Window)!.Value;
        ListBoxItem source = SourceRow(app, LibrarySource.Playlists);
        Point over = source.TranslatePoint(new Point(30, source.Bounds.Height / 2), app.Window)!.Value;

        app.Window.MouseDown(from, MouseButton.Left, RawInputModifiers.None);
        app.Window.MouseMove(new Point(from.X + 20, from.Y), RawInputModifiers.LeftMouseButton);
        app.Window.MouseMove(over, RawInputModifiers.LeftMouseButton);
        AppHarness.Settle(80);
        Assert.Contains("drop-target", source.Classes);   // it shows it'll open
        Assert.Equal(LibrarySource.MyMusic, app.Library.Source);
        AppHarness.Settle((int)MainWindow.SpringOpen.TotalMilliseconds + 200);   // held still

        Assert.Equal(LibrarySource.Playlists, app.Library.Source);
        Assert.Same(shown, app.Library.Results);   // the folder's songs, still under the drag
        var friday = Playlists(app).GetVisualDescendants().OfType<ListBoxItem>().First(i => i.DataContext is PlaylistInfo { Name: "Friday" });
        Point onFriday = friday.TranslatePoint(new Point(40, friday.Bounds.Height / 2), app.Window)!.Value;
        app.Window.MouseMove(onFriday, RawInputModifiers.LeftMouseButton);
        AppHarness.Settle(50);
        app.Window.MouseUp(onFriday, MouseButton.Left, RawInputModifiers.None);
        AppHarness.Settle(150);

        Assert.Equal(1, app.Library.Playlists.Single(p => p.Name == "Friday").Count);
    }
}
