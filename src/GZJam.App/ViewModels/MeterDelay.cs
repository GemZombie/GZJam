using System.Diagnostics;

namespace GZJam.App.ViewModels;

/// <summary>
/// A meter's readings held back until the room hears them (#417). A meter reads the music as it's
/// mixed, and the output plays it later, after what's in its buffers (up to half a second or more
/// with more protection against crackles): shown at once, the meters and the LIMIT light ran that
/// far ahead of the music. Each reading waits here for the output's latency; what's due comes out
/// together, the loudest of it, so no peak is lost. With no latency (no output), it passes
/// straight through.
/// </summary>
internal sealed class MeterDelay
{
    private readonly Queue<(long At, float Left, float Right, bool Flag)> _waiting = new();

    /// <summary>Takes this tick's reading; gives what's due now (zero and false if nothing is).</summary>
    public (float Left, float Right, bool Flag) Pass(float left, float right, bool flag, TimeSpan delay)
    {
        long now = Stopwatch.GetTimestamp();
        _waiting.Enqueue((now, left, right, flag));
        long due = now - (long)(delay.TotalSeconds * Stopwatch.Frequency);
        float l = 0, r = 0;
        bool f = false;
        while (_waiting.Count > 0 && _waiting.Peek().At <= due)
        {
            var reading = _waiting.Dequeue();
            l = Math.Max(l, reading.Left);
            r = Math.Max(r, reading.Right);
            f |= reading.Flag;
        }
        return (l, r, f);
    }
}
