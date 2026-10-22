using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace GZJam.App.Views;

/// <summary>
/// Watches presses on one list (library, queue, history) and starts a track drag when a press
/// turns into one:
/// <list type="bullet">
/// <item>A mouse drags as soon as it moves a few pixels.</item>
/// <item>A finger or pen drags on a mostly sideways swipe (towards the queue and decks), or from a
/// drag handle; an up-and-down swipe scrolls the list.</item>
/// <item>A finger held still for a moment picks the song up: it then drags in any direction (up
/// to a deck, say). Lifted without moving, it opens the list's menu instead, as a right-click
/// would.</item>
/// </list>
/// The drag carries the row under the finger (with the rest of the selection if it's selected):
/// a finger's press doesn't always select its row before the list starts reacting to the swipe,
/// and dragging "the selection" then dragged nothing, or another song.
/// </summary>
internal sealed class DragStarter
{
    /// <summary>How long a finger stays still to pick a song up.</summary>
    public static readonly TimeSpan HoldTime = TimeSpan.FromMilliseconds(350);

    /// <summary>How far (px) a finger may wander and still count as held still.</summary>
    public const double HoldTolerance = 10;

    private readonly MainWindow _window;
    private readonly Control _list;
    private readonly Func<object, MainWindow.TrackDrag?> _dragFor;
    private readonly ContextMenu _menu;
    private CancellationTokenSource? _hold;

    private IPointer? _pointer;
    private object? _item;
    private Point _pressedAt;
    private bool _fromHandle;

    /// <param name="dragFor">What dragging the pressed item drags: the selection if the item is in it, else the item alone.</param>
    public DragStarter(MainWindow window, Control list, Func<object, MainWindow.TrackDrag?> dragFor, ContextMenu menu)
    {
        _window = window;
        _list = list;
        _dragFor = dragFor;
        _menu = menu;
        list.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        list.AddHandler(InputElement.PointerReleasedEvent, (_, _) => Reset(), RoutingStrategies.Tunnel, handledEventsToo: true);
        // A long press would open the menu while the finger is still down, ending the drag before
        // it starts; the menu opens when a held finger lifts instead (see the drag in MainWindow).
        list.AddHandler(Control.ContextRequestedEvent, (_, e) =>
        {
            if ((_pointer is { Type: not PointerType.Mouse }) || _window.IsDragging)
                e.Handled = true;
        }, RoutingStrategies.Tunnel);
    }

    /// <summary>A left press (or a finger) on <paramref name="item"/>: maybe the start of a drag.</summary>
    public void Pressed(PointerPressedEventArgs e, object item, bool fromHandle = false)
    {
        _pointer = e.Pointer;
        _item = item;
        _pressedAt = e.GetPosition(_list);
        _fromHandle = fromHandle;
        if (e.Pointer.Type != PointerType.Mouse)
        {
            var hold = _hold = new CancellationTokenSource();
            Task.Delay(HoldTime, hold.Token).ContinueWith(_ => Dispatcher.UIThread.Post(() =>
            {
                if (!hold.IsCancellationRequested)
                    OnHeld();
            }), CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
        }
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_pointer is null || e.Pointer != _pointer || _window.IsDragging)
            return;
        var point = e.GetCurrentPoint(_list);
        if (!point.Properties.IsLeftButtonPressed)
        {
            Reset();
            return;
        }
        Vector moved = point.Position - _pressedAt;
        if (moved.Length > HoldTolerance)
            CancelHold();
        switch (DragIntent.Evaluate(e.Pointer.Type, moved, _fromHandle))
        {
            case DragIntent.Decision.Wait:
                return;
            case DragIntent.Decision.Cancel:
                Reset();   // a scroll: the list's own touch scrolling takes over from here
                return;
        }
        Start(e.GetPosition(_window), held: false);
    }

    private void OnHeld()
    {
        _hold = null;
        if (_pointer is not null && _list.TranslatePoint(_pressedAt, _window) is { } at)
            Start(at, held: true);
    }

    private void Start(Point at, bool held)
    {
        IPointer? pointer = _pointer;
        object? item = _item;
        Reset();
        if (item is not null && _dragFor(item) is { Tracks.Count: > 0 } drag)
            _window.StartTrackDrag(drag, pointer, at, held ? (_menu, _list) : null);
    }

    private void CancelHold()
    {
        _hold?.Cancel();
        _hold = null;
    }

    private void Reset()
    {
        CancelHold();
        _pointer = null;
        _item = null;
    }
}
