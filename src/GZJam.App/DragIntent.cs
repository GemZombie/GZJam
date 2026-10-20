using Avalonia;
using Avalonia.Input;

namespace GZJam.App;

/// <summary>
/// Decides when a press-and-move becomes a drag. With a mouse, any movement past a few pixels
/// drags. With touch (or pen), a vertical swipe must stay a scroll, so lists only drag on a
/// mostly sideways swipe (towards the queue and decks), or when started from a drag handle.
/// </summary>
public static class DragIntent
{
    public const double MouseThreshold = 6;
    public const double TouchThreshold = 14;

    public enum Decision { Wait, Drag, Cancel }

    public static Decision Evaluate(PointerType pointer, Vector delta, bool fromHandle)
    {
        double dx = Math.Abs(delta.X), dy = Math.Abs(delta.Y);

        if (pointer == PointerType.Mouse)
            return Math.Sqrt(dx * dx + dy * dy) >= MouseThreshold ? Decision.Drag : Decision.Wait;

        if (Math.Max(dx, dy) < TouchThreshold)
            return Decision.Wait;
        if (fromHandle)
            return Decision.Drag;
        // Mostly sideways: drag. Otherwise the finger is scrolling; stop considering a drag.
        return dx > dy * 1.5 ? Decision.Drag : Decision.Cancel;
    }
}
