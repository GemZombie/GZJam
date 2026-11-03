using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Views;

/// <summary>
/// Double-clicking a song (#260), as in any player, but never straight to the room: it loads onto
/// the deck the room isn't hearing, or with auto-play on, plays next (MainWindowViewModel.PlaySongAsync).
/// </summary>
public partial class MainWindow
{
    private void InitializeDoubleClick()
    {
        TrackGrid.AddHandler(InputElement.DoubleTappedEvent, OnTrackGridDoubleTapped, RoutingStrategies.Bubble, handledEventsToo: true);
        HistoryList.AddHandler(InputElement.DoubleTappedEvent, OnHistoryDoubleTapped, RoutingStrategies.Bubble, handledEventsToo: true);
        QueueList.AddHandler(InputElement.DoubleTappedEvent, OnQueueDoubleTapped, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>The row's item under a double-click, unless it was on a key in the row (the ♥, say) or a header.</summary>
    private static T? DoubleClicked<T>(RoutedEventArgs e) where T : class
    {
        if (e.Source is not Visual source || source.FindAncestorOfType<Button>(includeSelf: true) is not null)
            return null;
        return (source as StyledElement)?.DataContext as T;
    }

    private async void OnTrackGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DoubleClicked<Track>(e) is not { } track)
            return;
        e.Handled = true;
        await Vm.PlaySongAsync(track);
    }

    private async void OnHistoryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DoubleClicked<PlayItem>(e) is not { } play)
            return;
        e.Handled = true;
        await Vm.PlaySongAsync(play.Track);
    }

    private async void OnQueueDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DoubleClicked<QueueItem>(e) is not { } item)
            return;
        e.Handled = true;
        await Vm.PlaySongAsync(item.Track, item);
    }
}
