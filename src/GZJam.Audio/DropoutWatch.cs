namespace GZJam.Audio;

/// <summary>
/// Notices the speakers' sound stopping for a moment (#345), whichever way they're set up: shared
/// or GZJam's alone. Each tick it compares how far the speakers' mix has played with how much
/// time has passed. Normally the two stay within a millisecond of each other. When the sound
/// stops because the buffer ran dry, the play position stands still while the clock goes on, and
/// the difference grows by the length of the gap and stays there. Measured on Windows: a 400 ms
/// stall of the audio thread against a 200 ms buffer left the position 222 ms behind.
/// It works however late the ticks come: if the whole app was held up, the gap is still counted
/// on the next tick, so a dropout can't slip by unseen as the old exclusive-only check could.
/// </summary>
internal sealed class DropoutWatch
{
    /// <summary>A step this big is a dropout: well over the play position's jitter (under 1 ms shared).</summary>
    internal static readonly TimeSpan Threshold = TimeSpan.FromMilliseconds(30);

    /// <summary>How far back the steady difference is taken from, so slow drift never adds up to a dropout.</summary>
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(2);

    /// <summary>
    /// A dropout is reported once its gap has stopped growing for this long: the whole of it at once,
    /// and not before a card being unplugged (which stops the sound too) has had time to be noticed.
    /// </summary>
    internal static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(500);

    /// <summary>Longer than this between ticks (the computer slept, say): start again rather than guess.</summary>
    private static readonly TimeSpan TooLong = TimeSpan.FromSeconds(10);

    private readonly Queue<(TimeSpan At, double Lag)> _recent = new();
    private TimeSpan? _lastReal;
    private double _lastPlayed;
    private double _lag;   // seconds the clock has gone on beyond what was played
    private double? _gapFrom;
    private double _gapPeak;
    private TimeSpan _lastRise;

    /// <summary>
    /// How long the sound has been gone so far, while it still is (0 when it's playing): a gap
    /// that keeps growing is reported by <see cref="Observe"/> only once it's over, and one that
    /// never ends (the speakers' card gone, #353) would never be.
    /// </summary>
    public TimeSpan Silent => _gapFrom is { } from ? TimeSpan.FromSeconds(Math.Max(0, _lag - from)) : TimeSpan.Zero;

    /// <summary>Forgets what it saw: new outputs, say.</summary>
    public void Reset()
    {
        _recent.Clear();
        _lastReal = null;
        _lag = 0;
        _gapFrom = null;
    }

    /// <summary>
    /// The clock (<paramref name="real"/>) and how much the speakers have played, in seconds
    /// (negative if unknown). Returns a dropout's length once it's over, else null.
    /// </summary>
    public TimeSpan? Observe(TimeSpan real, double played)
    {
        if (_lastReal is not { } last || played < 0 || played < _lastPlayed || real - last > TooLong)
        {
            Reset();
            if (played >= 0)
            {
                _lastReal = real;
                _lastPlayed = played;
            }
            return null;
        }
        _lag += (real - last).TotalSeconds - (played - _lastPlayed);
        _lastReal = real;
        _lastPlayed = played;

        if (_gapFrom is { } from)
        {
            if (_lag > _gapPeak + 0.002)
            {
                _gapPeak = _lag;
                _lastRise = real;
            }
            if (real - _lastRise < Settle)
                return null;
            var gap = TimeSpan.FromSeconds(_gapPeak - from);
            _gapFrom = null;
            _recent.Clear();
            _recent.Enqueue((real, _lag));
            return gap;
        }

        while (_recent.Count > 0 && real - _recent.Peek().At > Window)
            _recent.Dequeue();
        double baseline = _lag;
        foreach (var (_, lag) in _recent)
            baseline = Math.Min(baseline, lag);
        _recent.Enqueue((real, _lag));
        if (_lag - baseline >= Threshold.TotalSeconds)
        {
            _gapFrom = baseline;
            _gapPeak = _lag;
            _lastRise = real;
        }
        return null;
    }
}
