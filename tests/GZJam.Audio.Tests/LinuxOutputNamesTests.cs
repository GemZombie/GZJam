using Output = GZJam.Audio.LinuxOutputNames.Output;

namespace GZJam.Audio.Tests;

/// <summary>
/// Clearer Linux output names, from what the kernel reports on a few kinds of machine. The first is
/// a real laptop (Intel Sound Open Firmware) with a USB interface; the others are typical ones.
/// </summary>
public class LinuxOutputNamesTests
{
    private static string? Name(string systemName, string driver, params (int Card, int Device, Output Output)[] outputs) =>
        LinuxOutputNames.For(systemName, driver,
            (card, device) => outputs.FirstOrDefault(o => o.Card == card && o.Device == device).Output);

    private static readonly (int, int, Output)[] SofLaptop =
    [
        (0, 0, new Output("sof-hda-dsp", "HDA Analog (*)", "", false, 5)),
        (0, 3, new Output("sof-hda-dsp", "HDMI1 (*)", "HDMI 1", false, 5)),
        (0, 31, new Output("sof-hda-dsp", "HDA Analog Deep Buffer (*)", "", false, 5)),
        (1, 0, new Output("Cable Creation", "USB Audio", "USB Audio", true, 1)),
    ];

    [Fact]
    public void A_laptop_with_intels_sound_firmware()
    {
        Assert.Equal("Default", Name("Default", "default", SofLaptop));
        Assert.Null(Name("PipeWire Sound Server", "pipewire", SofLaptop));
        Assert.Equal("Built-in: Headphones / Speakers", Name("sof-hda-dsp: ", "hw:0,0", SofLaptop));
        Assert.Equal("Built-in: HDMI 1", Name("sof-hda-dsp: HDMI 1", "hw:0,3", SofLaptop));
        Assert.Null(Name("sof-hda-dsp: ", "hw:0,31", SofLaptop));
        Assert.Equal("Cable Creation (USB)", Name("Cable Creation: USB Audio", "hw:1,0", SofLaptop));
    }

    [Fact]
    public void A_desktop_with_the_classic_intel_driver()
    {
        (int, int, Output)[] desktop =
        [
            (0, 0, new Output("HDA Intel PCH", "ALC892 Analog", "ALC892 Analog", false, 3)),
            (0, 1, new Output("HDA Intel PCH", "ALC892 Digital", "ALC892 Digital", false, 3)),
            (1, 3, new Output("HDA NVidia", "HDMI 0", "HDMI 0", false, 4)),
        ];
        Assert.Equal("Built-in: Headphones / Speakers", Name("HDA Intel PCH: ALC892 Analog", "hw:0,0", desktop));
        Assert.Equal("Built-in: Digital out", Name("HDA Intel PCH: ALC892 Digital", "hw:0,1", desktop));
        Assert.Equal("Built-in: HDMI 0", Name("HDA NVidia: HDMI 0", "hw:1,3", desktop));
    }

    [Fact]
    public void USB_interfaces_go_by_their_own_names()
    {
        (int, int, Output)[] usb =
        [
            (1, 0, new Output("Scarlett 2i2 USB", "USB Audio", "USB Audio", true, 1)),
            (2, 0, new Output("XONE:23C", "USB Audio", "USB Audio", true, 2)),
            (2, 1, new Output("XONE:23C", "USB Audio #1", "USB Audio #1", true, 2)),
        ];
        Assert.Equal("Scarlett 2i2 USB", Name("Scarlett 2i2 USB: USB Audio", "hw:1,0", usb));
        Assert.Equal("XONE:23C (USB)", Name("XONE:23C: USB Audio", "hw:2,0", usb));
        Assert.Equal("XONE:23C (USB): USB Audio #1", Name("XONE:23C: USB Audio #1", "hw:2,1", usb));
    }

    [Fact]
    public void Anything_unrecognised_keeps_its_name()
    {
        (int, int, Output)[] pi =
        [
            (0, 0, new Output("bcm2835 Headphones", "bcm2835 Headphones", "bcm2835 Headphones", false, 1)),
            (1, 0, new Output("vc4-hdmi-0", "MAI PCM i2s-hifi-0", "MAI PCM i2s-hifi-0", false, 1)),
        ];
        Assert.Equal("Built-in: bcm2835 Headphones", Name("bcm2835 Headphones: bcm2835 Headphones", "hw:0,0", pi));
        Assert.Equal("Built-in: HDMI", Name("vc4-hdmi-0: MAI PCM i2s-hifi-0", "hw:1,0", pi));

        Assert.Equal("JACK Audio Connection Kit", Name("JACK Audio Connection Kit", "jack", pi));
        Assert.Equal("Mystery: Out", Name("Mystery: Out", "hw:7,0", pi));   // the kernel had nothing on it
    }
}
