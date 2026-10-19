using ManagedBass;
using ManagedBass.Fx;

namespace GZJam.Audio;

public sealed class BpmDetector(AudioEngine engine)
{
    /// <summary>
    /// Estimates a track's BPM. The default range covers slow blues through fast balboa;
    /// BASS's own default (45-230) would halve most fast swing tunes.
    /// </summary>
    public double? Detect(string path, int minBpm = 60, int maxBpm = 320, double maxSeconds = 120)
    {
        // Decode-only work runs on BASS's "no sound" device so it never touches real outputs.
        engine.EnsureInitialized(AudioEngine.NoSoundDevice);
        Bass.CurrentDevice = AudioEngine.NoSoundDevice;

        int stream = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Prescan);
        if (stream == 0)
            throw new AudioException($"Could not open '{path}'", Bass.LastError);

        try
        {
            double length = Bass.ChannelBytes2Seconds(stream, Bass.ChannelGetLength(stream));
            // Skip a short intro; many records open with a solo pickup or rubato.
            double start = Math.Min(10, length / 4);
            double end = Math.Min(length, start + maxSeconds);

            var bpm = BassFx.BPMDecodeGet(stream, start, end, (maxBpm << 16) | minBpm, BassFlags.Default, null, IntPtr.Zero);
            BassFx.BPMFree(stream);
            return bpm > 0 ? bpm : null;
        }
        finally
        {
            Bass.StreamFree(stream);
        }
    }
}
