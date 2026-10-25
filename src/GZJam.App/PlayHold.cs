namespace GZJam.App;

/// <summary>
/// The play button's press-and-hold (two-tap pausing: holding pauses a deck the room hears),
/// decided by the input's own times as well as the clock. If the screen freezes during a quick
/// tap, the hold's timer can come due before the release is handled; the release's time then
/// still says it was a tap, so the pause it caused is undone (#81).
/// </summary>
public sealed class PlayHold(TimeSpan hold)
{
    /// <summary>What a release meant.</summary>
    public enum Release
    {
        /// <summary>A tap: the click does what a tap does.</summary>
        Tap,

        /// <summary>The end of a hold that paused: the click is swallowed.</summary>
        Held,

        /// <summary>
        /// A tap that the timer took for a hold during a freeze: the pause is undone, then the
        /// click does what a tap does.
        /// </summary>
        TapAfterFreeze,
    }

    private ulong? _pressedAt;
    private bool _paused;

    /// <summary>Pressed, at the input's own time (milliseconds).</summary>
    public void Press(ulong timestamp)
    {
        _pressedAt = timestamp;
        _paused = false;
    }

    /// <summary>The hold's timer came due, and the hold paused the deck.</summary>
    public void Paused() => _paused = true;

    /// <summary>Released, at the input's own time: what the press was.</summary>
    public Release Released(ulong timestamp)
    {
        bool quick = _pressedAt is { } pressed && timestamp >= pressed && timestamp - pressed < (ulong)hold.TotalMilliseconds;
        bool paused = _paused;
        _pressedAt = null;
        _paused = false;
        return !paused ? Release.Tap : quick ? Release.TapAfterFreeze : Release.Held;
    }
}
