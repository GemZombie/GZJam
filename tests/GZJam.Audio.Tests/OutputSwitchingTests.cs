using ManagedBass;

namespace GZJam.Audio.Tests;

/// <summary>
/// Changing outputs on this computer's real sound devices (silence plays; nothing is heard).
/// Skipped where there are no devices, as on CI machines.
/// </summary>
[Collection("BASS")]
public sealed class OutputSwitchingTests : IDisposable
{
    private readonly AudioEngine _engine = new();
    private readonly MixEngine _mix;

    public OutputSwitchingTests() => _mix = new MixEngine(_engine);

    public void Dispose()
    {
        _mix.Dispose();
        _engine.Dispose();
    }

    private static bool IsOpen(AudioDevice device) => Bass.GetDeviceInfo(device.Index, out DeviceInfo info) && info.IsInitialized;

    [Fact]
    public void Switching_outputs_lets_go_of_the_old_one()
    {
        var devices = _engine.GetOutputDevices();
        Assert.SkipWhen(devices.Count < 2, "Needs two output devices.");

        _mix.SetOutputs(devices[0], null);
        Assert.True(IsOpen(devices[0]));

        // On Linux the old one may be the very card wanted next (through PipeWire): it must be free.
        _mix.SetOutputs(devices[1], null);
        Assert.Equal(devices[1], _mix.Main.Device);
        Assert.True(IsOpen(devices[1]));
        Assert.False(IsOpen(devices[0]));
    }

    [Fact]
    public void An_output_that_wont_open_leaves_the_last_one_playing()
    {
        var devices = _engine.GetOutputDevices();
        Assert.SkipWhen(devices.Count < 1, "Needs an output device.");
        try
        {
            _mix.SetOutputs(devices[0], null);
        }
        catch (AudioException)
        {
            Assert.Skip("Needs an output device that opens (a Linux CI machine lists one with no sound card behind it).");
        }

        var missing = new AudioDevice(999, "Unplugged Interface", "nowhere", false);
        Assert.Throws<AudioException>(() => _mix.SetOutputs(missing, null));

        Assert.Equal(devices[0], _mix.Main.Device);
        Assert.True(IsOpen(devices[0]));
    }

    [Fact]
    public void Extra_buffering_lengthens_the_buffers_and_reopens_the_outputs_with_them()
    {
        int normal = Bass.GetConfig(Configuration.PlaybackBufferLength);
        _engine.ExtraBuffering = true;
        Assert.Equal(normal * 3, Bass.GetConfig(Configuration.PlaybackBufferLength));

        var devices = _engine.GetOutputDevices();
        if (devices.Count > 0)
        {
            try
            {
                _mix.SetOutputs(devices[0], null);
            }
            catch (AudioException)
            {
                Assert.Skip("Needs an output device that opens.");
            }
            Assert.Equal(devices[0], _mix.Main.Device);
            Assert.True(IsOpen(devices[0]));
        }

        _engine.ExtraBuffering = false;
        Assert.Equal(normal, Bass.GetConfig(Configuration.PlaybackBufferLength));
    }

    [Fact]
    public void A_booth_on_the_speakers_output_stays_off_and_says_why()
    {
        var devices = _engine.GetOutputDevices();
        Assert.SkipWhen(devices.Count < 1, "Needs an output device.");
        try
        {
            _mix.SetOutputs(devices[0], null, devices[0]);
        }
        catch (AudioException)
        {
            Assert.Skip("Needs an output device that opens.");
        }

        Assert.Equal(devices[0], _mix.Main.Device);
        Assert.Null(_mix.Booth.Device);
        Assert.Equal($"{devices[0].Name} is also the speaker output", _mix.BoothRefusal);
    }

    [Fact]
    public void A_booth_opens_beside_the_speakers_and_closes_again()
    {
        var devices = _engine.GetOutputDevices();
        Assert.SkipWhen(devices.Count < 2, "Needs two output devices.");
        try
        {
            _mix.SetOutputs(devices[0], null, devices[1]);
        }
        catch (AudioException)
        {
            Assert.Skip("Needs output devices that open.");
        }
        Assert.SkipWhen(_mix.BoothRefusal is not null, $"The second output won't open: {_mix.BoothRefusal}");

        Assert.Equal(devices[1], _mix.Booth.Device);
        Assert.True(IsOpen(devices[1]));

        _mix.SetOutputs(devices[0], null, null);
        Assert.Null(_mix.Booth.Device);
        Assert.False(IsOpen(devices[1]));
    }

    [Fact]
    public void A_busy_output_is_explained()
    {
        string message = AudioException.Busy("Cable Creation USB Audio").Message;
        Assert.StartsWith("Cable Creation USB Audio is in use", message);
    }
}
