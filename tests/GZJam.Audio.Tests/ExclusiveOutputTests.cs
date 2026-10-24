using ManagedBass;

namespace GZJam.Audio.Tests;

/// <summary>
/// Windows: the speakers taken for GZJam alone (WASAPI exclusive), on this computer's real sound
/// devices. Silence plays; nothing is heard. A device that isn't the computer's default is used,
/// so whatever else is playing isn't interrupted. Skipped elsewhere, and where there's no such
/// device, as on CI machines.
/// </summary>
[Collection("BASS")]
public sealed class ExclusiveOutputTests : IDisposable
{
    private readonly AudioEngine _engine = new();
    private readonly MixEngine _mix;

    public ExclusiveOutputTests() => _mix = new MixEngine(_engine) { ExclusiveSpeakers = true };

    public void Dispose()
    {
        _mix.Dispose();
        _engine.Dispose();
    }

    /// <summary>A real device that isn't the computer's default and opens alone, or the test skips.</summary>
    private AudioDevice SpareDevice()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Exclusive output is for Windows.");
        var spare = _engine.GetOutputDevices().Where(d => !d.FollowsSystemDefault && !d.IsDefault).ToList();
        Assert.SkipWhen(spare.Count == 0, "Needs an output device that isn't the computer's default.");
        foreach (AudioDevice device in spare)
        {
            _mix.SetOutputs(device, null);
            if (_mix.Main.IsExclusive)
                return device;
        }
        Assert.Skip($"No spare device would open alone: {_mix.ExclusiveRefusal}");
        return null!;
    }

    [Fact]
    public void A_chosen_speaker_output_is_gzjams_alone_and_plays_the_mix()
    {
        AudioDevice device = SpareDevice();
        Assert.Null(_mix.ExclusiveRefusal);
        Assert.Equal(device, _mix.Main.Device);

        // Decks and the preview attach to it like any output, and its meter sees the mix.
        Assert.False(_mix.Main.IsLost);
        _mix.Main.TakeLevels();
        Thread.Sleep(300);
        _mix.Update();
        Assert.False(_mix.Main.IsLost);
    }

    [Fact]
    public void Turned_off_or_on_Default_the_speakers_are_shared()
    {
        AudioDevice device = SpareDevice();

        _mix.ExclusiveSpeakers = false;
        _mix.SetOutputs(device, null);
        Assert.False(_mix.Main.IsExclusive);

        _mix.ExclusiveSpeakers = true;
        AudioDevice? followsDefault = _engine.GetOutputDevices().FirstOrDefault(d => d.FollowsSystemDefault);
        if (followsDefault is not null)
        {
            _mix.SetOutputs(followsDefault, null);
            Assert.False(_mix.Main.IsExclusive);
            Assert.Null(_mix.ExclusiveRefusal);   // not refused: never asked for
        }

        _mix.SetOutputs(device, null);
        Assert.True(_mix.Main.IsExclusive);
    }

    [Fact]
    public void It_takes_the_device_even_from_a_program_playing_on_it()
    {
        AudioDevice device = SpareDevice();
        _mix.SetOutputs(null, null);   // let go of it

        // "Another program": a second engine playing on it, shared, like a browser.
        using var other = new AudioEngine();
        using var otherMix = new MixEngine(other);
        otherMix.SetOutputs(device, null);

        _mix.SetOutputs(device, null);
        Assert.True(_mix.Main.IsExclusive);
        Assert.Null(_mix.ExclusiveRefusal);
    }

    [Fact]
    public void A_device_another_program_has_alone_is_refused_and_the_room_keeps_its_output()
    {
        AudioDevice device = SpareDevice();
        _mix.SetOutputs(null, null);

        using var other = new AudioEngine();
        using var otherMix = new MixEngine(other) { ExclusiveSpeakers = true };
        otherMix.SetOutputs(device, null);
        Assert.True(otherMix.Main.IsExclusive);

        // Neither alone nor shared: it says who has it, and nothing changes.
        var refused = Assert.Throws<AudioException>(() => _mix.SetOutputs(device, null));
        Assert.Contains("in use by another program", refused.Message);
        Assert.Null(_mix.Main.Device);
    }

    [Fact]
    public void With_extra_buffering_the_speakers_are_still_gzjams_alone()
    {
        AudioDevice device = SpareDevice();
        _engine.ExtraBuffering = true;
        _mix.SetOutputs(device, null);
        Assert.True(_mix.Main.IsExclusive);
        Thread.Sleep(1300);   // past settling: a running output isn't dry
        Assert.False(_mix.Main.IsDry());
        _engine.ExtraBuffering = false;
    }

    [Fact]
    public void The_same_output_for_speakers_and_headphones_is_split_and_still_gzjams_alone()
    {
        // The speakers on its left, the headphones on its right, through a splitter cable (#15).
        AudioDevice device = SpareDevice();
        _mix.SetOutputs(device, device);
        Assert.True(_mix.IsSplit);
        Assert.True(_mix.SpeakersAreExclusive);
        Assert.Equal(device, _mix.Main.Device);
        Assert.Equal(device, _mix.Cue.Device);
        Assert.Null(_mix.CueRefusal);
        Assert.Null(_mix.ExclusiveRefusal);
    }

    [Fact]
    public void Headphones_that_wont_open_leave_the_speakers_playing()
    {
        AudioDevice device = SpareDevice();
        _mix.SetOutputs(null, null);
        AudioDevice? speakers = _engine.GetOutputDevices().FirstOrDefault(d => d.FollowsSystemDefault);
        Assert.SkipWhen(speakers is null, "Needs the computer's Default output.");

        // Another program has the headphones' output to itself.
        using var other = new AudioEngine();
        using var otherMix = new MixEngine(other) { ExclusiveSpeakers = true };
        otherMix.SetOutputs(device, null);
        Assert.True(otherMix.Main.IsExclusive);

        _mix.SetOutputs(speakers, device);   // no exception: the room comes first
        Assert.Equal(speakers, _mix.Main.Device);
        Assert.Null(_mix.Cue.Device);
        Assert.Contains("in use by another program", _mix.CueRefusal);
    }

    [Fact]
    public void Headphones_stay_shared()
    {
        AudioDevice device = SpareDevice();
        _mix.SetOutputs(null, device);
        Assert.False(_mix.Cue.IsExclusive);
        Assert.True(Bass.GetDeviceInfo(device.Index, out DeviceInfo info) && info.IsInitialized);
    }
}
