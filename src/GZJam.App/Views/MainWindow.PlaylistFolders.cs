using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Views;

/// <summary>
/// Folders in the Playlists panel (#354): made from + New or a folder's menu, opened and closed by
/// a click (as the folder tree's), and playlists and folders moved between them by Move to (by
/// touch, no drag needed) or by dragging one onto a folder, or onto the panel but off the rows
/// (its header, the space under them) for the top.
/// </summary>
public partial class MainWindow
{
    private static PlaylistFolderRow? FolderRowOf(object? sender) => (sender as Control)?.DataContext as PlaylistFolderRow;

    private void OnNewPlaylistFolderClick(object? sender, RoutedEventArgs e) => _ = Vm.CreatePlaylistFolderAsync();

    private void OnNewPlaylistInFolderClick(object? sender, RoutedEventArgs e)
    {
        if (FolderRowOf(sender) is { } folder)
            _ = Vm.CreatePlaylistAsync(folderId: folder.Id);
    }

    private void OnNewFolderInFolderClick(object? sender, RoutedEventArgs e)
    {
        if (FolderRowOf(sender) is { } folder)
            _ = Vm.CreatePlaylistFolderAsync(folder.Id);
    }

    private void OnRenamePlaylistFolderClick(object? sender, RoutedEventArgs e)
    {
        if (FolderRowOf(sender) is { } folder)
            _ = Vm.RenamePlaylistFolderAsync(folder);
    }

    private void OnDeletePlaylistFolderClick(object? sender, RoutedEventArgs e)
    {
        if (FolderRowOf(sender) is { } folder)
            _ = Vm.DeletePlaylistFolderAsync(folder);
    }

    /// <summary>A click or tap on a folder's row (not its arrow): it opens, or closes, and new playlists go in it.</summary>
    private void OnPlaylistFolderTapped(object? sender, TappedEventArgs e)
    {
        if (FolderRowOf(sender) is not { } folder)
            return;
        Vm.Library.ChosenPlaylistFolder = folder;
        if (folder.HasChildren)
            folder.IsExpanded = !folder.IsExpanded;
    }

    /// <summary>A folder's menu, about to open: Move to, the top and every folder it can go in.</summary>
    private void OnPlaylistFolderMenuRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control { ContextMenu: { } menu, DataContext: PlaylistFolderRow folder })
            return;
        var within = Vm.FoldersWithin(folder.Id);
        if (menu.Items.OfType<MenuItem>().FirstOrDefault(i => i.Tag is "move") is { } move)
            FillMoveTo(move, folder.Folder.ParentId, f => !within.Contains(f.Id), to => Vm.MovePlaylistFolder(folder, to));
    }

    /// <summary>
    /// Move to's choices: the top, then each folder by its path ("Setlists › 2026"), where it is
    /// now ticked and left out of the choosing.
    /// </summary>
    private void FillMoveTo(MenuItem move, long? now, Func<PlaylistFolder, bool> allowed, Action<long?> to)
    {
        move.Items.Clear();
        void Add(string header, long? folder)
        {
            bool here = folder == now;
            var item = new MenuItem
            {
                Header = header,
                IsEnabled = !here,
                Icon = here ? new TextBlock { Text = "✓" } : null,
                Command = new RelayCommand(() => to(folder)),
            };
            move.Items.Add(item);
        }
        Add("The top", null);
        var paths = Vm.Library.PlaylistFolders.Where(allowed)
            .Select(f => (Folder: f, Path: Vm.FolderPathText(f.Id)))
            .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var (folder, path) in paths)
            Add(path, folder.Id);
        move.IsVisible = move.Items.Count > 1 || now is not null;
    }

    // ───────────── Dragging a playlist or folder onto a folder ─────────────

    /// <summary>How far (px) a mouse or pen moves a pressed row before it's a drag, not a click.</summary>
    private const double RowDragStart = 8;

    private object? _rowDragging;
    private Point _rowPressedAt;
    private bool _rowDragStarted;

    private void InitializePlaylistFolderDrags()
    {
        PlaylistList.AddHandler(PointerPressedEvent, OnPlaylistRowPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        PlaylistList.AddHandler(PointerMovedEvent, OnPlaylistRowMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        PlaylistList.AddHandler(PointerReleasedEvent, OnPlaylistRowReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        PlaylistList.AddHandler(PointerCaptureLostEvent, (_, _) => EndRowDrag(), RoutingStrategies.Bubble);
    }

    /// <summary>A mouse or pen pressed on a row may drag it; a finger scrolls the list (Move to is for touch).</summary>
    private void OnPlaylistRowPressed(object? sender, PointerPressedEventArgs e)
    {
        EndRowDrag();
        if (e.Pointer.Type == PointerType.Touch || !e.GetCurrentPoint(PlaylistList).Properties.IsLeftButtonPressed)
            return;
        Point at = e.GetPosition(this);
        if (PlaylistItemAt(at)?.DataContext is { } row && row is PlaylistInfo or PlaylistFolderRow
            && (e.Source as Visual)?.FindAncestorOfType<ToggleButton>(includeSelf: true) is null)
        {
            _rowDragging = row;
            _rowPressedAt = at;
        }
    }

    private void OnPlaylistRowMoved(object? sender, PointerEventArgs e)
    {
        if (_rowDragging is null)
            return;
        Point at = e.GetPosition(this);
        if (!_rowDragStarted)
        {
            if (Math.Abs(at.X - _rowPressedAt.X) + Math.Abs(at.Y - _rowPressedAt.Y) < RowDragStart)
                return;
            _rowDragStarted = true;
            e.Pointer.Capture(PlaylistList);
        }
        ListBoxItem? over = RowDropTarget(at) is { } target ? ContainerOf(target) : null;
        foreach (ListBoxItem item in PlaylistList.GetVisualDescendants().OfType<ListBoxItem>())
            item.Classes.Set("drop-target", item == over);
        e.Handled = true;
    }

    private void OnPlaylistRowReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_rowDragging is not { } dragged || !_rowDragStarted)
        {
            EndRowDrag();
            return;
        }
        Point at = e.GetPosition(this);
        // Anywhere on the panel that isn't a row (its header, the space under the rows) is the top.
        bool overPanel = IsOver(PlaylistsSection, at);
        long? into = RowDropTarget(at) is { } target ? target.Id : null;
        EndRowDrag();
        e.Pointer.Capture(null);
        e.Handled = true;
        if (!overPanel)
            return;   // let go outside the panel: nothing moves
        if (dragged is PlaylistInfo playlist)
            Vm.MovePlaylist(playlist, into);
        else if (dragged is PlaylistFolderRow folder && into != folder.Id)
            Vm.MovePlaylistFolder(folder, into);
    }

    /// <summary>
    /// The folder a row let go at <paramref name="at"/> goes into: the folder there, or the folder
    /// of the playlist there; null for the top (the empty space, or a playlist at the top).
    /// </summary>
    private PlaylistFolderRow? RowDropTarget(Point at) => PlaylistItemAt(at)?.DataContext switch
    {
        PlaylistFolderRow folder => folder,
        PlaylistInfo { FolderId: { } f } => Vm.Library.PlaylistRows.OfType<PlaylistFolderRow>().FirstOrDefault(r => r.Id == f),
        _ => null,
    };

    private ListBoxItem? ContainerOf(PlaylistFolderRow folder) => PlaylistList.ContainerFromItem(folder) as ListBoxItem;

    private void EndRowDrag()
    {
        if (_rowDragStarted)
        {
            foreach (ListBoxItem item in PlaylistList.GetVisualDescendants().OfType<ListBoxItem>())
                item.Classes.Set("drop-target", false);
        }
        _rowDragging = null;
        _rowDragStarted = false;
    }

    /// <summary>A playlist's Move to (#354), filled with its other menu items.</summary>
    private void FillPlaylistMoveTo(ContextMenu menu, PlaylistInfo playlist)
    {
        if (menu.Items.OfType<MenuItem>().FirstOrDefault(i => i.Tag is "move") is { } move)
            FillMoveTo(move, playlist.FolderId, _ => true, to => Vm.MovePlaylist(playlist, to));
    }
}
