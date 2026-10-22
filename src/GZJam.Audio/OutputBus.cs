using ManagedBass;

namespace GZJam.Audio;

/// <summary>
/// A mixer feeding one output (main speakers or cue headphones). Normally it plays on an audio
/// device; with no device it's "offline" and only produces audio when <see cref="Render"/> pulls
/// it, which is how tests check what each output would sound like.
/// Everything leaving the bus goes through its volume, optional mono fold-down and a safety
/// limiter, and is metered.
/// </summary>
public sealed class OutputBus : IDisposable
{
    /// <summary>
    /// The limiter's ceiling: just under full scale. Mastered tracks already peak near full scale,
    /// so the limiter only acts on what would otherwise clip (e.g. two loud decks blended).
    /// </summary>
    public const float Ceiling = 0.99f;

    /// <summary>
    /// How far the limiter must turn things down (2 dB) before it counts as limiting for the
    /// light. Loud MP3s often decode a little over full scale (one in the library reaches
    /// +1.3 dB); the limiter absorbs that quietly. Two loud decks blended light it.
    /// </summary>
    private const float Noticeable = 0.794f;

    /// <summary>How quickly the limiter lets go after a loud moment (per sample, about 300 ms).</summary>
    private static readonly float Release = MathF.Exp(-1f / (0.3f * MixEngine.SampleRate));

    private readonly DSPProcedure _dsp;   // kept alive: BASS calls it from its mixing thread
    private volatile float _volume = 1;
    private volatile bool _mono;
    private float _gain = 1;
    private float _peakLeft, _peakRight;
    private volatile bool _limited;

    internal OutputBus(int handle, AudioDevice? device)
    {
        Handle = handle;
        Device = device;
        _dsp = Process;
        Bass.ChannelSetDSP(handle, _dsp);
    }

    internal int Handle { get; }

    /// <summary>The device this bus plays on, or null when offline.</summary>
    public AudioDevice? Device { get; }

    public bool IsOffline => Device is null;

    /// <summary>Output level, 0 (silent) to 1 (as mixed).</summary>
    public float Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0, 1);
    }

    /// <summary>
    /// Both sides play the same thing: left and right are averaged. For dance floors, where half
    /// the room would otherwise miss an instrument recorded on one side, and for headphones worn
    /// on one ear.
    /// </summary>
    public bool Mono
    {
        get => _mono;
        set => _mono = value;
    }

    /// <summary>
    /// The loudest left and right samples since the last call (0 to 1), and whether the limiter
    /// had to hold the level down. For meters: call it regularly.
    /// </summary>
    public (float Left, float Right, bool Limited) TakeLevels()
    {
        var levels = (Interlocked.Exchange(ref _peakLeft, 0), Interlocked.Exchange(ref _peakRight, 0), _limited);
        _limited = false;
        return levels;
    }

    internal void CopySettingsFrom(OutputBus other)
    {
        Volume = other.Volume;
        Mono = other.Mono;
    }

    private unsafe void Process(int handle, int channel, IntPtr buffer, int length, IntPtr user)
    {
        var samples = new Span<float>((void*)buffer, length / sizeof(float));
        float volume = _volume, gain = _gain, left = 0, right = 0;
        bool mono = _mono, limited = false;
        for (int i = 0; i + 1 < samples.Length; i += 2)
        {
            float l = samples[i] * volume, r = samples[i + 1] * volume;
            if (mono)
                l = r = (l + r) / 2;

            // Instant attack, smooth release: the gain drops at once to keep this frame under
            // the ceiling, then recovers gradually so there's no pumping.
            gain = 1 - (1 - gain) * Release;
            float peak = Math.Max(Math.Abs(l), Math.Abs(r));
            if (peak * gain > Ceiling)
            {
                gain = Ceiling / peak;
                limited |= gain < Noticeable;
            }
            l *= gain;
            r *= gain;

            samples[i] = l;
            samples[i + 1] = r;
            left = Math.Max(left, Math.Abs(l));
            right = Math.Max(right, Math.Abs(r));
        }
        _gain = gain;
        if (limited)
            _limited = true;
        Raise(ref _peakLeft, left);
        Raise(ref _peakRight, right);
    }

    private static void Raise(ref float peak, float value)
    {
        float current;
        while (value > (current = Volatile.Read(ref peak)) && Interlocked.CompareExchange(ref peak, value, current) != current)
        {
        }
    }

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
