using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using GZJam.App.Controls;
using GZJam.App.Views;

namespace GZJam.App.Tests;

/// <summary>The controls drawn like hardware: pitch fader, volume faders, LEDs.</summary>
public sealed class HardwareControlTests
{
    private static Point At(AppHarness app, Visual control, double x, double y) =>
        control.TranslatePoint(new Point(x, y), app.Window)!.Value;

    [AvaloniaFact]
    public void The_pitch_fader_moves_with_the_pointer_and_never_jumps()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        vm.LoadToDeckAsync(vm.Deck1!, app.ToneTrack("Song")).GetAwaiter().GetResult();
        AppHarness.Settle(100);   // the deck's layout changes once a track is loaded
        var fader = app.Window.FindControl<DeckView>("Deck1View")!.FindControl<PitchFader>("TempoFader")!;
        double y = fader.Bounds.Height / 3;

        // Pressing well to the right of the cap doesn't change the tempo...
        Point press = At(app, fader, fader.XOf(6), y);
        app.Window.MouseDown(press, MouseButton.Left);
        Assert.Equal(0, vm.Deck1!.TempoPercent);
        // ...dragging moves it by as much as the pointer moved: from 6% to 4% is -2%.
        app.Window.MouseMove(At(app, fader, fader.XOf(4), y));
        app.Window.MouseUp(At(app, fader, fader.XOf(4), y), MouseButton.Left);
        Assert.Equal(-2, vm.Deck1.TempoPercent, 1);

        // Double-tap: back to 0.
        Point cap = At(app, fader, fader.XOf(-2), y);
        app.Window.MouseDown(cap, MouseButton.Left);
        app.Window.MouseUp(cap, MouseButton.Left);
        app.Window.MouseDown(cap, MouseButton.Left);
        app.Window.MouseUp(cap, MouseButton.Left);
        Assert.Equal(0, vm.Deck1.TempoPercent);
    }

    [AvaloniaFact]
    public void The_volume_faders_show_how_far_down_they_are_in_db()
    {
        using var app = new AppHarness();
        var mixer = app.ViewModel.Mixer!;
        var fader = app.Window.FindControl<Fader>("CueVolumeFader")!;

        Assert.Equal("0 dB", mixer.CueVolumeText);
        fader.Value = 50;
        Assert.Equal("−12 dB", mixer.CueVolumeText);
        Assert.StartsWith("Phones (headphone) volume: −12 dB · keys: ", mixer.CueVolumeTip);   // and its keys (#132)
        fader.Value = 0;
        Assert.Equal("off", mixer.CueVolumeText);
    }

    [AvaloniaFact]
    public void Leds_show_cue_on_air_and_the_live_side()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        vm.LoadToDeckAsync(vm.Deck1!, app.ToneTrack("Song")).GetAwaiter().GetResult();
        var deck = app.Window.FindControl<DeckView>("Deck1View")!;
        Border CueLed() => (Border)((StackPanel)deck.FindControl<ToggleButton>("CueButton")!.Content!).Children[0];
        Border SideLed(string name) => (Border)((StackPanel)app.Window.FindControl<Button>(name)!.Content!).Children[0];

        Assert.DoesNotContain("on", CueLed().Classes);
        vm.Deck1!.IsCueOn = true;
        AppHarness.Settle(50);
        Assert.Contains("on", CueLed().Classes);

        Assert.Contains("on", SideLed("LeftSideButton").Classes);
        vm.Mixer!.SetNow(1);
        AppHarness.Settle(50);
        Assert.DoesNotContain("on", SideLed("LeftSideButton").Classes);
        Assert.Contains("on", SideLed("RightSideButton").Classes);
    }
}
