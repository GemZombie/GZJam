using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using GZJam.App.ViewModels;
using GZJam.App.Views;

namespace GZJam.App.Tests;

/// <summary>Folding the folders away, or dragging the playlists' grip, for more playlists (#163).</summary>
public sealed class LibraryColumnTests
{
    private static ListBox Playlists(AppHarness app) => app.Window.FindControl<ListBox>("PlaylistList")!;

    private static void MakePlaylists(AppHarness app, int count)
    {
        for (int i = 1; i <= count; i++)
            app.ViewModel.CreatePlaylist($"Set {i:00}", []);
        AppHarness.Settle(150);
    }

    private static IEnumerable<FolderNodeViewModel> AllFolders(AppHarness app) => app.Library.Roots.SelectMany(r => r.SelfAndDescendants());

    [AvaloniaFact]
    public void Folded_only_the_music_folder_shows_and_the_playlists_take_the_rest()
    {
        using var app = new AppHarness();
        MakePlaylists(app, 25);
        Assert.True(app.Library.Roots[0].IsExpanded);   // as GZJam starts: the music folder open
        Assert.InRange(Playlists(app).Bounds.Height, 200, MainWindow.DefaultPlaylistsHeight);

        var fold = app.Window.FindControl<Button>("FoldFoldersButton")!;
        var middle = fold.TranslatePoint(new Point(fold.Bounds.Width / 2, fold.Bounds.Height / 2), app.Window)!.Value;
        app.Window.MouseDown(middle, MouseButton.Left, RawInputModifiers.None);
        app.Window.MouseUp(middle, MouseButton.Left, RawInputModifiers.None);
        AppHarness.Settle(150);
        app.Screenshot("library-folded");

        Assert.True(app.Settings.FoldersFolded);   // remembered
        Assert.DoesNotContain(AllFolders(app), n => n.IsExpanded);
        Assert.True(Playlists(app).Bounds.Height > MainWindow.DefaultPlaylistsHeight + 100, $"playlists {Playlists(app).Bounds.Height} px");
        Assert.True(app.Window.FindControl<TreeView>("FolderTree")!.Bounds.Height < 80, "one row for the music folder");
        Assert.False(app.Window.FindControl<Control>("PlaylistsGrip")!.IsVisible);
        Assert.Equal("Show the folders", app.Library.FoldTip);

        // The music folder is still there to tap for the whole library.
        app.Library.SelectedFolder = app.Library.Roots[0];
        AppHarness.Settle(150);
        Assert.True(app.Library.Results.Count > 10);

        // Opening it in the tree unfolds.
        app.Library.Roots[0].IsExpanded = true;
        AppHarness.Settle(150);
        Assert.False(app.Library.AreFoldersFolded);
        Assert.False(app.Settings.FoldersFolded);
        Assert.InRange(Playlists(app).Bounds.Height, 200, MainWindow.DefaultPlaylistsHeight);

        // Folded and unfolded by the arrow, the music folder comes back open.
        app.Library.ToggleFoldFoldersCommand.Execute(null);
        app.Library.ToggleFoldFoldersCommand.Execute(null);
        Assert.True(app.Library.Roots[0].IsExpanded);
    }

    [AvaloniaFact]
    public void GZJam_starts_folded_if_it_was_left_folded()
    {
        using var app = new AppHarness(settings: new AppSettings { MatchLoudness = false, FoldersFolded = true });
        Assert.True(app.Library.AreFoldersFolded);
        Assert.DoesNotContain(AllFolders(app), n => n.IsExpanded);
    }

    [AvaloniaFact]
    public void The_grip_makes_the_playlists_taller_or_shorter_and_leaves_the_folders_some_room()
    {
        using var app = new AppHarness();
        MakePlaylists(app, 25);
        var grip = app.Window.FindControl<Control>("PlaylistsGrip")!;
        Point at() => grip.TranslatePoint(new Point(grip.Bounds.Width / 2, grip.Bounds.Height / 2), app.Window)!.Value;
        double before = Playlists(app).Bounds.Height;

        var from = at();
        app.MouseDrag(from, from + new Point(0, -100));
        Assert.Equal(before + 100, Playlists(app).Bounds.Height, 1);
        Assert.Equal(before + 100, app.Settings.PlaylistsHeight!.Value, 1);   // remembered

        from = at();
        app.MouseDrag(from, from + new Point(0, 60));
        Assert.Equal(before + 40, Playlists(app).Bounds.Height, 1);

        // All the way up: the folders keep a couple of rows.
        from = at();
        app.MouseDrag(from, new Point(from.X, 0));
        Assert.True(app.Window.FindControl<TreeView>("FolderTree")!.Bounds.Height >= 69);

        // All the way down: two rows of playlists stay.
        from = at();
        app.MouseDrag(from, new Point(from.X, 899));
        Assert.Equal(60, Playlists(app).Bounds.Height, 1);
    }

    [AvaloniaFact]
    public void A_dragged_height_comes_back_next_time()
    {
        using var app = new AppHarness(settings: new AppSettings { MatchLoudness = false, PlaylistsHeight = 320 });
        MakePlaylists(app, 25);
        Assert.Equal(320, Playlists(app).Bounds.Height, 1);
    }
}
