using System.Collections.Concurrent;
using ManagedBass;
using ManagedBass.Mix;
using ManagedBass.Wasapi;

namespace GZJam.Audio;

/// <summary>
/// Windows: an output GZJam takes for itself (WASAPI exclusive mode), so no other program's sound,
/// a notification or a browser, can reach the speakers mid-set. un4seen's BASSWASAPI add-on pulls
/// the mix from a decoding mixer. Linux gets the same by opening a sound card directly.
/// </summary>
internal sealed class ExclusiveOutput : IDisposable
{
    /// <summary>The rates tried, music's own first so most songs aren't resampled.</summary>
    private static readonly int[] Rates = [44100, 48000, 96000, 88200, 192000];

    private static readonly Lock s_lock = new();
    private static readonly WasapiNotifyProcedure s_notify = OnNotify;   // kept alive: BASSWASAPI calls it
    private static readonly ConcurrentDictionary<int, ExclusiveOutput> s_open = new();   // read by the notification, lock-free
    private static bool s_notifying;

    /// <summary>A just-opened output fills its buffer first: an empty one then isn't a dropout.</summary>
    private static readonly TimeSpan Settling = TimeSpan.FromSeconds(1);

    private readonly int _device;
    private readonly long _opened = Environment.TickCount64;
    private volatile bool _lost;

    private ExclusiveOutput(int device, int mixer, int sampleRate)
    {
        _device = device;
        Mixer = mixer;
        SampleRate = sampleRate;
    }

    /// <summary>
    /// Whether the output's buffer is empty right now, which is heard as a dropout or crackle: the
    /// computer didn't keep up. Checked regularly, it tells a report of crackles from other causes.
    /// </summary>
    public bool IsDry()
    {
        if (_lost || Environment.TickCount64 - _opened < Settling.TotalMilliseconds)
            return false;
        lock (s_lock)
        {
            if (!s_open.ContainsKey(_device))
                return false;
            BassWasapi.CurrentDevice = _device;
            return BassWasapi.GetData(IntPtr.Zero, (int)DataFlags.Available) == 0;
        }
    }

    /// <summary>The decoding mixer the output plays: decks and the preview are added to it.</summary>
    public int Mixer { get; }

    public int SampleRate { get; }

    /// <summary>The device stopped by itself: unplugged, switched off, or taken by Windows.</summary>
    public bool IsLost => _lost;

    /// <summary>
    /// Opens <paramref name="device"/> for GZJam alone, playing a new decoding mixer. Throws an
    /// <see cref="AudioException"/> saying why it couldn't (another program has it, or it doesn't
    /// take stereo at the usual rates), so the caller can share it instead.
    /// </summary>
    public static ExclusiveOutput Open(AudioEngine engine, AudioDevice device, BassFlags mixerFlags)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("WASAPI is Windows only.");
        lock (s_lock)
        {
            if (!s_notifying)
                s_notifying = BassWasapi.SetNotify(s_notify, IntPtr.Zero);

            int wasapi = FindDevice(device.Driver)
                ?? throw new AudioException($"{device.Name} wasn't found", Errors.Device);
            int rate = Rates.FirstOrDefault(r => BassWasapi.CheckFormat(wasapi, r, MixEngine.Channels, WasapiInitFlags.Exclusive) != WasapiFormat.Unknown);
            if (rate == 0)
                throw new AudioException($"{device.Name} doesn't take stereo at the usual rates", Errors.SampleFormat);

            engine.UseNoSoundDevice();
            int mixer = BassMix.CreateMixerStream(rate, MixEngine.Channels, mixerFlags | BassFlags.Decode);
            if (mixer == 0)
                throw new AudioException("Could not create mixer", Bass.LastError);

            // WASAPIPROC_BASS (-1): BASSWASAPI reads the mixer itself, on its own thread.
            if (!BassWasapi.InitEx(wasapi, rate, MixEngine.Channels, WasapiInitFlags.Exclusive, engine.ExclusiveBufferSeconds, 0, new IntPtr(-1), new IntPtr(mixer)))
            {
                Errors error = Bass.LastError;
                Bass.StreamFree(mixer);
                throw error == Errors.Busy
                    ? new AudioException($"{device.Name} is in use by another program", error)
                    : new AudioException($"{device.Name} can't be opened for GZJam alone", error);
            }
            if (!BassWasapi.Start())
            {
                Errors error = Bass.LastError;
                BassWasapi.Free();
                Bass.StreamFree(mixer);
                throw new AudioException($"{device.Name} wouldn't start", error);
            }

            var output = new ExclusiveOutput(wasapi, mixer, rate);
            s_open[wasapi] = output;
            return output;
        }
    }

    /// <summary>BASSWASAPI's device for a BASS output, matched by the endpoint ID both report.</summary>
    private static int? FindDevice(string endpointId)
    {
        for (int i = 0; BassWasapi.GetDeviceInfo(i, out WasapiDeviceInfo info); i++)
        {
            if (info.IsEnabled && !info.IsInput && !info.IsLoopback && string.Equals(info.ID, endpointId, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return null;
    }

    private static void OnNotify(WasapiNotificationType notify, int device, IntPtr user)
    {
        if (notify is not (WasapiNotificationType.Disabled or WasapiNotificationType.Fail))
            return;
        if (s_open.TryGetValue(device, out ExclusiveOutput? output))
            output._lost = true;
    }

    public void Dispose()
    {
        lock (s_lock)
        {
            if (s_open.TryRemove(_device, out _))
            {
                BassWasapi.CurrentDevice = _device;
                BassWasapi.Stop(true);
                BassWasapi.Free();
            }
            Bass.StreamFree(Mixer);
        }
    }
}
