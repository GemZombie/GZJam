namespace GZJam.Testing;

/// <summary>
/// Writes stereo 16-bit WAV files of generated audio (tones, silence, drum patterns), so audio
/// tests can measure levels, trimming and tempo.
/// </summary>
public static class TestWav
{
    public const int SampleRate = 44100;

    /// <param name="right">The right channel's level relative to the left: 0 for a tone only on the left.</param>
    public static string WriteTone(string path, TimeSpan duration, double amplitude = 0.5, double frequency = 440,
        TimeSpan silenceBefore = default, TimeSpan silenceAfter = default, double right = 1)
    {
        int before = Frames(silenceBefore), tone = Frames(duration);
        return Write(path, before + tone + Frames(silenceAfter), i =>
            i >= before && i < before + tone ? Math.Sin(2 * Math.PI * frequency * (i - before) / SampleRate) * amplitude : 0, right);
    }

    /// <summary>
    /// A drum pattern in 4/4: a kick on every beat, a snare on <paramref name="backbeat"/> beats
    /// (2 and 4), and a ride cymbal playing swung eighths (on the beat and two thirds through it).
    /// </summary>
    public static string WriteSwingBeat(string path, double bpm, TimeSpan duration, double kick = 0.3, double backbeat = 0.6, double ride = 0.15)
    {
        double beat = 60.0 / bpm;
        var noise = new Random(1);
        var white = Enumerable.Range(0, SampleRate).Select(_ => noise.NextDouble() * 2 - 1).ToArray();
        return Write(path, Frames(duration), i =>
        {
            double t = (double)i / SampleRate;
            int n = (int)(t / beat);
            double inBeat = t - n * beat;
            double sample = 0;

            // Kick: a 60 Hz thump.
            sample += kick * Math.Sin(2 * Math.PI * 60 * inBeat) * Math.Exp(-inBeat * 25);
            // Snare on 2 and 4: a noise burst.
            if (n % 2 == 1)
                sample += backbeat * white[i % white.Length] * Math.Exp(-inBeat * 30);
            // Ride: swung eighths, a short bright tick on the beat and at two thirds.
            double sinceRide = inBeat >= beat * 2 / 3 ? inBeat - beat * 2 / 3 : inBeat;
            sample += ride * white[(i * 7) % white.Length] * Math.Exp(-sinceRide * 120);
            return Math.Clamp(sample, -1, 1);
        });
    }

    private static int Frames(TimeSpan t) => (int)(t.TotalSeconds * SampleRate);

    private static string Write(string path, int frames, Func<int, double> sample, double right = 1)
    {
        const int channels = 2, bytesPerSample = 2;
        int dataBytes = frames * channels * bytesPerSample;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);                                   // PCM
        writer.Write((short)channels);
        writer.Write(SampleRate);
        writer.Write(SampleRate * channels * bytesPerSample);     // byte rate
        writer.Write((short)(channels * bytesPerSample));         // block align
        writer.Write((short)(bytesPerSample * 8));
        writer.Write("data"u8);
        writer.Write(dataBytes);
        for (int i = 0; i < frames; i++)
        {
            double value = sample(i);
            writer.Write((short)(value * short.MaxValue));
            writer.Write((short)(value * right * short.MaxValue));
        }
        return path;
    }
}

/// <summary>A clock tests move by hand.</summary>
public sealed class ManualTime : TimeProvider
{
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _ticks;
    public void Advance(TimeSpan by) => _ticks += by.Ticks;
}
