using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Views;

/// <summary>
/// Playlists: listed under the folder tree; one opens in the song list in its own order (numbered,
/// with a bar above to add it to the queue). Songs are added by dragging them onto a playlist's
/// name or from the songs' menu, and reordered by dragging rows within an open playlist.
/// </summary>
public partial class MainWindow
{
    private void InitializePlaylists()
    {
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
                vm.Library.PropertyChanged += OnLibraryChangedForPlaylists;
        };
    }

    private void OnLibraryChangedForPlaylists(object? sender, PropertyChangedEventArgs e)
    {
        // A playlist shows in its own order: whatever column the library was sorted by doesn't apply.
        if (e.PropertyName == nameof(LibraryViewModel.IsPlaylistOpen) && Vm.Library.IsPlaylistOpen)
            TrackGrid.CollectionView?.SortDescriptions.Clear();
    }

    private void OnNewPlaylistClick(object? sender, RoutedEventArgs e) => _ = Vm.CreatePlaylistAsync();

    private void OnPlaylistToQueueClick(object? sender, RoutedEventArgs e)
    {
        if (Vm.Library.SelectedPlaylist is { } playlist)
            Vm.AddPlaylistToQueue(playlist);
    }

    private void OnRenameOpenPlaylistClick(object? sender, RoutedEventArgs e)
    {
        if (Vm.Library.SelectedPlaylist is { } playlist)
            _ = Vm.RenamePlaylistAsync(playlist);
    }

    private void OnDeleteOpenPlaylistClick(object? sender, RoutedEventArgs e)
    {
        if (Vm.Library.SelectedPlaylist is { } playlist)
            _ = Vm.DeletePlaylistAsync(playlist);
    }

    private static PlaylistInfo? PlaylistOf(object? sender) => (sender as Control)?.DataContext as PlaylistInfo;

    private void OnPlaylistItemToQueueClick(object? sender, RoutedEventArgs e)
    {
        if (PlaylistOf(sender) is { } playlist)
            Vm.AddPlaylistToQueue(playlist);
    }

    private void OnRenamePlaylistItemClick(object? sender, RoutedEventArgs e)
    {
        if (PlaylistOf(sender) is { } playlist)
            _ = Vm.RenamePlaylistAsync(playlist);
    }

    private void OnDeletePlaylistItemClick(object? sender, RoutedEventArgs e)
    {
        if (PlaylistOf(sender) is { } playlist)
            _ = Vm.DeletePlaylistAsync(playlist);
    }

    private void OnPlaylistFromHereClick(object? sender, RoutedEventArgs e)
    {
        if (Vm.Library.SelectedPlaylist is { } playlist && SelectedTracks().MinBy(t => t.PlaylistNumber) is { } from)
            Vm.AddPlaylistToQueue(playlist, from);
    }

    private void OnRemoveFromPlaylistClick(object? sender, RoutedEventArgs e) => Vm.RemoveFromPlaylist(SelectedTracks());

    private void OnQueueToPlaylistClick(object? sender, RoutedEventArgs e) => _ = Vm.SaveQueueAsPlaylistAsync();

    private void OnSessionToPlaylistClick(object? sender, RoutedEventArgs e) => _ = Vm.SaveSessionAsPlaylistAsync();

    /// <summary>The songs' menu: "Add to playlist" › each playlist, and "New playlist…"; the open playlist's own items.</summary>
    private void FillPlaylistItems(IReadOnlyList<Track> tracks)
    {
        bool open = Vm.Library.IsPlaylistOpen;
        TrackFromHereMenuItem.IsVisible = open;
        RemoveFromPlaylistMenuItem.IsVisible = open;
        TrackFromHereMenuItem.IsEnabled = RemoveFromPlaylistMenuItem.IsEnabled = tracks.Count > 0;

        TrackPlaylistsMenu.Items.Clear();
        foreach (PlaylistInfo playlist in Vm.Library.Playlists.Where(p => p.Id != Vm.Library.SelectedPlaylist?.Id))
        {
            var item = new MenuItem { Header = playlist.Name, IsEnabled = tracks.Count > 0 };
            item.Click += (_, _) => Vm.AddToPlaylist(playlist, tracks);
            TrackPlaylistsMenu.Items.Add(item);
        }
        if (TrackPlaylistsMenu.Items.Count > 0)
            TrackPlaylistsMenu.Items.Add(new Separator());
        var create = new MenuItem { Header = "New playlist…", IsEnabled = tracks.Count > 0 };
        create.Click += (_, _) => _ = Vm.CreatePlaylistAsync(tracks, open: false);
        TrackPlaylistsMenu.Items.Add(create);
    }

    // ───────────── Dragging onto playlists, and within one ─────────────

    /// <summary>The playlist row under <paramref name="at"/> (window coordinates), if any.</summary>
    private ListBoxItem? PlaylistItemAt(Point at)
    {
        if (!IsOver(PlaylistList, at))
            return null;
        return PlaylistList.GetVisualDescendants().OfType<ListBoxItem>().FirstOrDefault(item => IsOver(item, at));
    }

    /// <summary>
    /// Where songs dropped at <paramref name="at"/> would go in the open playlist: before the
    /// entry with this index (from 0), and the line's height in the song list. Null if the
    /// playlist can't be reordered (it's filtered by a search) or the point isn't over the list.
    /// </summary>
    private (int Index, double LineY)? PlaylistDropAt(Point at)
    {
        if (!Vm.Library.CanReorderPlaylist || !IsOver(TrackGrid, at))
            return null;
        var rows = TrackGrid.GetVisualDescendants().OfType<DataGridRow>()
            .Where(r => r.IsVisible && r.DataContext is Track { PlaylistNumber: not null })
            .Select(r => (Row: r, Top: r.TranslatePoint(default, TrackGrid)?.Y ?? double.NaN))
            .Where(r => !double.IsNaN(r.Top))
            .OrderBy(r => r.Top)
            .ToList();
        double y = this.TranslatePoint(at, TrackGrid)?.Y ?? 0;
        double gridTop = TrackGrid.TranslatePoint(default, (Visual)PlaylistDropLine.Parent!)?.Y ?? 0;
        foreach (var (row, top) in rows)
        {
            if (y < top + row.Bounds.Height / 2)
                return (((Track)row.DataContext!).PlaylistNumber!.Value - 1, gridTop + top);
        }
        if (rows.Count == 0)
            return (0, gridTop + (TrackGrid.Bounds.Height > 40 ? 34 : 0));
        var (last, lastTop) = rows[^1];
        return (((Track)last.DataContext!).PlaylistNumber!.Value, gridTop + lastTop + last.Bounds.Height);
    }

    /// <summary>Shows where a drag over the playlists or an open playlist would put the songs.</summary>
    private void UpdatePlaylistDrop(Point at)
    {
        ListBoxItem? over = PlaylistItemAt(at);
        foreach (ListBoxItem item in PlaylistList.GetVisualDescendants().OfType<ListBoxItem>())
            item.Classes.Set("drop-target", item == over);
        if (PlaylistDropAt(at) is { } drop)
        {
            PlaylistDropLine.Margin = new Thickness(8, drop.LineY - 1, 8, 0);
            PlaylistDropLine.IsVisible = true;
        }
        else
        {
            PlaylistDropLine.IsVisible = false;
        }
    }

    private void EndPlaylistDrop()
    {
        foreach (ListBoxItem item in PlaylistList.GetVisualDescendants().OfType<ListBoxItem>())
            item.Classes.Set("drop-target", false);
        PlaylistDropLine.IsVisible = false;
    }

    /// <summary>
    /// Drops songs on a playlist's name (added at its end) or into the open playlist (moved, if
    /// they're its own rows; else added there). True if it was a playlist drop.
    /// </summary>
    private bool DropOnPlaylist(TrackDrag drag, Point at, out string where)
    {
        if (PlaylistItemAt(at)?.DataContext is PlaylistInfo playlist)
        {
            where = $"playlist \"{playlist.Name}\"";
            Vm.AddToPlaylist(playlist, drag.Tracks);
            return true;
        }
        if (PlaylistDropAt(at) is { } drop && Vm.Library.SelectedPlaylist is { } open)
        {
            where = $"position {drop.Index + 1} in playlist \"{open.Name}\"";
            if (drag.Tracks.All(t => t.PlaylistEntryId is not null) && drag.FromQueue is null)
                Vm.MoveInPlaylist(drag.Tracks, drop.Index);
            else
                Vm.AddToPlaylist(open, drag.Tracks, drop.Index);
            return true;
        }
        where = "";
        return false;
    }
}
