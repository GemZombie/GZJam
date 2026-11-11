using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using GZJam.Library;
using Track = GZJam.Library.Track;

namespace GZJam.App.Tests;

/// <summary>One clear selection (#377): only the list the keys act on shows its selection in full.</summary>
public sealed class SelectionAppTests
{
    private static DataGrid Grid(AppHarness app) => app.Window.FindControl<DataGrid>("TrackGrid")!;
    private static ListBox Queue(AppHarness app) => app.Window.FindControl<ListBox>("QueueList")!;
    private static ListBox Folders(AppHarness app) => app.Window.FindControl<ListBox>("FolderTree")!;

    private static IBrush Brush(string key) => (IBrush)Application.Current!.FindResource(key)!;

    /// <summary>A song selected in the library, one in the queue, a folder open and a playlist made.</summary>
    private static void ANight(AppHarness app)
    {
        var songs = app.Library.Results.ToList();
        app.ViewModel.CreatePlaylist("Saturday set", [songs[0], songs[1]]);
        app.ViewModel.AddToQueue([songs[2], songs[3], songs[4]]);
        app.Library.SelectedFolder = app.Library.Roots.First(r => r.IsFolder);
        AppHarness.WaitFor(() => !app.Library.IsSearching);
        AppHarness.Settle(200);
        Grid(app).SelectedIndex = 2;
        Queue(app).SelectedIndex = 1;
        Grid(app).Focus();
        AppHarness.Settle(200);
    }

    /// <summary>The fill of the song list's selected row.</summary>
    private static IBrush? SongFill(AppHarness app) =>
        Grid(app).GetVisualDescendants().OfType<DataGridRow>().Single(r => r.IsSelected)
            .GetVisualDescendants().OfType<Rectangle>().Single(r => r.Name == "BackgroundRectangle").Fill;

    private static ContentPresenter Presenter(ListBoxItem item) =>
        item.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");

    private static IBrush? QueueFill(AppHarness app) => Presenter((ListBoxItem)Queue(app).ContainerFromIndex(1)!).Background;

    [AvaloniaFact]
    public void Only_the_list_the_keys_act_on_shows_its_selection_in_full()
    {
        using var app = new AppHarness();
        ANight(app);

        // In the song list: its selection strong, the queue's faint, and kept.
        Assert.Same(Grid(app), app.Window.ActiveList);
        Assert.Same(Brush("SelectionActiveBrush"), SongFill(app));
        Assert.Same(Brush("SelectionInactiveBrush"), QueueFill(app));
        Assert.Single(Queue(app).SelectedItems!);
        app.Screenshot("selection-library-active");

        // Into the queue: the other way round.
        ((ListBoxItem)Queue(app).ContainerFromIndex(1)!).Focus();
        AppHarness.Settle(150);
        Assert.Same(Queue(app), app.Window.ActiveList);
        Assert.Same(Brush("SelectionActiveBrush"), QueueFill(app));
        Assert.Same(Brush("SelectionInactiveBrush"), SongFill(app));
        app.Screenshot("selection-queue-active");

        // The search box is the song list's: back to it.
        app.Window.FindControl<TextBox>("SearchBox")!.Focus();
        AppHarness.Settle(150);
        Assert.Same(Grid(app), app.Window.ActiveList);
        Assert.Same(Brush("SelectionActiveBrush"), SongFill(app));

        // Hover is lighter than a selection, even a faint one.
        Assert.True(((ISolidColorBrush)Brush("RowHoverBrush")).Color.A < ((ISolidColorBrush)Brush("SelectionInactiveBrush")).Color.A);
    }

    [AvaloniaFact]
    public void The_open_folder_is_a_place_not_a_selection()
    {
        using var app = new AppHarness();
        ANight(app);
        var open = Folders(app).GetVisualDescendants().OfType<ListBoxItem>().Single(i => i.IsSelected);
        ContentPresenter presenter = Presenter(open);

        // An accent bar at its edge and its name in the accent color, no fill to compete with the song.
        Assert.Equal(Colors.Transparent, ((ISolidColorBrush)presenter.Background!).Color);
        Assert.True(presenter.BoxShadow.Count > 0 && presenter.BoxShadow[0].IsInset);
        Assert.Equal(((ISolidColorBrush)Brush("AccentBrush")).Color, ((ISolidColorBrush)presenter.Foreground!).Color);

        // With the keys in the folders (arrow keys go through them), it's their selection too.
        open.Focus();
        AppHarness.Settle(150);
        Assert.Same(Folders(app), app.Window.ActiveList);
        Assert.Same(Brush("SelectionActiveBrush"), presenter.Background);
        Assert.Same(Brush("SelectionInactiveBrush"), SongFill(app));
    }
}
