using Avalonia.Input;

namespace GZJam.App.Controls;

/// <summary>Which point a marker on the waveform stands for.</summary>
public enum MarkerKind { Start, End, JumpFrom, JumpTo, Cue }

/// <summary>
/// A marker dragged to a new place: which one (a jump's index, a cue's number; 0 for the start
/// and end) and where to, as a fraction of the file.
/// </summary>
public readonly record struct MarkerMove(MarkerKind Kind, int Index, double To);

/// <summary>
/// Grabbing a marker on the whole song or the close-up and dragging it: shared by both. A press
/// near a marker grabs it (a finger reaches further than the mouse, so a slightly missed marker
/// isn't missed); once moved a few pixels, the marker follows the pointer and is drawn there; let
/// go, and it's asked to move there. Allowed on air: moving a point changes nothing the room hears
/// until the music gets there.
/// </summary>
internal sealed class MarkerGrab
{
    private const double MouseReach = 5, FingerReach = 14, DragDistance = 4;

    private (MarkerKind Kind, int Index, double At)? _grabbed;
    private double _pressedX;

    /// <summary>A marker is held (pressed, maybe not moved yet).</summary>
    public bool IsHolding => _grabbed is not null;

    /// <summary>The held marker has been dragged: it's drawn where the pointer is.</summary>
    public bool IsDragging { get; private set; }

    /// <summary>How far from a marker a press still grabs it.</summary>
    public static double Reach(IPointer pointer) => pointer.Type == PointerType.Mouse ? MouseReach : FingerReach;

    /// <summary>The markers the DJ can drag, where they are (fractions of the file).</summary>
    public static IEnumerable<(MarkerKind Kind, int Index, double At)> Draggable(
        double trimStart, double trimEnd, bool startShown, bool endShown,
        IReadOnlyList<JumpMarker>? jumps, IReadOnlyList<CueMarker>? cues)
    {
        foreach (CueMarker cue in cues ?? [])
            yield return (MarkerKind.Cue, cue.Number, cue.At);
        for (int i = 0; i < (jumps?.Count ?? 0); i++)
        {
            yield return (MarkerKind.JumpFrom, i, jumps![i].From);
            yield return (MarkerKind.JumpTo, i, jumps[i].To);
        }
        if (endShown)
            yield return (MarkerKind.End, 0, trimEnd);
        if (startShown)
            yield return (MarkerKind.Start, 0, trimStart);
    }

    /// <summary>The marker nearest <paramref name="x"/> within reach, if any.</summary>
    public static (MarkerKind Kind, int Index, double At)? Find(
        IEnumerable<(MarkerKind Kind, int Index, double At)> markers, Func<double, double> toX, double x, double reach)
    {
        (MarkerKind, int, double)? nearest = null;
        double best = reach;
        foreach (var marker in markers)
        {
            double distance = Math.Abs(toX(marker.At) - x);
            if (distance <= best)
                (nearest, best) = (marker, distance);
        }
        return nearest;
    }

    /// <summary>Holds a marker found at a press.</summary>
    public void Hold((MarkerKind Kind, int Index, double At) marker, double x)
    {
        _grabbed = marker;
        _pressedX = x;
        IsDragging = false;
    }

    /// <summary>The pointer moved to <paramref name="x"/> (where the song is at <paramref name="fraction"/>): true if the marker moved.</summary>
    public bool Drag(double x, double fraction)
    {
        if (_grabbed is not { } held || (!IsDragging && Math.Abs(x - _pressedX) < DragDistance))
            return false;
        IsDragging = true;
        _grabbed = held with { At = fraction };
        return true;
    }

    /// <summary>Lets go: where the marker was dragged to (null if it was only pressed).</summary>
    public MarkerMove? Release()
    {
        MarkerMove? move = IsDragging && _grabbed is { } held ? new MarkerMove(held.Kind, held.Index, held.At) : null;
        Cancel();
        return move;
    }

    public void Cancel()
    {
        _grabbed = null;
        IsDragging = false;
    }

    // While dragging, the marker is drawn where it's being dragged to, not where it was.

    public double TrimStart(double value) => Dragged(MarkerKind.Start) ?? value;

    public double TrimEnd(double value) => Dragged(MarkerKind.End) ?? value;

    public bool StartManual(bool value) => value || Dragged(MarkerKind.Start) is not null;

    public bool EndManual(bool value) => value || Dragged(MarkerKind.End) is not null;

    public IReadOnlyList<JumpMarker>? Jumps(IReadOnlyList<JumpMarker>? jumps) =>
        IsDragging && _grabbed is { Kind: MarkerKind.JumpFrom or MarkerKind.JumpTo } held && jumps is not null
            ? jumps.Select((j, i) => i != held.Index ? j
                : held.Kind == MarkerKind.JumpFrom ? j with { From = held.At } : j with { To = held.At }).ToList()
            : jumps;

    public IReadOnlyList<CueMarker>? Cues(IReadOnlyList<CueMarker>? cues) =>
        IsDragging && _grabbed is { Kind: MarkerKind.Cue } held && cues is not null
            ? cues.Select(c => c.Number == held.Index ? c with { At = held.At } : c).ToList()
            : cues;

    private double? Dragged(MarkerKind kind) => IsDragging && _grabbed is { } held && held.Kind == kind ? held.At : null;

    private static Cursor? _cursor;

    /// <summary>The mouse pointer over a marker: arrows saying it can be dragged sideways.</summary>
    public static Cursor DragCursor => _cursor ??= new Cursor(StandardCursorType.SizeWestEast);
}
