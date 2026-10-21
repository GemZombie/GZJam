namespace GZJam.Audio;

/// <summary>
/// Measures how loud a whole track sounds, the way broadcast and streaming services do (EBU R128 /
/// ITU-R BS.1770 integrated loudness, in LUFS): K-weighting (a high shelf for how ears hear, and a
/// high-pass), 400 ms blocks every 100 ms, then gating that ignores silence and quiet passages.
/// Also tracks the sample peak. Feed it interleaved float frames with <see cref="Push"/>.
/// </summary>
public sealed class LoudnessMeter
{
    private readonly int _channels;
    private readonly bool _dualMono;
    private readonly Biquad[] _shelf, _highPass;
    private readonly int _subBlockFrames;
    private readonly List<double> _subBlocks = [];   // mean square (summed over channels) per 100 ms
    private double _sum;
    private int _frames;
    private float _peak;

    public LoudnessMeter(int channels, int rate)
    {
        _channels = Math.Max(1, channels);
        // A mono file plays through both speakers, so it counts twice, as dual mono.
        _dualMono = _channels == 1;
        _shelf = Enumerable.Range(0, _channels).Select(_ => Biquad.KShelf(rate)).ToArray();
        _highPass = Enumerable.Range(0, _channels).Select(_ => Biquad.KHighPass(rate)).ToArray();
        _subBlockFrames = Math.Max(1, rate / 10);
    }

    /// <summary>Adds frames of interleaved samples.</summary>
    public void Push(ReadOnlySpan<float> interleaved, int frames)
    {
        for (int f = 0; f < frames; f++)
        {
            double square = 0;
            for (int ch = 0; ch < _channels; ch++)
            {
                float sample = interleaved[f * _channels + ch];
                _peak = Math.Max(_peak, Math.Abs(sample));
                double weighted = _highPass[ch].Process(_shelf[ch].Process(sample));
                square += weighted * weighted;
            }
            _sum += _dualMono ? square * 2 : square;
            if (++_frames == _subBlockFrames)
            {
                _subBlocks.Add(_sum / _frames);
                _sum = 0;
                _frames = 0;
            }
        }
    }

    /// <summary>The highest sample, in dB below full scale (0 = full scale), or null for silence.</summary>
    public double? PeakDb => _peak > 0 ? 20 * Math.Log10(_peak) : null;

    /// <summary>Integrated loudness in LUFS, or null if the track is too short or silent to measure.</summary>
    public double? IntegratedLufs
    {
        get
        {
            // 400 ms blocks overlapping by 75%: four 100 ms sub-blocks each.
            var blocks = new List<double>();
            for (int i = 0; i + 4 <= _subBlocks.Count; i++)
                blocks.Add((_subBlocks[i] + _subBlocks[i + 1] + _subBlocks[i + 2] + _subBlocks[i + 3]) / 4);

            // Absolute gate at -70 LUFS, then a relative gate 10 LU below what passed it.
            var audible = blocks.Where(z => Lufs(z) > -70).ToList();
            if (audible.Count == 0)
                return null;
            double relativeGate = Lufs(audible.Average()) - 10;
            var gated = audible.Where(z => Lufs(z) > relativeGate).ToList();
            return gated.Count == 0 ? null : Lufs(gated.Average());
        }
    }

    private static double Lufs(double meanSquare) => meanSquare <= 0 ? double.NegativeInfinity : -0.691 + 10 * Math.Log10(meanSquare);

    /// <summary>A second-order filter, direct form I. Coefficients for any sample rate (as in libebur128).</summary>
    private sealed class Biquad(double b0, double b1, double b2, double a1, double a2)
    {
        private double _x1, _x2, _y1, _y2;

        public double Process(double x)
        {
            double y = b0 * x + b1 * _x1 + b2 * _x2 - a1 * _y1 - a2 * _y2;
            (_x2, _x1, _y2, _y1) = (_x1, x, _y1, y);
            return y;
        }

        /// <summary>Stage 1: a high shelf, about +4 dB above 1.5 kHz (the head's effect on what reaches the ear).</summary>
        public static Biquad KShelf(int rate)
        {
            const double f0 = 1681.974450955533, gain = 3.999843853973347, q = 0.7071752369554196;
            double k = Math.Tan(Math.PI * f0 / rate);
            double vh = Math.Pow(10, gain / 20), vb = Math.Pow(vh, 0.4996667741545416);
            double a0 = 1 + k / q + k * k;
            return new Biquad(
                (vh + vb * k / q + k * k) / a0, 2 * (k * k - vh) / a0, (vh - vb * k / q + k * k) / a0,
                2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0);
        }

        /// <summary>Stage 2: a high-pass around 38 Hz (deep bass counts for little).</summary>
        public static Biquad KHighPass(int rate)
        {
            const double f0 = 38.13547087602444, q = 0.5003270373238773;
            double k = Math.Tan(Math.PI * f0 / rate);
            double a0 = 1 + k / q + k * k;
            return new Biquad(1, -2, 1, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0);
        }
    }
}

/// <summary>How loudness matching turns a track up or down.</summary>
public static class LoudnessMatch
{
    /// <summary>Everything is brought towards this (Apple Music and many DJ apps use about the same).</summary>
    public const double TargetLufs = -16;

    /// <summary>The most a quiet track is turned up, however quiet.</summary>
    public const double MaxBoostDb = 9;

    /// <summary>
    /// How far a boost may push peaks over full scale. The speakers' limiter takes up to about 2 dB
    /// without it being noticeable (its light stays off), so a little is allowed.
    /// </summary>
    public const double PeakAllowanceDb = 1.5;

    /// <summary>The gain, in dB, that brings a track of this loudness and peak to the target.</summary>
    public static double GainDb(double lufs, double? peakDb)
    {
        double gain = TargetLufs - lufs;
        if (gain <= 0)
            return Math.Round(gain, 1);
        double headroom = PeakAllowanceDb - (peakDb ?? 0);
        return Math.Round(Math.Max(0, Math.Min(gain, Math.Min(MaxBoostDb, headroom))), 1);
    }
}
