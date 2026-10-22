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
    public void A_busy_output_is_explained()
    {
        string message = AudioException.Busy("Cable Creation USB Audio").Message;
        Assert.StartsWith("Cable Creation USB Audio is in use", message);
    }
}
