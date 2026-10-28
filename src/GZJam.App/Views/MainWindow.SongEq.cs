using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Views;

/// <summary>
/// A song's EQ without loading it on a deck (#137): its right-click menu › EQ… in the song list
/// (and an open playlist), the queue or History opens the deck's EQ panel for it, beside its row.
/// Heard in the headphone preview, and used when the song is loaded.
/// </summary>
public partial class MainWindow
{
    private void OnTrackEqClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedTracks() is [var track])
        {
            var row = TrackGrid.GetVisualDescendants().OfType<DataGridRow>().FirstOrDefault(r => r.DataContext == track);
            ShowSongEq(track, row ?? (Control)TrackGrid, PlacementMode.BottomEdgeAlignedLeft);
        }
    }

    private void OnQueueEqClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedQueueItems() is [var item])
            ShowSongEq(item.Track, QueueList.ContainerFromItem(item) ?? QueueList, PlacementMode.LeftEdgeAlignedTop);
    }

    private void OnHistoryEqClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedPlays() is [var play])
            ShowSongEq(play.Track, HistoryList.ContainerFromItem(play) ?? HistoryList, PlacementMode.LeftEdgeAlignedTop);
    }

    /// <summary>The song's EQ panel, open beside <paramref name="at"/>.</summary>
    internal EqEditor ShowSongEq(Track track, Control at, PlacementMode placement)
    {
        EqEditor editor = Vm.EqFor(track);
        var flyout = new Flyout { Placement = placement, Content = new EqPanel { DataContext = editor } };
        flyout.FlyoutPresenterClasses.Add("eq");
        flyout.ShowAt(at);
        Log.Info($"EQ panel for \"{track.DisplayTitle}\"{(editor.IsSet ? ": " + editor.Describe() : "")}");
        return editor;
    }
}
