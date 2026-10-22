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
    private readonly Lock _lock = new();   // analysis threads decode alongside the UI thread

    public AudioEngine()
    {
        // Touching the add-on versions forces bass_fx and bassmix to load alongside bass, so a
        // missing library fails here at startup rather than mid-set.
        FxVersion = BassFx.Version;
        MixVersion = BassMix.Version;
        BassVersion = Bass.Version;
        LoadFormatAddOns();

        // Shorter buffers than BASS's defaults (500 ms / 100 ms) keep the crossfader, seeking
        // and the cue preview responsive. Still comfortably safe against dropouts.
        Bass.Configure(Configuration.PlaybackBufferLength, 200);
        Bass.Configure(Configuration.UpdatePeriod, 20);
    }

    public Version BassVersion { get; }
    public Version FxVersion { get; }
    public Version MixVersion { get; }

    /// <summary>
    /// un4seen's add-ons for the formats BASS doesn't play by itself (it plays MP3, MP2, OGG, WAV
    /// and AIFF), beside bass in the program's folder. AAC comes from the computer instead: Media
    /// Foundation on Windows, Core Audio on a Mac, which also plays ALAC, so there's no Mac build
    /// of bassalac. Linux has no AAC: the free add-on for it is GPL, which GZJam can't include.
    /// </summary>
    private static readonly string[] FormatAddOns = ["bassflac", "bassopus", "bassalac"];

    private static readonly Lock s_addOnsLock = new();
    private static List<string>? s_loadedAddOns;

    /// <summary>The format add-ons that loaded, e.g. "bassflac".</summary>
    public static IReadOnlyList<string> LoadedAddOns => s_loadedAddOns ?? [];

    /// <summary>Loads the add-ons once per process (BASS's plugins are process-wide).</summary>
    private static void LoadFormatAddOns()
    {
        lock (s_addOnsLock)
        {
            if (s_loadedAddOns is not null)
                return;
            s_loadedAddOns = [];
            foreach (string name in FormatAddOns)
            {
                string file = OperatingSystem.IsWindows() ? $"{name}.dll"
                    : OperatingSystem.IsMacOS() ? $"lib{name}.dylib"
                    : $"lib{name}.so";
                string path = Path.Combine(AppContext.BaseDirectory, file);
                if (File.Exists(path) && (Bass.PluginLoad(path) != 0 || Bass.LastError == Errors.Already))
                    s_loadedAddOns.Add(name);
            }
        }
    }

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
        lock (_lock)
        {
            if (_initializedDevices.Contains(deviceIndex))
                return;

            // Linux: the desktop's sound server (PipeWire) holds a card for a few seconds after the
            // last sound through it, even GZJam's own through "Default", so a busy card is retried.
            var giveUp = DateTime.UtcNow + (OperatingSystem.IsLinux() ? BusyWait : TimeSpan.Zero);
            while (!Bass.Init(deviceIndex) && Bass.LastError != Errors.Already)
            {
                if (Bass.LastError != Errors.Busy || DateTime.UtcNow >= giveUp)
                    throw new AudioException($"Could not open output device {deviceIndex}", Bass.LastError);
                Thread.Sleep(250);
            }

            _initializedDevices.Add(deviceIndex);
        }
    }

    /// <summary>How long a busy output is retried on Linux: PipeWire lets an idle card go after 5 s.</summary>
    internal static TimeSpan BusyWait { get; set; } = TimeSpan.FromSeconds(7);

    /// <summary>
    /// Closes the output devices that aren't in <paramref name="keep"/>, so another program (or
    /// GZJam itself, through a different device) can open them. Decoding's "no sound" device stays.
    /// </summary>
    internal void ReleaseDevicesExcept(params int?[] keep)
    {
        lock (_lock)
        {
            foreach (int device in _initializedDevices.Where(d => d != NoSoundDevice && !keep.Contains(d)).ToList())
            {
                Bass.CurrentDevice = device;
                Bass.Free();
                _initializedDevices.Remove(device);
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
            FreeDevices();
    }

    private void FreeDevices()
    {
        foreach (int device in _initializedDevices)
        {
            // BASS is process-wide, so another engine may already have freed this device.
            if (Bass.GetDeviceInfo(device, out DeviceInfo info) && !info.IsInitialized)
                continue;
            try
            {
                Bass.CurrentDevice = device;
                Bass.Free();
            }
            catch (BassException)
            {
                // Already gone; nothing to release.
            }
        }
        _initializedDevices.Clear();
    }
}

public sealed class AudioException : Exception
{
    public AudioException(string message, Errors error) : this(error, $"{message} (BASS error: {error})")
    {
    }

    private AudioException(Errors error, string message) : base(message) => Error = error;

    public Errors Error { get; }

    /// <summary>Kinds of file that hold AAC, which needs the computer's own decoder.</summary>
    private static readonly HashSet<string> AacExtensions = new([".m4a", ".aac", ".mp4"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A song BASS couldn't open (just after it failed), with a reason a DJ can act on: shown as
    /// "Couldn't load {song}: {reason}".
    /// </summary>
    internal static AudioException CouldNotOpen(string path)
    {
        Errors error = Bass.LastError;
        bool aac = AacExtensions.Contains(Path.GetExtension(path));
        string reason = error switch
        {
            Errors.FileOpen => "the file couldn't be read. Has it been moved or deleted?",
            Errors.FileFormat or Errors.Codec or Errors.NotAvailable when aac && OperatingSystem.IsLinux() =>
                "it's AAC, which GZJam can't play on Linux (Apple Lossless .m4a files play).",
            Errors.FileFormat or Errors.Codec or Errors.NotAvailable when aac && OperatingSystem.IsWindows() && !HasMediaFoundation =>
                "it's AAC, which needs Windows' Media Feature Pack (missing from Windows \"N\" editions).",
            Errors.FileFormat or Errors.Codec or Errors.NotAvailable => "it's a kind of file GZJam can't play.",
            _ => $"BASS error {error}.",
        };
        return new AudioException(error, reason);
    }

    /// <summary>An output another program has, said so a DJ can free it.</summary>
    internal static AudioException Busy(string device) => new(Errors.Busy, OperatingSystem.IsLinux()
        ? $"{device} is in use. The desktop's sound system keeps a card while other sound plays through it: " +
          "make another output the computer's default (or choose Default here), then try again."
        : $"{device} is in use by another program.");

    /// <summary>Windows' own decoders (AAC among them), absent from "N" editions and servers.</summary>
    internal static bool HasMediaFoundation =>
        OperatingSystem.IsWindows() && File.Exists(Path.Combine(Environment.SystemDirectory, "mfplat.dll"));
}
