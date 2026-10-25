using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Views;

/// <summary>
/// Dragging tracks onto a deck or into the queue. GZJam moves the drag itself rather than handing it
/// to the operating system's drag and drop, which on Windows follows the mouse only: a finger's drag
/// went nowhere. So a drag works the same with touch, pen, trackpad and mouse: a tag with the
/// song follows the pointer, the deck under it lights up (or shows it won't take the song), and
/// the queue shows where it would go, scrolling when held near its top or bottom. Held over the
/// Queue tab while History is showing, it opens the queue.
/// </summary>
public partial class MainWindow
{
    /// <summary>What's being dragged: tracks, plus the queue items themselves when reordering the queue.</summary>
    internal sealed record TrackDrag(IReadOnlyList<Track> Tracks, IReadOnlyList<QueueItem>? FromQueue);

    /// <summary>How close to the queue's top or bottom edge (px) a drag scrolls it, and how far per step.</summary>
    private const double EdgeZone = 36;
    private const double EdgeStep = 14;

    private TrackDrag? _drag;
    private IPointer? _dragPointer;
    private PointerType _dragPointerType;
    private Border? _dragTag;
    private int _queueScrollDirection;
    private readonly DispatcherTimer _queueScroll = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private Point _dragAt;
    private Point _dragStart;
    // A song picked up by a held finger: let go without moving, it opens this menu instead.
    private (ContextMenu Menu, Control Target)? _dragMenu;

    private void InitializeDrag()
    {
        AddHandler(PointerMovedEvent, OnDragPointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnDragPointerReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, (_, e) =>
        {
            if (_drag is not null && e.Pointer == _dragPointer)
                EndDrag();
        });
        _queueScroll.Tick += (_, _) => ScrollQueueStep();
    }

    internal bool IsDragging => _drag is not null;

    /// <summary>
    /// Starts dragging <paramref name="drag"/> from <paramref name="at"/> (window coordinates).
    /// The pointer is captured so nothing under it reacts until it's let go. With
    /// <paramref name="menuIfStill"/>, letting go without moving opens that menu instead (a held
    /// finger: long-press still means right-click).
    /// </summary>
    internal void StartTrackDrag(TrackDrag drag, IPointer? pointer, Point at, (ContextMenu Menu, Control Target)? menuIfStill = null)
    {
        if (drag.Tracks.Count == 0)
            return;
        EndDrag();
        _drag = drag;
        _dragPointer = pointer;
        _dragPointerType = pointer?.Type ?? PointerType.Mouse;
        _dragStart = at;
        _dragMenu = menuIfStill;
        pointer?.Capture(this);
        _dragTag = MakeDragTag(drag);
        DragLayer.Children.Add(_dragTag);
        UpdateDrag(at);
    }

    private static Border MakeDragTag(TrackDrag drag)
    {
        Track first = drag.Tracks[0];
        var text = new StackPanel { Spacing = 1 };
        text.Children.Add(new TextBlock
        {
            Text = drag.Tracks.Count == 1 ? first.DisplayTitle : $"{drag.Tracks.Count} songs",
            FontWeight = FontWeight.SemiBold,
            FontSize = 13,
            MaxWidth = 280,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        string detail = drag.Tracks.Count == 1 ? first.Artist ?? "" : $"{first.DisplayTitle} and more";
        if (detail.Length > 0)
            text.Children.Add(new TextBlock { Text = detail, Classes = { "muted" }, FontSize = 11.5, MaxWidth = 280, TextTrimming = TextTrimming.CharacterEllipsis });
        return new Border { Classes = { "drag-tag" }, Child = text, IsHitTestVisible = false };
    }

    private void OnDragPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_drag is null)
            return;
        UpdateDrag(e.GetPosition(this));
        e.Handled = true;
    }

    private async void OnDragPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_drag is not { } drag)
            return;
        e.Handled = true;
        Point at = e.GetPosition(this);
        object? target = DropTargetAt(at);
        Point queuePoint = this.TranslatePoint(at, QueueArea) ?? default;
        var menu = _dragMenu;
        bool still = ((Vector)(at - _dragStart)).Length <= DragStarter.HoldTolerance;
        PointerType pointerType = _dragPointerType;
        EndDrag();
        if (menu is { } m && still)
        {
            m.Menu.Open(m.Target);
            return;
        }
        // One line per drag, to see what a touchscreen did if a drag goes astray.
        string what = drag.Tracks.Count == 1 ? $"\"{drag.Tracks[0].DisplayTitle}\"" : $"{drag.Tracks.Count} songs";
        if (target is null && DropOnPlaylist(drag, at, out string playlistDrop))
        {
            Log.Info($"Dragged {what} by {pointerType.ToString().ToLowerInvariant()} to {playlistDrop}");
            return;
        }
        string where = target switch
        {
            DeckView { DataContext: DeckViewModel d } => $"Deck {d.Label}",
            Panel when target == QueueArea => "the queue",
            _ => "nowhere (dropped outside the decks and queue)",
        };
        Log.Info($"Dragged {what} by {pointerType.ToString().ToLowerInvariant()} to {where}");

        switch (target)
        {
            case DeckView { DataContext: DeckViewModel deck } when drag.Tracks is [var track]:
                if (await Vm.LoadToDeckAsync(deck, track) && drag.FromQueue is { } fromQueue)
                    Vm.Queue.Remove(fromQueue);
                break;
            case Panel when target == QueueArea:
                int index = DropIndex(queuePoint, out _);
                if (drag.FromQueue is { } items)
                    Vm.Queue.Move(items, index);
                else
                    Vm.Queue.Add(drag.Tracks, index);
                break;
        }
    }

    /// <summary>Cancels a drag (Escape, or the pointer taken away).</summary>
    internal void EndDrag()
    {
        if (_drag is null)
            return;
        _drag = null;
        _dragMenu = null;
        IPointer? pointer = _dragPointer;
        _dragPointer = null;
        if (pointer?.Captured == this)
            pointer.Capture(null);
        if (_dragTag is not null)
            DragLayer.Children.Remove(_dragTag);
        _dragTag = null;
        HideDropTarget();
        EndPlaylistDrop();
    }

    /// <summary>Lights nothing: no deck outlined, no line in the queue.</summary>
    private void HideDropTarget()
    {
        foreach (DeckView view in DeckViews)
            view.SetDropState(null);
        QueueDropLine.IsVisible = false;
        SetQueueScroll(0);
    }

    private void UpdateDrag(Point at)
    {
        if (_drag is not { } drag || _dragTag is not { } tag)
            return;
        _dragAt = at;

        // The tag sits above a finger (so the finger doesn't hide it), beside a mouse pointer.
        tag.Measure(Size.Infinity);
        Size size = tag.DesiredSize;
        bool finger = _dragPointerType != PointerType.Mouse;
        double left = finger ? at.X - size.Width / 2 : at.X + 16;
        double top = finger ? at.Y - size.Height - 34 : at.Y + 14;
        Canvas.SetLeft(tag, Math.Clamp(left, 0, Math.Max(0, Bounds.Width - size.Width)));
        Canvas.SetTop(tag, Math.Clamp(top, 0, Math.Max(0, Bounds.Height - size.Height)));

        ShowDropTarget(at, drag.Tracks.Count, out bool blocked);
        tag.Classes.Set("blocked", blocked);
        UpdatePlaylistDrop(at);
    }

    /// <summary>
    /// Lights what <paramref name="songs"/> would land on at <paramref name="at"/>: a deck (or shows
    /// it won't take them), or the line in the queue where they'd go. Returns that target, if any.
    /// </summary>
    private object? ShowDropTarget(Point at, int songs, out bool blocked)
    {
        object? target = DropTargetAt(at);
        blocked = false;
        foreach (DeckView view in DeckViews)
        {
            if (view != target)
            {
                view.SetDropState(null);
                continue;
            }
            // A deck takes exactly one track, and not while protection refuses it.
            bool allowed = songs == 1 && view.DataContext is DeckViewModel deck && Vm.CanLoad(deck);
            view.SetDropState(allowed);
            blocked = !allowed;
        }

        if (Vm.ShowHistory && IsOver(QueueTab, at))
            Vm.ShowHistory = false;

        if (target == QueueArea && this.TranslatePoint(at, QueueArea) is { } queuePoint)
        {
            DropIndex(queuePoint, out double lineY);
            QueueDropLine.Margin = new Thickness(10, lineY, 10, 0);
            QueueDropLine.IsVisible = true;
            SetQueueScroll(queuePoint.Y < EdgeZone ? -1 : queuePoint.Y > QueueArea.Bounds.Height - EdgeZone ? 1 : 0);
        }
        else
        {
            QueueDropLine.IsVisible = false;
            SetQueueScroll(0);
        }
        return target;
    }

    /// <summary>The deck view or the queue under <paramref name="at"/> (window coordinates), if any.</summary>
    private object? DropTargetAt(Point at)
    {
        foreach (DeckView view in DeckViews)
            if (IsOver(view, at))
                return view;
        return IsOver(QueueArea, at) ? QueueArea : null;
    }

    private bool IsOver(Control control, Point at) =>
        control.IsEffectivelyVisible && this.TranslatePoint(at, control) is { } p && new Rect(control.Bounds.Size).Contains(p);

    private void SetQueueScroll(int direction)
    {
        _queueScrollDirection = direction;
        if (direction == 0)
            _queueScroll.Stop();
        else if (!_queueScroll.IsEnabled)
            _queueScroll.Start();
    }

    private void ScrollQueueStep()
    {
        if (_queueScrollDirection == 0 || QueueList.FindDescendantOfType<ScrollViewer>() is not { } scroller)
            return;
        double y = Math.Clamp(scroller.Offset.Y + _queueScrollDirection * EdgeStep, 0, Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height));
        scroller.Offset = new Vector(scroller.Offset.X, y);
        UpdateDrag(_dragAt);   // the line follows the songs moving under a still finger
    }
}
