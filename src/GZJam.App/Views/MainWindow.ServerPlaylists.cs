using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GZJam.App.ViewModels;

namespace GZJam.App.Views;

/// <summary>
/// The servers' playlists in the Playlists panel (#355): a server's heading and its folders open
/// and close with a click, ↻ reads its playlists again, and each playlist's menu plays it, adds it
/// to the queue, keeps it on the computer (or stops) and copies it into the DJ's own playlists.
/// </summary>
public partial class MainWindow
{
    private static ServerPlaylistRow? ServerPlaylistOf(object? sender) => (sender as Control)?.DataContext as ServerPlaylistRow;

    private void OnServerHeadingTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ServerPlaylistsHeading heading && e.Source is not Button)
            heading.IsExpanded = !heading.IsExpanded;
    }

    private void OnServerFolderTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ServerPlaylistFolderRow folder)
            folder.IsExpanded = !folder.IsExpanded;
    }

    private void OnReadServerPlaylistsClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ServerPlaylistsHeading heading)
            _ = Vm.Servers.ReadPlaylistsAgainAsync(heading.ServerId);
        e.Handled = true;   // not a click on the heading, which folds it
    }

    private void OnServerPlaylistPlayClick(object? sender, RoutedEventArgs e)
    {
        if (ServerPlaylistOf(sender) is { } playlist)
            _ = Vm.Servers.PlayPlaylistAsync(playlist);
    }

    private void OnServerPlaylistPlayShuffledClick(object? sender, RoutedEventArgs e)
    {
        if (ServerPlaylistOf(sender) is { } playlist)
            _ = Vm.Servers.PlayPlaylistAsync(playlist, shuffled: true);
    }

    private void OnServerPlaylistToQueueClick(object? sender, RoutedEventArgs e)
    {
        if (ServerPlaylistOf(sender) is { } playlist)
            _ = Vm.Servers.AddPlaylistToQueueAsync(playlist);
    }

    private void OnServerPlaylistToQueueShuffledClick(object? sender, RoutedEventArgs e)
    {
        if (ServerPlaylistOf(sender) is { } playlist)
            _ = Vm.Servers.AddPlaylistToQueueAsync(playlist, shuffled: true);
    }

    private void OnKeepPanelServerPlaylistClick(object? sender, RoutedEventArgs e)
    {
        if (ServerPlaylistOf(sender) is { } playlist)
            _ = Vm.Servers.KeepPlaylistAsync(playlist.ServerId, playlist.PlaylistId);
    }

    private void OnStopKeepingPanelServerPlaylistClick(object? sender, RoutedEventArgs e)
    {
        if (ServerPlaylistOf(sender) is { } playlist)
            _ = Vm.Servers.StopKeepingPlaylistAsync(playlist.ServerId, playlist.PlaylistId);
    }

    private void OnStopFollowingPanelServerPlaylistClick(object? sender, RoutedEventArgs e)
    {
        if (ServerPlaylistOf(sender) is { Kept: { } kept })
            Vm.Servers.StopFollowing(kept);
    }

    private void OnCopyServerPlaylistClick(object? sender, RoutedEventArgs e)
    {
        if (ServerPlaylistOf(sender) is { } playlist)
            _ = Vm.Servers.CopyPlaylistAsync(playlist);
    }
}
