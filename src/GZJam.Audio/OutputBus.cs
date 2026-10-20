using ManagedBass;

namespace GZJam.Audio;

/// <summary>
/// A mixer feeding one output (main speakers or cue headphones). Normally it plays on an audio
/// device; with no device it's "offline" and only produces audio when <see cref="Render"/> pulls
/// it, which is how tests check what each output would sound like.
/// </summary>
public sealed class OutputBus : IDisposable
{
    internal OutputBus(int handle, AudioDevice? device)
    {
        Handle = handle;
        Device = device;
    }

    internal int Handle { get; }

    /// <summary>The device this bus plays on, or null when offline.</summary>
    public AudioDevice? Device { get; }

    public bool IsOffline => Device is null;

    /// <summary>
    /// Offline buses only: pulls <paramref name="duration"/> of audio through the mix and returns
    /// its peak level (0 = silence, 1 = full scale).
    /// </summary>
    public float Render(TimeSpan duration)
    {
        if (!IsOffline)
            throw new InvalidOperationException("Only offline buses can be rendered manually.");

        int remaining = (int)(duration.TotalSeconds * MixEngine.SampleRate) * MixEngine.Channels;
        var buffer = new float[4096];
        float peak = 0;
        while (remaining > 0)
        {
            int wanted = Math.Min(remaining, buffer.Length);
            int bytes = Bass.ChannelGetData(Handle, buffer, (wanted * sizeof(float)) | (int)DataFlags.Float);
            if (bytes <= 0)
                break;
            int samples = bytes / sizeof(float);
            for (int i = 0; i < samples; i++)
                peak = Math.Max(peak, Math.Abs(buffer[i]));
            remaining -= samples;
        }
        return peak;
    }

    public void Dispose() => Bass.StreamFree(Handle);
}
