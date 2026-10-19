namespace GZJam.Audio;

/// <summary>An audio output device as reported by BASS.</summary>
/// <param name="Index">BASS device number. Not stable across sessions; use <see cref="Preference"/> to remember a device.</param>
/// <param name="Name">Human-readable name, e.g. "Speakers (USB Audio CODEC)".</param>
/// <param name="Driver">Driver identifier (endpoint ID on Windows, ALSA name on Linux).</param>
public sealed record AudioDevice(int Index, string Name, string Driver, bool IsDefault)
{
    public AudioDevicePreference Preference => new(Name, Driver);

    /// <summary>
    /// BASS's virtual "Default" device, which follows the OS default output. Avoid it for
    /// gigs: if the OS default changes mid-set, the music moves with it.
    /// </summary>
    public bool FollowsSystemDefault => Driver.Length == 0 || Driver == "default";

    public override string ToString() =>
        FollowsSystemDefault ? $"{Name} (follows system setting)"
        : IsDefault ? $"{Name} (system default)"
        : Name;
}

/// <summary>What we persist to find the same physical device again next session.</summary>
public sealed record AudioDevicePreference(string Name, string Driver);
