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
    /// <summary>What's being dragged: tracks, plus the queue items themselves when reordering the queue.</summary>
    internal sealed record TrackDrag(IReadOnlyList<Track> Tracks, IReadOnlyList<QueueItem>? FromQueue);

    internal static readonly DataFormat<TrackDrag> TrackFormat = DataFormat.CreateInProcessFormat<TrackDrag>("GZJam.Tracks");
    // A drag must start from the original press event, so keep it until the pointer moves far enough.
    private PointerPressedEventArgs? _gridPress;
    private Point _gridPressPoint;
    private PointerPressedEventArgs? _queuePress;
    private Point _queuePressPoint;
    private bool _queuePressOnHandle;

    private PixelPoint _normalPosition;
    private Size _normalSize;

    public MainWindow()
    {
        InitializeComponent();
        RevealMenuItem.Header = FileManager.RevealLabel;

        TrackGrid.AddHandler(PointerPressedEvent, OnGridPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        TrackGrid.AddHandler(PointerMovedEvent, OnGridPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        TrackGrid.AddHandler(PointerReleasedEvent, (_, _) => _gridPress = null, RoutingStrategies.Tunnel, handledEventsToo: true);
        TrackGrid.AddHandler(KeyDownEvent, OnGridKeyDown, RoutingStrategies.Tunnel);

        QueueList.AddHandler(PointerPressedEvent, OnQueuePointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        QueueList.AddHandler(PointerMovedEvent, OnQueuePointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        QueueList.AddHandler(PointerReleasedEvent, (_, _) => _queuePress = null, RoutingStrategies.Tunnel, handledEventsToo: true);
        QueueList.AddHandler(KeyDownEvent, OnQueueKeyDown, RoutingStrategies.Tunnel);

        QueueArea.AddHandler(DragDrop.DragOverEvent, OnQueueDragOver);
        QueueArea.AddHandler(DragDrop.DragLeaveEvent, (_, _) => QueueDropLine.IsVisible = false);
        QueueArea.AddHandler(DragDrop.DropEvent, OnQueueDrop);

        SearchBox.AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel);

        PositionChanged += (_, _) => RememberNormalBounds();
        Resized += (_, _) => RememberNormalBounds();

        InitializeDecks();
        InitializeHistory();
    }

    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

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
            _gridPress = e;
            _gridPressPoint = point.Position;
        }
    }

    private async void OnGridPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_gridPress is not { } press)
            return;
        var point = e.GetCurrentPoint(TrackGrid);
        if (!point.Properties.IsLeftButtonPressed)
        {
            _gridPress = null;
            return;
        }
        switch (DragIntent.Evaluate(e.Pointer.Type, point.Position - _gridPressPoint, fromHandle: false))
        {
            case DragIntent.Decision.Wait:
                return;
            case DragIntent.Decision.Cancel:
                _gridPress = null;   // a touch scroll, not a drag
                return;
        }

        _gridPress = null;
        var tracks = SelectedTracks();
        if (tracks.Count == 0)
            return;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(TrackFormat, new TrackDrag(tracks, null)));
        await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Copy);
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
            _queuePress = e;
            _queuePressPoint = point.Position;
            _queuePressOnHandle = (e.Source as Control)?.FindAncestorOfType<Border>(includeSelf: true) is { } border && border.Classes.Contains("grip");
        }
    }

    private async void OnQueuePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_queuePress is not { } press)
            return;
        var point = e.GetCurrentPoint(QueueList);
        if (!point.Properties.IsLeftButtonPressed)
        {
            _queuePress = null;
            return;
        }
        switch (DragIntent.Evaluate(e.Pointer.Type, point.Position - _queuePressPoint, _queuePressOnHandle))
        {
            case DragIntent.Decision.Wait:
                return;
            case DragIntent.Decision.Cancel:
                _queuePress = null;
                return;
        }

        _queuePress = null;
        var items = SelectedQueueItems();
        if (items.Count == 0)
            return;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(TrackFormat, new TrackDrag(items.Select(i => i.Track).ToList(), items)));
        await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Move);
    }

    private void OnQueueKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Delete or Key.Back)
        {
            Vm.Queue.Remove(SelectedQueueItems());
            e.Handled = true;
        }
    }

    private void OnQueueDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetValue(TrackFormat) is not { } drag)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        e.DragEffects = drag.FromQueue is null ? DragDropEffects.Copy : DragDropEffects.Move;
        DropIndex(e.GetPosition(QueueArea), out double lineY);
        QueueDropLine.Margin = new Thickness(10, lineY, 10, 0);
        QueueDropLine.IsVisible = true;
    }

    private void OnQueueDrop(object? sender, DragEventArgs e)
    {
        QueueDropLine.IsVisible = false;
        if (e.DataTransfer.TryGetValue(TrackFormat) is not { } drag)
            return;

        int index = DropIndex(e.GetPosition(QueueArea), out _);
        if (drag.FromQueue is { } items)
            Vm.Queue.Move(items, index);
        else
            Vm.Queue.Add(drag.Tracks, index);
        e.Handled = true;
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

    public void RestorePlacement(AppSettings.WindowPlacement? placement)
    {
        if (placement is null || placement.Width < MinWidth || placement.Height < MinHeight)
            return;

        Width = placement.Width;
        Height = placement.Height;
        // Only restore the position if it's still on a connected screen (e.g. after undocking).
        var bounds = new PixelRect(placement.X, placement.Y, (int)placement.Width, (int)placement.Height);
        if (Screens.All.Any(s => s.WorkingArea.Intersects(bounds)))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(placement.X, placement.Y);
        }
        if (placement.Maximized)
            WindowState = WindowState.Maximized;
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
