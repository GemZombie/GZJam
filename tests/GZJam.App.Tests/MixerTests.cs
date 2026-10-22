using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GZJam.App.Controls;

namespace GZJam.App.Tests;

/// <summary>The mixer column's speaker controls: mono, volume, meter and limit light.</summary>
public sealed class MixerTests
{
    [AvaloniaFact]
    public void Speakers_and_headphones_start_in_mono_and_the_setting_is_remembered()
    {
        using var app = new AppHarness();
        var mixer = app.ViewModel.Mixer!;

        Assert.True(app.Mix.Main.Mono);
        Assert.True(app.Mix.Cue.Mono);   // what the room hears, in either ear
        Assert.True(app.Window.FindControl<LevelMeter>("MainMeter")!.IsMono);   // one bar

        app.ViewModel.Settings.Mono = false;

        Assert.False(app.Mix.Main.Mono);
        Assert.False(app.Mix.Cue.Mono);
        Assert.False(app.Settings.Mono);
        Assert.False(mixer.IsMono);
        Assert.False(app.Window.FindControl<LevelMeter>("MainMeter")!.IsMono);
    }

    [AvaloniaFact]
    public void Saved_volumes_are_applied()
    {
        using var app = new AppHarness(settings: new AppSettings { MainVolume = 50, CueVolume = 0 });

        Assert.Equal(0.25f, app.Mix.Main.Volume, 0.001f);   // half way on the slider: -12 dB
        Assert.Equal(0, app.Mix.Cue.Volume);
    }

    [AvaloniaFact]
    public void The_meter_shows_what_the_room_hears_and_the_volume_slider_turns_it_down()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var mixer = vm.Mixer!;
        vm.LoadToDeckAsync(vm.Deck1!, app.ToneTrack("Tone")).GetAwaiter().GetResult();
        vm.Deck1!.TogglePlayCommand.Execute(null);

        app.Play(0.3);
        app.Play(0.3);   // past the start-up ramp
        double full = mixer.LevelLeft;
        Assert.InRange(full, 0.85, 0.9);   // a 0.5 tone is -6 dB: 42 of the meter's 48 dB
        Assert.Equal(full, mixer.LevelRight, 3);
        Assert.False(mixer.IsLimiting);

        var fader = app.Window.FindControl<Fader>("MainVolumeFader")!;
        fader.Value = 50;
        Assert.Equal(50, app.Settings.MainVolume);
        for (int i = 0; i < 30; i++)
            app.Play(0.05);   // the meter falls back gradually

        Assert.InRange(mixer.LevelLeft, full - 12.5 / 48, full - 11.5 / 48);
    }
}
