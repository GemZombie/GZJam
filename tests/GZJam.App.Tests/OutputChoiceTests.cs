using GZJam.App.ViewModels;
using GZJam.Audio;

namespace GZJam.App.Tests;

/// <summary>Which outputs open at startup (and after an unplug), from the saved choices and what's connected.</summary>
public sealed class OutputChoiceTests
{
    private static readonly AudioDevice Tv = new(2, "HISENSE", "{tv}", false);
    private static readonly AudioDevice Laptop = new(5, "Speaker (Realtek(R) Audio)", "{laptop}", true);
    private static readonly AudioDevice Usb = new(3, "Speakers (USB)", "{usb}", false);

    private static (AudioDevice? Main, AudioDevice? Cue, string? Notice) Choose(AudioDevice? main, AudioDevice? cue, AudioDevice? fallback) =>
        MainWindowViewModel.ChooseOutputs(Tv.Preference, Laptop.Preference, main, cue, fallback);

    [Fact]
    public void Both_connected_open_as_saved()
    {
        var (main, cue, notice) = Choose(Tv, Laptop, Laptop);
        Assert.Equal(Tv, main);
        Assert.Equal(Laptop, cue);
        Assert.Null(notice);
    }

    [Fact]
    public void Speakers_unplugged_and_falling_back_to_the_headphones_output_leave_the_headphones_off()
    {
        // Taken mobile: the TV is gone and the laptop's own output, the headphones, is the default.
        var (main, cue, notice) = Choose(null, Laptop, Laptop);
        Assert.Equal(Laptop, main);
        Assert.Null(cue);   // a preview would otherwise play in the room
        Assert.Equal("Speaker output \"HISENSE\" isn't connected; using Speaker (Realtek(R) Audio). " +
                     "Headphones are off: they'd play in the room. Choose them in Settings › Sound.", notice);
    }

    [Fact]
    public void Speakers_unplugged_falling_back_elsewhere_keep_the_headphones()
    {
        var (main, cue, notice) = Choose(null, Laptop, Usb);
        Assert.Equal(Usb, main);
        Assert.Equal(Laptop, cue);
        Assert.Equal("Speaker output \"HISENSE\" isn't connected; using Speakers (USB).", notice);
    }

    [Fact]
    public void Headphones_chosen_on_the_speakers_output_on_purpose_are_kept()
    {
        // A split output: the speakers on its left, the headphones on its right.
        var (main, cue, notice) = MainWindowViewModel.ChooseOutputs(Laptop.Preference, Laptop.Preference, Laptop, Laptop, Laptop);
        Assert.Equal(Laptop, main);
        Assert.Equal(Laptop, cue);
        Assert.Null(notice);
    }
}
