namespace GZJam.App.ViewModels;

/// <summary>
/// Works out a tempo from taps along to the beat. The BPM is the best straight line through every
/// tap since the last pause, so one tap a little early or late barely moves it, and it settles the
/// longer the DJ taps. A pause of <see cref="Pause"/> starts again.
/// </summary>
public sealed class TapTempo
{
    /// <summary>The taps needed before there's a BPM to show.</summary>
    public const int TapsNeeded = 4;

    /// <summary>A gap this long between taps starts again.</summary>
    public static readonly TimeSpan Pause = TimeSpan.FromSeconds(2);

    private readonly List<double> _taps = [];   // seconds

    /// <summary>Taps since the last pause.</summary>
    public int Count => _taps.Count;

    /// <summary>The tempo tapped (beats a minute), once there are <see cref="TapsNeeded"/> taps.</summary>
    public double? Bpm { get; private set; }

    /// <summary>
    /// Enough taps, evenly enough, to trust: at least 8, on average within 8% of a beat of where
    /// the tempo puts them (about 20 ms at 260 BPM, the wobble of a careful finger).
    /// </summary>
    public bool IsSteady { get; private set; }

    /// <summary>A tap at <paramref name="at"/> (any clock, as long as it's the same one).</summary>
    public void Tap(TimeSpan at)
    {
        double seconds = at.TotalSeconds;
        if (_taps.Count > 0 && (seconds - _taps[^1] >= Pause.TotalSeconds || seconds <= _taps[^1]))
            _taps.Clear();
        _taps.Add(seconds);
        Update();
    }

    public void Reset()
    {
        _taps.Clear();
        Update();
    }

    private void Update()
    {
        int n = _taps.Count;
        if (n < TapsNeeded)
        {
            (Bpm, IsSteady) = (null, false);
            return;
        }
        // Least squares: tap i at time a + i × beat.
        double meanI = (n - 1) / 2.0, meanT = _taps.Average();
        double covariance = 0, variance = 0;
        for (int i = 0; i < n; i++)
        {
            covariance += (i - meanI) * (_taps[i] - meanT);
            variance += (i - meanI) * (i - meanI);
        }
        double beat = covariance / variance;
        if (beat <= 0)
        {
            (Bpm, IsSteady) = (null, false);
            return;
        }
        double squares = 0;
        for (int i = 0; i < n; i++)
            squares += Math.Pow(_taps[i] - (meanT + (i - meanI) * beat), 2);
        Bpm = 60 / beat;
        IsSteady = n >= 8 && Math.Sqrt(squares / n) <= beat * 0.08;
    }
}
