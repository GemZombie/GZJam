using System.Diagnostics;

namespace GZJam.App.Controls;

/// <summary>
/// Where the close-up's playhead is between the deck's reports: worked out from the clock and the
/// tempo, and eased toward where the deck says it is (which arrives about 30 times a second, in
/// uneven 20 and 40 ms steps), so the music glides past instead of stuttering.
/// <para>
/// Shared by the window's thread, which tells it the reports and asks it where a click lands,
/// and the render thread, which draws the close-up at every frame from it (#364): one clock, so
/// what's drawn and what's clicked agree. Locked; each call is a few sums.
/// </para>
/// </summary>
internal sealed class CloseUpClock
{
    private readonly Lock _gate = new();

    private double _length;       // the song's length (s)
    private double _position;     // where the deck last said it was (s)
    private double _tempo;        // percent
    private bool _playing;

    private double _clockFrom;    // the song's position (s) at _clockAt
    private double _clockAt;      // on the clock, in seconds
    private double _correction;   // where the corrections from reports have got to (s)
    private double _shown;        // how much of it is shown so far: it follows over CorrectionSpread
    private double _shownAt;      // on the clock, in seconds

    /// <summary>A report's error counts for this much, so one report moves the playhead little.</summary>
    private const double CorrectionGain = 0.1;

    /// <summary>
    /// The most a report's error counts for (s): reports come in 20 and 40 ms steps, now and then
    /// 80, and one far out mustn't jolt the picture.
    /// </summary>
    private const double CorrectionCap = 0.03;

    /// <summary>
    /// Each correction is spread over about this long (s), not made in one frame: zoomed in tight,
    /// a few milliseconds at once is a visible jerk. Measured on real reports (GZJam.DevTools
    /// clock): the worst frame went from about 2 px off steady to 0.4 px, as close to the music.
    /// </summary>
    private const double CorrectionSpread = 0.15;

    /// <summary>Seconds on a clock every thread reads alike.</summary>
    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    private static double Rate(double tempoPercent) => 1 + tempoPercent / 100;

    /// <summary>Where the playhead is drawn, in seconds into the song.</summary>
    public double Playhead
    {
        get
        {
            lock (_gate)
            {
                double now = Now;
                return _playing && _length > 0 ? Math.Clamp(Predicted(now), 0, _length) : _position;
            }
        }
    }

    /// <summary>The deck said where it is: <paramref name="progress"/> (0..1) of a song <paramref name="length"/> long.</summary>
    public void Report(double progress, double length)
    {
        lock (_gate)
        {
            double now = Now, reported = progress * length;
            _length = length;
            _position = reported;
            if (!_playing || Math.Abs(reported - Predicted(now)) > 0.25)
                Restart(now);   // paused, a seek, a jump, a new song
            else
            {
                // Reports come in uneven steps: ease toward them, a little at a time.
                Shown(now);
                double error = reported - (Clock(now) + _correction);
                _correction += Math.Clamp(error, -CorrectionCap, CorrectionCap) * CorrectionGain;
            }
        }
    }

    /// <summary>The tempo changed: carry on from where the old one had got to.</summary>
    public void SetTempo(double tempoPercent)
    {
        lock (_gate)
        {
            double now = Now;
            _clockFrom += (now - _clockAt) * Rate(_tempo);
            _clockAt = now;
            _tempo = tempoPercent;
        }
    }

    /// <summary>Playing or paused: starts again from where the deck says it is.</summary>
    public void SetPlaying(bool playing)
    {
        lock (_gate)
        {
            _playing = playing;
            Restart(Now);
        }
    }

    /// <summary>Starts again from where the deck says it is (the close-up shown again, say).</summary>
    public void Restart()
    {
        lock (_gate)
            Restart(Now);
    }

    private void Restart(double now) => (_clockFrom, _clockAt, _correction, _shown, _shownAt) = (_position, now, 0, 0, now);

    private double Clock(double now) => _clockFrom + (now - _clockAt) * Rate(_tempo);

    private double Predicted(double now) => Clock(now) + Shown(now);

    /// <summary>The part of the correction shown by <paramref name="now"/>, catching up smoothly.</summary>
    private double Shown(double now)
    {
        if (now > _shownAt)
        {
            _shown = _correction - (_correction - _shown) * Math.Exp(-(now - _shownAt) / CorrectionSpread);
            _shownAt = now;
        }
        return _shown;
    }
}
