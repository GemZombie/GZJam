using ManagedBass;
using ManagedBass.Fx;
using ManagedBass.Mix;

namespace GZJam.Audio;

/// <summary>
/// Owns BASS for the process: device enumeration and per-device initialization.
/// Create one instance at startup and dispose it on exit.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    /// <summary>BASS device 0: decodes without producing output.</summary>
    internal const int NoSoundDevice = 0;

    private readonly HashSet<int> _initializedDevices = [];

    public AudioEngine()
    {
        // Touching the add-on versions forces bass_fx and bassmix to load alongside bass, so a
        // missing library fails here at startup rather than mid-set.
        FxVersion = BassFx.Version;
        MixVersion = BassMix.Version;
        BassVersion = Bass.Version;

        // Shorter buffers than BASS's defaults (500 ms / 100 ms) keep the crossfader, seeking
        // and the cue preview responsive. Still comfortably safe against dropouts.
        Bass.Configure(Configuration.PlaybackBufferLength, 200);
        Bass.Configure(Configuration.UpdatePeriod, 20);
    }

    public Version BassVersion { get; }
    public Version FxVersion { get; }
    public Version MixVersion { get; }

    /// <summary>Lists enabled output devices. BASS device 0 ("No sound") is skipped.</summary>
    public IReadOnlyList<AudioDevice> GetOutputDevices()
    {
        var devices = new List<AudioDevice>();
        for (int i = 1; Bass.GetDeviceInfo(i, out DeviceInfo info); i++)
        {
            if (!info.IsEnabled || info.IsLoopback)
                continue;
            devices.Add(new AudioDevice(i, info.Name, info.Driver ?? "", info.IsDefault));
        }
        return devices;
    }

    /// <summary>
    /// Finds the current device matching a saved preference. Tries name and driver together,
    /// then name alone (Linux card numbers shift when USB devices move), then driver alone
    /// (Windows endpoint IDs survive a device rename).
    /// </summary>
    public AudioDevice? FindDevice(AudioDevicePreference? preference)
    {
        if (preference is null)
            return null;

        var devices = GetOutputDevices();
        return devices.FirstOrDefault(d => d.Name == preference.Name && d.Driver == preference.Driver)
            ?? devices.FirstOrDefault(d => d.Name == preference.Name)
            ?? devices.FirstOrDefault(d => d.Driver.Length > 0 && d.Driver == preference.Driver);
    }

    /// <summary>The real device that is currently the OS default, rather than BASS's virtual "Default".</summary>
    public AudioDevice? GetDefaultDevice()
    {
        var devices = GetOutputDevices();
        return devices.FirstOrDefault(d => d.IsDefault && !d.FollowsSystemDefault)
            ?? devices.FirstOrDefault(d => d.IsDefault);
    }

    /// <summary>
    /// Makes BASS's "no sound" device current on this thread. Decoding (reading files, BPM
    /// detection, offline mixing) happens there so it never touches a real output.
    /// </summary>
    internal void UseNoSoundDevice()
    {
        EnsureInitialized(NoSoundDevice);
        Bass.CurrentDevice = NoSoundDevice;
    }

    /// <summary>Initializes a device for output if it isn't already.</summary>
    internal void EnsureInitialized(int deviceIndex)
    {
        if (_initializedDevices.Contains(deviceIndex))
            return;

        if (!Bass.Init(deviceIndex) && Bass.LastError != Errors.Already)
            throw new AudioException($"Could not open output device {deviceIndex}", Bass.LastError);

        _initializedDevices.Add(deviceIndex);
    }

    public void Dispose()
    {
        foreach (int device in _initializedDevices)
        {
            Bass.CurrentDevice = device;
            Bass.Free();
        }
        _initializedDevices.Clear();
    }
}

public sealed class AudioException(string message, Errors error)
    : Exception($"{message} (BASS error: {error})")
{
    public Errors Error { get; } = error;
}
