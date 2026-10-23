using ManagedBass;

namespace GZJam.Audio;

/// <summary>What one analysis pass learns about a track.</summary>
/// <param name="AudibleStart">Where the music starts, after any silence at the top of the file.</param>
/// <param name="AudibleEnd">Where the music ends, before any silence at the end of the file.</param>
/// <param name="Bpm">Detected tempo in whole BPM (it's good to a few percent), or null if not asked for or not found.</param>
public sealed record TrackAnalysis(TimeSpan Length, TimeSpan AudibleStart, TimeSpan AudibleEnd, double? Bpm, Waveform Waveform)
{
    /// <summary>Integrated loudness in LUFS (see <see cref="LoudnessMeter"/>), or null if it couldn't be measured.</summary>
    public double? Lufs { get; init; }

    /// <summary>The highest sample, in dB below full scale.</summary>
    public double? PeakDb { get; init; }

    /// <summary>The tempos considered, best first (for tuning the detector).</summary>
    internal IReadOnlyList<(double Bpm, double Score)> TempoCandidates { get; init; } = [];
}

/// <summary>
/// Analyzes a track in one decode: the overview waveform, where the audible music starts and
/// ends (so the gap between songs is real silence, the same every time), its BPM, and how loud
/// it is (for loudness matching).
/// Safe to run on several threads at once; nothing is ever played.
/// </summary>
public sealed class TrackAnalyzer(AudioEngine engine)
{
    /// <summary>Bump when the results would change, so stored analyses get redone.</summary>
    public const int Version = 2;   // 2: loudness and peak

    /// <summary>Slices in the overview waveform: about one per pixel of a wide deck.</summary>
    public const int WaveformSlices = 1000;

    /// <summary>Slices per second in a close-up waveform (<see cref="Detail"/>): 10 ms each.</summary>
    public const int DetailSlicesPerSecond = 100;

    /// <summary>Silence is judged in windows this long.</summary>
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// How far below the track's loud passages counts as silence. Generous on purpose: a soft
    /// piano tag or a long fade must not be clipped, and 40 dB down is inaudible on a dance floor.
    /// </summary>
    private const double SilenceBelowLoudDb = 40;

    /// <summary>Never call anything louder than this silence, however quiet the record (-60 dBFS).</summary>
    private const double SilenceFloor = 0.001;

    /// <summary>Kept before the first audible window, so the opening attack isn't shaved.</summary>
    private static readonly TimeSpan PreRoll = TimeSpan.FromMilliseconds(50);

    /// <summary>Kept after the last audible window, so the final note rings out.</summary>
    private static readonly TimeSpan Tail = TimeSpan.FromMilliseconds(250);

    // Band split points for the waveform colours.
    private const double BassBelowHz = 250;
    private const double HighsAboveHz = 2500;

    /// <summary>
    /// BPM search range. Wide enough for slow blues through fast balboa; BASS's own default
    /// (45-230) would halve most fast swing tunes.
    /// </summary>
    public const int MinBpm = 60, MaxBpm = 320;

    /// <param name="bpmRange">
    /// Where the DJ says the tempo is (when automatic detection got it wrong): the strongest
    /// tempo inside it is chosen.
    /// </param>
    public TrackAnalysis Analyze(string path, bool detectBpm = true, (int Min, int Max)? bpmRange = null, CancellationToken cancellationToken = default)
    {
        // Decode-only work runs on BASS's "no sound" device so it never touches real outputs.
        engine.UseNoSoundDevice();
        int stream = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float | BassFlags.Prescan);
        if (stream == 0)
            throw AudioException.CouldNotOpen(path);

        try
        {
            ChannelInfo info = Bass.ChannelGetInfo(stream);
            int channels = Math.Max(1, info.Channels);
            int rate = info.Frequency;
            long totalFrames = Bass.ChannelGetLength(stream) / (sizeof(float) * channels);
            var length = TimeSpan.FromSeconds((double)totalFrames / rate);

            var tempo = detectBpm ? new TempoDetector(rate) : null;
            var loudness = new LoudnessMeter(channels, rate);
            int slices = (int)Math.Clamp(totalFrames / 64, 1, WaveformSlices);
            var (waveform, windows) = Decode(stream, channels, rate, totalFrames, slices, tempo, loudness, cancellationToken);
            var (start, end) = FindAudible(windows, length);

            var candidates = tempo?.Estimate(start, end, MinBpm, MaxBpm) ?? [];
            return new TrackAnalysis(length, start, end, ChooseBpm(candidates, bpmRange) is { } bpm ? Math.Round(bpm) : null, waveform)
            {
                TempoCandidates = candidates,
                Lufs = loudness.IntegratedLufs is { } lufs ? Math.Round(lufs, 1) : null,
                PeakDb = loudness.PeakDb is { } peak ? Math.Round(peak, 1) : null,
            };
        }
        finally
        {
            Bass.StreamFree(stream);
        }
    }

    /// <summary>
    /// The detector's choice, or with a range, the best candidate inside it. If no candidate
    /// is, the detector's choice is doubled or halved into the range.
    /// </summary>
    internal static double? ChooseBpm(IReadOnlyList<(double Bpm, double Score)> candidates, (int Min, int Max)? range)
    {
        if (candidates.Count == 0)
            return null;
        if (range is not { } r)
            return candidates[0].Bpm;
        foreach (var c in candidates)
        {
            if (c.Bpm >= r.Min && c.Bpm <= r.Max)
                return c.Bpm;
        }
        return Fold(candidates[0].Bpm, r.Min, r.Max);
    }

    /// <summary>Doubles or halves <paramref name="bpm"/> into a range (or as near as it gets).</summary>
    internal static double Fold(double bpm, int min, int max)
    {
        double best = bpm, bestDistance = double.MaxValue;
        foreach (int k in new[] { 0, 1, -1, 2, -2, 3, -3 })
        {
            double candidate = bpm * Math.Pow(2, k);
            // How far outside the range, in octaves (0 inside it).
            double distance = candidate < min ? Math.Log2(min / candidate) : candidate > max ? Math.Log2(candidate / max) : 0;
            if (distance < bestDistance - 1e-9)
                (best, bestDistance) = (candidate, distance);
        }
        return best;
    }

    /// <summary>
    /// A close-up waveform of the whole track, <see cref="DetailSlicesPerSecond"/> slices a second
    /// (about 24,000 for a four-minute song): for the deck's close-up, which shows a few seconds
    /// at a time. Worked out when a song is loaded onto a deck, in under a second, and not stored:
    /// for a whole library it would take about a gigabyte.
    /// </summary>
    public Waveform Detail(string path, CancellationToken cancellationToken = default)
    {
        engine.UseNoSoundDevice();
        int stream = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float | BassFlags.Prescan);
        if (stream == 0)
            throw AudioException.CouldNotOpen(path);
        try
        {
            ChannelInfo info = Bass.ChannelGetInfo(stream);
            int channels = Math.Max(1, info.Channels);
            int rate = info.Frequency;
            long totalFrames = Bass.ChannelGetLength(stream) / (sizeof(float) * channels);
            int slices = (int)Math.Max(1, Math.Ceiling((double)totalFrames * DetailSlicesPerSecond / rate));
            return Decode(stream, channels, rate, totalFrames, slices, null, null, cancellationToken).Waveform;
        }
        finally
        {
            Bass.StreamFree(stream);
        }
    }

    /// <summary>Reads every sample once: band peaks per waveform slice, and RMS per silence window.</summary>
    private static (Waveform Waveform, List<float> Windows) Decode(int stream, int channels, int rate, long totalFrames, int slices,
        TempoDetector? tempo, LoudnessMeter? meter, CancellationToken cancellationToken)
    {
        var low = new float[slices];
        var energy = new double[slices];
        var frameCount = new int[slices];
        var mid = new float[slices];
        var high = new float[slices];
        var windows = new List<float>();
        int windowFrames = Math.Max(1, (int)(rate * Window.TotalSeconds));
        double windowSum = 0;
        int windowCount = 0;

        // Two cascaded one-pole low-passes per split point: soft, but plenty for colouring.
        float aLow = OnePole(BassBelowHz, rate), aHigh = OnePole(HighsAboveHz, rate);
        float low1 = 0, low2 = 0, high1 = 0, high2 = 0;

        var buffer = new float[16384 * channels];
        long frame = 0;
        double slicesPerFrame = (double)slices / Math.Max(1, totalFrames);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int bytes = Bass.ChannelGetData(stream, buffer, buffer.Length * sizeof(float) | (int)DataFlags.Float);
            if (bytes <= 0)
                break;
            int frames = bytes / sizeof(float) / channels;
            meter?.Push(buffer, frames);
            for (int f = 0; f < frames; f++, frame++)
            {
                float m = 0;
                for (int ch = 0; ch < channels; ch++)
                    m += buffer[f * channels + ch];
                m /= channels;
                tempo?.Push(m);

                low1 += aLow * (m - low1);
                low2 += aLow * (low1 - low2);
                high1 += aHigh * (m - high1);
                high2 += aHigh * (high1 - high2);
                float bass = low2, highs = m - high2, mids = high2 - low2;

                int s = Math.Min(slices - 1, (int)(frame * slicesPerFrame));
                low[s] = Math.Max(low[s], Math.Abs(bass));
                energy[s] += m * m;
                frameCount[s]++;
                mid[s] = Math.Max(mid[s], Math.Abs(mids));
                high[s] = Math.Max(high[s], Math.Abs(highs));

                windowSum += m * m;
                if (++windowCount == windowFrames)
                {
                    windows.Add((float)Math.Sqrt(windowSum / windowCount));
                    windowSum = 0;
                    windowCount = 0;
                }
            }
        }
        if (windowCount > 0)
            windows.Add((float)Math.Sqrt(windowSum / windowCount));

        var loudness = energy.Select((e, s) => (float)Math.Sqrt(e / Math.Max(1, frameCount[s]))).ToArray();
        var waveform = new Waveform(low.Select(Waveform.Encode).ToArray(), mid.Select(Waveform.Encode).ToArray(),
            high.Select(Waveform.Encode).ToArray(), loudness.Select(Waveform.Encode).ToArray());
        return (waveform, windows);
    }

    /// <summary>
    /// The audible part of the track: from the first window louder than the silence threshold
    /// to the last. The threshold follows the track's own loudness, so a quiet old recording
    /// isn't treated as silence, and hiss below it is.
    /// </summary>
    internal static (TimeSpan Start, TimeSpan End) FindAudible(IReadOnlyList<float> windows, TimeSpan length)
    {
        if (windows.Count == 0)
            return (TimeSpan.Zero, length);

        // "Loud" is the 95th percentile window, so one pop or click doesn't set the bar.
        float loud = windows.Order().ElementAt((int)(windows.Count * 0.95));
        double threshold = Math.Max(SilenceFloor, loud * Math.Pow(10, -SilenceBelowLoudDb / 20));

        int first = -1, last = -1;
        for (int i = 0; i < windows.Count; i++)
        {
            if (windows[i] > threshold)
            {
                if (first < 0)
                    first = i;
                last = i;
            }
        }
        if (first < 0)
            return (TimeSpan.Zero, length);   // silent throughout: leave it alone

        TimeSpan start = Window * first - PreRoll;
        TimeSpan end = Window * (last + 1) + Tail;
        return (start < TimeSpan.Zero ? TimeSpan.Zero : start, end > length ? length : end);
    }

    private static float OnePole(double cutoffHz, int rate) => (float)(1 - Math.Exp(-2 * Math.PI * cutoffHz / rate));
}
