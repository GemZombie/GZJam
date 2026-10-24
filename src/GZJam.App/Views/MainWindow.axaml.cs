using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Views;

public partial class MainWindow : Window
{
    private readonly DragStarter _gridDrag;
    private readonly DragStarter _queueDrag;

    private PixelPoint _normalPosition;
    private Size _normalSize;

    public MainWindow()
    {
        InitializeComponent();
        RevealMenuItem.Header = FileManager.RevealLabel;

        TrackGrid.AddHandler(PointerPressedEvent, OnGridPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        TrackGrid.AddHandler(KeyDownEvent, OnGridKeyDown, RoutingStrategies.Tunnel);
        _gridDrag = new DragStarter(this, TrackGrid, item => DragFromLibrary((Track)item), TrackMenu);

        QueueList.AddHandler(PointerPressedEvent, OnQueuePointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        QueueList.AddHandler(KeyDownEvent, OnQueueKeyDown, RoutingStrategies.Tunnel);
        _queueDrag = new DragStarter(this, QueueList, item => DragFromQueue((QueueItem)item), QueueMenu);

        SearchBox.AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel);

        PositionChanged += (_, _) => RememberNormalBounds();
        Resized += (_, _) => RememberNormalBounds();

        InitializeDrag();
        InitializeDecks();
        InitializeHistory();
        InitializeSearch();
        InitializeColumns();
        UseTopBarAsTitleBar();
    }

    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    private bool _closeConfirmed;
    private bool _askingToClose;

    /// <summary>
    /// While the room is hearing a deck, closing (the close button, Alt+F4, Cmd+Q) asks first, so
    /// a stray click or tap can't stop the music. Not when the computer is shutting down: GZJam
    /// mustn't hold that up.
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_closeConfirmed && e.CloseReason != WindowCloseReason.OSShutdown
            && DataContext is MainWindowViewModel vm && vm.WhatTheRoomIsHearing() is { } playing)
        {
            e.Cancel = true;
            if (!_askingToClose)
                _ = AskBeforeClosingAsync(vm, playing);
            return;
        }
        base.OnClosing(e);
    }

    private async Task AskBeforeClosingAsync(MainWindowViewModel vm, string playing)
    {
        _askingToClose = true;
        try
        {
            bool close = vm.ConfirmCloseAsync is not { } ask || await ask(playing);
            Log.Info($"Asked before closing ({playing}): {(close ? "closed" : "kept playing")}");
            if (close)
                CloseWithoutAsking();
        }
        finally
        {
            _askingToClose = false;
        }
    }

    /// <summary>Closes even while music is playing (after the DJ has said so, and in tests).</summary>
    public void CloseWithoutAsking()
    {
        _closeConfirmed = true;
        Close();
    }

    private static KeyModifiers CommandModifier => OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    // ───────────── Track list ─────────────

    private List<Track> SelectedTracks() => TrackGrid.SelectedItems.OfType<Track>().ToList();

    private void OnGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<DataGridRow>(includeSelf: true) is not { DataContext: Track track })
            return;

        var point = e.GetCurrentPoint(TrackGrid);
        if (point.Properties.IsRightButtonPressed)
        {
            // Right-clicking outside the selection acts on the clicked row, like a file manager.
            if (!TrackGrid.SelectedItems.Contains(track))
                TrackGrid.SelectedItem = track;
        }
        else if (point.Properties.IsLeftButtonPressed)
        {
            _gridDrag.Pressed(e, track);
        }
    }

    /// <summary>Dragging a library row drags the selection if the row is in it, else that row alone.</summary>
    private TrackDrag DragFromLibrary(Track pressed)
    {
        var tracks = SelectedTracks();
        if (!tracks.Contains(pressed))
        {
            TrackGrid.SelectedItem = pressed;
            tracks = [pressed];
        }
        return new TrackDrag(tracks, null);
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                Vm.Queue.PlayNext(SelectedTracks());
                break;
            case Key.Enter:
                Vm.Queue.Add(SelectedTracks());
                break;
            case Key.Space:
                PreviewSelected();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void OnAddToQueueClick(object? sender, RoutedEventArgs e) => Vm.Queue.Add(SelectedTracks());

    private void OnPlayNextClick(object? sender, RoutedEventArgs e) => Vm.Queue.PlayNext(SelectedTracks());

    private void OnShowInTreeClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedTracks().FirstOrDefault() is not { } track)
            return;
        Vm.Library.RevealTrack(track);
        // Scroll once the expanded folders have been laid out.
        Dispatcher.UIThread.Post(() =>
        {
            if (Vm.Library.SelectedFolder is { } folder && FolderTree.TreeContainerFromItem(folder) is Control container)
                container.BringIntoView();
        }, DispatcherPriority.Background);
    }

    private void OnRevealFileClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedTracks().FirstOrDefault() is { } track)
            FileManager.Reveal(track.Path);
    }

    // ───────────── Search box ─────────────

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Vm.Library.SearchText = "";
                e.Handled = true;
                break;
            case Key.Down when Vm.Library.Results.Count > 0:
                // Arrow down from the search box into the results, keyboard-only.
                TrackGrid.SelectedIndex = 0;
                TrackGrid.Focus();
                e.Handled = true;
                break;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Ctrl+F on Windows and Linux, Cmd+F on macOS.
        if (e.Key == Key.Escape && IsDragging)
        {
            EndDrag();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && IsSettingsOpen)
        {
            CloseSettings();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(CommandModifier))
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    // ───────────── Queue ─────────────

    private List<QueueItem> SelectedQueueItems() => QueueList.SelectedItems?.OfType<QueueItem>().ToList() ?? [];

    private void OnQueuePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not { DataContext: QueueItem item })
            return;

        var point = e.GetCurrentPoint(QueueList);
        if (point.Properties.IsRightButtonPressed)
        {
            if (QueueList.SelectedItems?.Contains(item) != true)
                QueueList.SelectedItem = item;
        }
        else if (point.Properties.IsLeftButtonPressed)
        {
            bool onHandle = (e.Source as Control)?.FindAncestorOfType<Border>(includeSelf: true) is { } border && border.Classes.Contains("grip");
            _queueDrag.Pressed(e, item, onHandle);
        }
    }

    /// <summary>Dragging a queue row drags the selection if the row is in it, else that row alone.</summary>
    private TrackDrag DragFromQueue(QueueItem pressed)
    {
        var items = SelectedQueueItems();
        if (!items.Contains(pressed))
        {
            QueueList.SelectedItems?.Clear();
            QueueList.SelectedItem = pressed;
            items = [pressed];
        }
        return new TrackDrag(items.Select(i => i.Track).ToList(), items);
    }

    private void OnQueueKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Delete or Key.Back)
        {
            Vm.Queue.Remove(SelectedQueueItems());
            e.Handled = true;
        }
    }

    /// <summary>Where a drop at <paramref name="position"/> would insert, and where to draw the insertion line.</summary>
    private int DropIndex(Point position, out double lineY)
    {
        int count = Vm.Queue.Items.Count;
        lineY = 8;
        for (int i = 0; i < count; i++)
        {
            if (QueueList.ContainerFromIndex(i) is not Control container || container.TranslatePoint(default, QueueArea) is not { } top)
                continue;
            if (position.Y < top.Y + container.Bounds.Height / 2)
            {
                lineY = top.Y - 3;
                return i;
            }
            lineY = top.Y + container.Bounds.Height + 1;
        }
        return count;
    }

    private void OnQueueMoveToTopClick(object? sender, RoutedEventArgs e) => Vm.Queue.Move(SelectedQueueItems(), 0);

    private void OnQueueRemoveClick(object? sender, RoutedEventArgs e) => Vm.Queue.Remove(SelectedQueueItems());

    private void OnQueueClearClick(object? sender, RoutedEventArgs e) => Vm.Queue.Clear();

    // ───────────── Library folders ─────────────

    private async void OnAddFolderClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Add a music folder",
            AllowMultiple = false,
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            await Vm.Library.AddRootAsync(path);
    }

    // ───────────── Window placement ─────────────

    public void RestorePlacement(AppSettings.WindowPlacement? placement) => RestorePlacement(this, placement);

    /// <summary>
    /// Puts <paramref name="window"/> where the main window was last time: the main window itself,
    /// and the startup screen, which opens in the same place so the one becomes the other.
    /// </summary>
    internal static void RestorePlacement(Window window, AppSettings.WindowPlacement? placement)
    {
        if (placement is null || placement.Width < window.MinWidth || placement.Height < window.MinHeight)
            return;

        window.Width = placement.Width;
        window.Height = placement.Height;
        // Only restore the position if it's still on a connected screen (e.g. after undocking).
        var bounds = new PixelRect(placement.X, placement.Y, (int)placement.Width, (int)placement.Height);
        if (window.Screens.All.Any(s => s.WorkingArea.Intersects(bounds)))
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = new PixelPoint(placement.X, placement.Y);
        }
        if (placement.Maximized)
            window.WindowState = WindowState.Maximized;
    }

    public AppSettings.WindowPlacement CapturePlacement()
    {
        RememberNormalBounds();
        return new AppSettings.WindowPlacement
        {
            X = _normalPosition.X,
            Y = _normalPosition.Y,
            Width = _normalSize.Width,
            Height = _normalSize.Height,
            Maximized = WindowState == WindowState.Maximized,
        };
    }

    /// <summary>Tracks the un-maximized size and position, so restoring a maximized window un-maximizes sensibly.</summary>
    private void RememberNormalBounds()
    {
        if (WindowState != WindowState.Normal)
            return;
        _normalPosition = Position;
        _normalSize = new Size(Width, Height);
    }
}
