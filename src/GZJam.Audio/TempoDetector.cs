namespace GZJam.Audio;

/// <summary>
/// Finds a track's tempo from its audio, tuned for swing: the beat a dancer counts (the walking
/// bass, the rhythm guitar's four-to-the-bar), not the backbeat or the swung eighth notes.
/// Samples are pushed in as the track decodes; the onset envelope (how much new sound starts in
/// each instant, measured as spectral flux) is built on the fly, and the tempo is read from its
/// periodicity at the end.
/// </summary>
internal sealed class TempoDetector
{
    private const int TargetRate = 11025;   // plenty for rhythm, and four times less work
    private const int FftSize = 256;        // 23 ms window
    private const int Hop = 128;            // ~86 onset frames a second

    private readonly int _decimate;
    private readonly float[] _ring = new float[FftSize];
    private readonly float[] _window = new float[FftSize];
    private readonly float[] _re = new float[FftSize];
    private readonly float[] _im = new float[FftSize];
    private float[] _previous = new float[FftSize / 2];
    private float[] _current = new float[FftSize / 2];
    private readonly List<float> _onsets = [];
    private float _sum;
    private int _summed;
    private int _filled;
    private int _sinceHop;

    public TempoDetector(int sampleRate)
    {
        _decimate = Math.Max(1, (int)Math.Round((double)sampleRate / TargetRate));
        FrameRate = (double)sampleRate / _decimate / Hop;
        for (int i = 0; i < FftSize; i++)
            _window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / FftSize));
    }

    /// <summary>Onset frames per second.</summary>
    public double FrameRate { get; }

    public IReadOnlyList<float> Onsets => _onsets;

    /// <summary>Adds one mono sample at the track's own rate.</summary>
    public void Push(float sample)
    {
        _sum += sample;
        if (++_summed < _decimate)
            return;
        float s = _sum / _summed;
        _sum = 0;
        _summed = 0;

        _ring[_filled % FftSize] = s;
        _filled++;
        if (++_sinceHop < Hop || _filled < FftSize)
            return;
        _sinceHop = 0;
        _onsets.Add(Flux());
    }

    /// <summary>Spectral flux: the summed rise in log magnitude across all bins since the last frame.</summary>
    private float Flux()
    {
        int start = _filled % FftSize;   // oldest sample in the ring
        for (int i = 0; i < FftSize; i++)
        {
            _re[i] = _ring[(start + i) % FftSize] * _window[i];
            _im[i] = 0;
        }
        Fft(_re, _im);

        float flux = 0;
        for (int k = 1; k < FftSize / 2; k++)
        {
            float magnitude = MathF.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]);
            float level = MathF.Log(1 + 100 * magnitude);
            _current[k] = level;
            float rise = level - _previous[k];
            if (rise > 0)
                flux += rise;
        }
        (_previous, _current) = (_current, _previous);
        return flux;
    }

    /// <summary>
    /// Tempo candidates between <paramref name="from"/> and <paramref name="to"/>, best first;
    /// empty if there's no steady beat (or too little music).
    /// </summary>
    public List<(double Bpm, double Score)> Estimate(TimeSpan from, TimeSpan to, int minBpm, int maxBpm)
    {
        int first = (int)(from.TotalSeconds * FrameRate), last = Math.Min(_onsets.Count, (int)(to.TotalSeconds * FrameRate));
        if (last - first < FrameRate * 10)
            return [];
        return Candidates(_onsets.Skip(first).Take(last - first).ToArray(), FrameRate, minBpm, maxBpm);
    }

    /// <summary>For tuning: autocorrelation of the whole onset envelope, normalized to 1 at lag 0.</summary>
    internal double[] Autocorrelation(int maxLag)
    {
        float[] o = HighPass(_onsets.ToArray(), (int)FrameRate);
        var ac = new double[maxLag + 1];
        for (int lag = 0; lag <= maxLag; lag++)
        {
            double sum = 0;
            for (int i = lag; i < o.Length; i++)
                sum += o[i] * o[i - lag];
            ac[lag] = sum / (o.Length - lag);
        }
        return ac.Select(x => x / ac[0]).ToArray();
    }

    internal static double Interpolate(double[] ac, double lag) => At(ac, lag);

    /// <summary>Tempo candidates, best first, with their scores (for tuning).</summary>
    internal static List<(double Bpm, double Score)> Candidates(float[] onsets, double frameRate, int minBpm, int maxBpm)
    {
        // Remove the slowly changing loudness so only the pulses remain.
        float[] o = HighPass(onsets, (int)frameRate);

        // Autocorrelation out to four of the slowest beats.
        int maxLag = (int)Math.Ceiling(60 * frameRate / minBpm * 4) + 2;
        if (o.Length <= maxLag * 2)
            return [];
        var ac = new double[maxLag + 1];
        for (int lag = 0; lag <= maxLag; lag++)
        {
            double sum = 0;
            for (int i = lag; i < o.Length; i++)
                sum += o[i] * o[i - lag];
            ac[lag] = sum / (o.Length - lag);
        }
        if (ac[0] <= 0)
            return [];

        // A beat period is strong when the pulses line up at one, two, three and four beats.
        var scores = new List<(double Bpm, double Score)>();
        for (double bpm = minBpm; bpm <= maxBpm; bpm += 0.25)
        {
            double period = 60 * frameRate / bpm;
            double score = 0;
            for (int k = 1; k <= 4; k++)
                score += At(ac, period * k);
            scores.Add((bpm, score / 4 / ac[0]));
        }

        // Peaks only, best first.
        var peaks = new List<(double Bpm, double Score)>();
        for (int i = 1; i < scores.Count - 1; i++)
        {
            if (scores[i].Score > scores[i - 1].Score && scores[i].Score >= scores[i + 1].Score && scores[i].Score > 0)
                peaks.Add(scores[i]);
        }
        peaks = peaks.OrderByDescending(p => p.Score).ToList();
        if (peaks.Count == 0)
            return peaks;

        // Half or double time? Swing's strong backbeat (2 and 4) often makes half the tempo
        // score best. If there's a regular pulse right between those slow beats, they're really
        // two beats each. A true ballad has little there: swung eighths fall off the midpoint.
        var best = peaks[0];
        double beat = 60 * frameRate / best.Bpm;
        double between = At(ac, beat / 2) / Math.Max(1e-9, At(ac, beat));
        var half = Near(peaks, best.Bpm / 2);
        if (best.Bpm * 2 <= maxBpm && between >= (best.Bpm < 100 ? 0.4 : 0.6))
            best = Near(peaks, best.Bpm * 2) ?? (best.Bpm * 2, best.Score);
        else if (best.Bpm >= 280 && half is { } h && h.Score >= best.Score * 0.75)
            best = h;   // eighth notes rather than beats; very fast swing is rare
        peaks.Remove(best);
        peaks.Insert(0, best);
        return peaks;
    }

    /// <summary>The candidate within 3% of <paramref name="bpm"/>, if there is one.</summary>
    private static (double Bpm, double Score)? Near(List<(double Bpm, double Score)> peaks, double bpm) =>
        peaks.Where(p => Math.Abs(p.Bpm - bpm) <= bpm * 0.03).Cast<(double, double)?>().FirstOrDefault();

    private static double At(double[] ac, double lag)
    {
        int i = (int)lag;
        if (i + 1 >= ac.Length)
            return 0;
        double t = lag - i;
        return ac[i] * (1 - t) + ac[i + 1] * t;
    }

    private static float[] HighPass(float[] x, int window)
    {
        var y = new float[x.Length];
        double sum = 0;
        int half = window / 2;
        int lo = 0, hi = 0;
        for (int i = 0; i < x.Length; i++)
        {
            while (hi < x.Length && hi <= i + half)
                sum += x[hi++];
            while (lo < i - half)
                sum -= x[lo++];
            float mean = (float)(sum / (hi - lo));
            y[i] = Math.Max(0, x[i] - mean);
        }
        return y;
    }

    /// <summary>In-place radix-2 FFT.</summary>
    private static void Fft(float[] re, float[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        for (int size = 2; size <= n; size <<= 1)
        {
            double angle = -2 * Math.PI / size;
            float wr = (float)Math.Cos(angle), wi = (float)Math.Sin(angle);
            for (int start = 0; start < n; start += size)
            {
                float cr = 1, ci = 0;
                for (int k = 0; k < size / 2; k++)
                {
                    int a = start + k, b = a + size / 2;
                    float tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                    float next = cr * wr - ci * wi;
                    ci = cr * wi + ci * wr;
                    cr = next;
                }
            }
        }
    }
}
