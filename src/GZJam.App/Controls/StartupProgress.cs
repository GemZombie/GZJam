namespace GZJam.App.Controls;

/// <summary>
/// How far the startup screen's sweep has got, 0 to 1. Startup reports each step as it begins,
/// with the share of the sweep it covers. How long a step will take isn't known, so within one the
/// sweep creeps on toward the step's end, slowing and never quite reaching it: a slow step shows
/// as a slow sweep, not a frozen one. The value shown follows smoothly and never goes back.
/// </summary>
internal sealed class StartupProgress
{
    /// <summary>Within a step, the sweep creeps two thirds of the way it may go in this time.</summary>
    private const double CreepSeconds = 0.6;

    /// <summary>How much of a step the sweep may creep through before the step is done.</summary>
    private const double CreepShare = 0.85;

    /// <summary>The value shown closes two thirds of its gap to where the sweep should be in this time.</summary>
    private const double FollowSeconds = 0.08;

    /// <summary>Once ready, faster: the sweep reaches the end in the startup screen's last fifth of a second.</summary>
    private const double FinishFollowSeconds = 0.035;

    private double _from, _to;
    private TimeSpan _stepStart;
    private bool _finished;
    private TimeSpan? _last;

    /// <summary>The value shown, as of the last <see cref="Update"/>.</summary>
    public double Shown { get; private set; }

    /// <summary>Everything is ready and the sweep has reached the end.</summary>
    public bool IsComplete => _finished && Shown >= 1;

    /// <summary>A step begins at <paramref name="now"/>, covering <paramref name="from"/> to <paramref name="to"/> of the sweep.</summary>
    public void BeginStep(double from, double to, TimeSpan now)
    {
        _from = from;
        _to = to;
        _stepStart = now;
    }

    /// <summary>Everything is ready: the sweep goes to the end.</summary>
    public void Finish() => _finished = true;

    /// <summary>Moves the value shown on to <paramref name="now"/>, and returns it.</summary>
    public double Update(TimeSpan now)
    {
        double target = _finished
            ? 1
            : _from + (_to - _from) * CreepShare * (1 - Math.Exp(-(now - _stepStart).TotalSeconds / CreepSeconds));
        double elapsed = _last is { } last ? Math.Max(0, (now - last).TotalSeconds) : 0;
        _last = now;

        double follow = _finished ? FinishFollowSeconds : FollowSeconds;
        Shown = Math.Max(Shown, Shown + (target - Shown) * (1 - Math.Exp(-elapsed / follow)));
        if (_finished && Shown > 0.998)
            Shown = 1;
        return Shown;
    }
}
