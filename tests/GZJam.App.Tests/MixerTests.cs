using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GZJam.App.Controls;
using GZJam.App.Views;

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
    public void Each_deck_has_a_meter_beside_the_mixer_that_shows_it_out_of_the_room_too()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var meter1 = app.Window.FindControl<DeckView>("Deck1View")!.FindControl<LevelMeter>("DeckMeter")!;
        var meter2 = app.Window.FindControl<DeckView>("Deck2View")!.FindControl<LevelMeter>("DeckMeter")!;
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Right, meter1.HorizontalAlignment);   // the edge nearest the mixer
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Left, meter2.HorizontalAlignment);
        Assert.False(meter1.ShowScale);

        vm.LoadToDeckAsync(vm.Deck2!, app.ToneTrack("Tone")).GetAwaiter().GetResult();
        vm.Mixer!.Crossfader = 0;   // the room hears Deck 1, which is empty
        vm.Deck2!.TogglePlayCommand.Execute(null);
        app.Play(0.3);
        app.Play(0.3);

        Assert.InRange(vm.Deck2.Level, 0.7, 1);
        Assert.Equal(vm.Deck2.Level, meter2.Left);
        Assert.Equal(0, vm.Deck1!.Level);
        Assert.InRange(vm.Mixer.LevelLeft, 0, 0.1);   // nothing in the room

        vm.Deck2.TogglePlayCommand.Execute(null);   // paused: the meter falls back
        for (int i = 0; i < 60; i++)
            app.Play(0.05);
        Assert.Equal(0, vm.Deck2.Level);
    }

    [AvaloniaFact]
    public void The_phones_mix_lets_the_headphones_hear_the_room_and_is_remembered()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var fader = app.Window.FindControl<CrossfaderControl>("PhonesMixFader")!;
        vm.LoadToDeckAsync(vm.Deck1!, app.ToneTrack("On air")).GetAwaiter().GetResult();
        vm.Mixer!.Crossfader = 0;   // the room hears Deck 1, which isn't cued
        vm.Deck1!.TogglePlayCommand.Execute(null);
        app.Play(0.3);

        Assert.Equal(0, fader.Value);   // all the way to CUE: as before
        Assert.InRange(app.Play(0.3).Cue, 0, 0.01);
        Assert.Contains("what's cued", ToolTip.GetTip((Control)fader.Parent!) as string, StringComparison.Ordinal);

        fader.Value = 1;   // as a drag or the arrow keys would
        app.Play(0.1);
        Assert.Equal(1, vm.Mixer.PhonesMix);
        Assert.Equal(1, app.Mix.PhonesMix);
        Assert.Equal(1, app.Settings.PhonesMix);
        Assert.InRange(app.Play(0.3).Cue, 0.4, 0.6);   // the room, in the headphones
        Assert.StartsWith("Phones mix: the room only", ToolTip.GetTip((Control)fader.Parent!) as string, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void A_saved_phones_mix_is_applied()
    {
        using var app = new AppHarness(settings: new AppSettings { PhonesMix = 0.5 });
        Assert.Equal(0.5, app.Mix.PhonesMix);
        Assert.Equal(0.5, app.Window.FindControl<CrossfaderControl>("PhonesMixFader")!.Value);
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

        // The meter's level: the highest of a few readings. GZJam's own timer can tick between
        // the test's renders, find no new sound, and let the meter fall a step (it did once on
        // CI's Mac).
        double Level()
        {
            double level = 0;
            for (int i = 0; i < 4; i++)
            {
                app.Play(0.05);
                level = Math.Max(level, mixer.LevelLeft);
            }
            return level;
        }

        app.Play(0.6);   // past the start-up ramp
        double full = Level();
        Assert.InRange(full, 0.85, 0.9);   // a 0.5 tone is -6 dB: 42 of the meter's 48 dB
        Assert.Equal(mixer.LevelLeft, mixer.LevelRight, 3);   // mono: both sides alike
        Assert.False(mixer.IsLimiting);

        var fader = app.Window.FindControl<Fader>("MainVolumeFader")!;
        fader.Value = 50;
        Assert.Equal(50, app.Settings.MainVolume);
        for (int i = 0; i < 30; i++)
            app.Play(0.05);   // the meter falls back gradually

        Assert.InRange(Level(), full - 12.5 / 48, full - 11.5 / 48);
    }
}
