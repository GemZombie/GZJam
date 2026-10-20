namespace GZJam.Testing;

/// <summary>Writes stereo 16-bit WAV files containing a sine tone, so audio tests can measure levels.</summary>
public static class TestWav
{
    public static string WriteTone(string path, TimeSpan duration, double amplitude = 0.5, double frequency = 440)
    {
        const int sampleRate = 44100, channels = 2, bytesPerSample = 2;
        int frames = (int)(duration.TotalSeconds * sampleRate);
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
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bytesPerSample);     // byte rate
        writer.Write((short)(channels * bytesPerSample));         // block align
        writer.Write((short)(bytesPerSample * 8));
        writer.Write("data"u8);
        writer.Write(dataBytes);
        for (int i = 0; i < frames; i++)
        {
            short sample = (short)(Math.Sin(2 * Math.PI * frequency * i / sampleRate) * amplitude * short.MaxValue);
            writer.Write(sample);
            writer.Write(sample);
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
