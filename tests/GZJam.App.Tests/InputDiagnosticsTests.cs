using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.VisualTree;
using GZJam.App.ViewModels;
using GZJam.App.Views;

namespace GZJam.App.Tests;

/// <summary>
/// Real pointer input (mouse and touch) on the crossfader and play buttons, as a DJ would use
/// them, to make sure the room can always be switched and a deck can always be stopped.
/// </summary>
public sealed class InputDiagnosticsTests
{
    private static async Task<AppHarness> TwoDecksPlaying()
    {
        var app = new AppHarness();
        var vm = app.ViewModel;
        await vm.LoadToDeckAsync(vm.DeckA!, app.ToneTrack("One", 60));
        await vm.LoadToDeckAsync(vm.DeckB!, app.ToneTrack("Two", 60));
        vm.DeckA!.TogglePlayCommand.Execute(null);
        vm.DeckB!.TogglePlayCommand.Execute(null);
        app.Play(0.2);
        return app;
    }

    private static Point Centre(AppHarness app, Visual control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), app.Window)!.Value;

    private static Thumb CrossfaderThumb(AppHarness app) =>
        app.Window.FindControl<Slider>("Crossfader")!.GetVisualDescendants().OfType<Thumb>().First();

    /// <summary>Runs playback ticks for a while, as the app's 30 fps timer would.</summary>
    private static void RunTicks(AppHarness app, int milliseconds)
    {
        var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < until)
        {
            app.Play(0.033);
            Thread.Sleep(20);
        }
    }

    [AvaloniaFact]
    public async Task Mouse_drag_moves_the_crossfader_both_ways()
    {
        using var app = await TwoDecksPlaying();
        var slider = app.Window.FindControl<Slider>("Crossfader")!;
        Point thumb = Centre(app, CrossfaderThumb(app));
        Point right = slider.TranslatePoint(new Point(slider.Bounds.Width - 2, slider.Bounds.Height / 2), app.Window)!.Value;

        app.Window.MouseDown(thumb, MouseButton.Left);
        app.Window.MouseMove(new Point((thumb.X + right.X) / 2, thumb.Y));
        app.Window.MouseMove(right);
        app.Window.MouseUp(right, MouseButton.Left);
        RunTicks(app, 100);
        Assert.Equal(1, app.ViewModel.Mixer!.Crossfader, 1);

        Point left = slider.TranslatePoint(new Point(2, slider.Bounds.Height / 2), app.Window)!.Value;
        thumb = Centre(app, CrossfaderThumb(app));
        app.Window.MouseDown(thumb, MouseButton.Left);
        app.Window.MouseMove(left);
        app.Window.MouseUp(left, MouseButton.Left);
        RunTicks(app, 100);
        Assert.Equal(0, app.ViewModel.Mixer.Crossfader, 1);
    }

    [AvaloniaFact]
    public async Task Snap_buttons_reach_the_end_while_playback_ticks()
    {
        using var app = await TwoDecksPlaying();
        var mixer = app.ViewModel.Mixer!;

        mixer.SnapToBCommand.Execute(null);
        RunTicks(app, 500);
        Assert.Equal(1, mixer.Crossfader, 3);

        mixer.SnapToACommand.Execute(null);
        RunTicks(app, 500);
        Assert.Equal(0, mixer.Crossfader, 3);
    }

    [AvaloniaFact]
    public async Task Clicking_the_snap_button_reaches_the_end()
    {
        using var app = await TwoDecksPlaying();
        var b = app.Window.GetVisualDescendants().OfType<Button>().First(x => x.Classes.Contains("side") && (string?)x.Content == "B");
        Point p = Centre(app, b);

        app.Window.MouseDown(p, MouseButton.Left);
        app.Window.MouseUp(p, MouseButton.Left);
        RunTicks(app, 500);

        Assert.Equal(1, app.ViewModel.Mixer!.Crossfader, 3);
    }

    [AvaloniaFact]
    public async Task Touch_drag_moves_the_crossfader()
    {
        using var app = await TwoDecksPlaying();
        var slider = app.Window.FindControl<Slider>("Crossfader")!;
        Point thumb = Centre(app, CrossfaderThumb(app));
        Point right = slider.TranslatePoint(new Point(slider.Bounds.Width - 2, slider.Bounds.Height / 2), app.Window)!.Value;

        var touch = app.Window.TouchBegin(thumb, RawInputModifiers.None);
        app.Window.TouchMove(touch, new Point((thumb.X + right.X) / 2, thumb.Y), RawInputModifiers.None);
        app.Window.TouchMove(touch, right, RawInputModifiers.None);
        app.Window.TouchEnd(touch, right, RawInputModifiers.None);
        RunTicks(app, 100);

        Assert.Equal(1, app.ViewModel.Mixer!.Crossfader, 1);
    }

    private static void UseTwoTapPause(AppHarness app)
    {
        app.ViewModel.Settings.PauseChoice = app.ViewModel.Settings.PauseChoices.Single(c => c.Value == OnAirPause.TapTwice);
        app.ViewModel.Tick();
    }

    [AvaloniaFact]
    public async Task Mouse_click_pauses_the_on_air_deck_by_default()
    {
        using var app = await TwoDecksPlaying();
        var play = app.Window.FindControl<DeckView>("DeckAView")!.FindControl<Button>("PlayButton")!;
        Point p = Centre(app, play);
        Assert.True(app.ViewModel.DeckA!.IsOnAir);

        app.Window.MouseDown(p, MouseButton.Left);
        app.Window.MouseUp(p, MouseButton.Left);
        RunTicks(app, 50);

        Assert.False(app.ViewModel.DeckA.IsPlaying);
    }

    [AvaloniaFact]
    public async Task Two_mouse_clicks_pause_the_on_air_deck_in_two_tap_mode()
    {
        using var app = await TwoDecksPlaying();
        UseTwoTapPause(app);
        var play = app.Window.FindControl<DeckView>("DeckAView")!.FindControl<Button>("PlayButton")!;
        Point p = Centre(app, play);

        app.Window.MouseDown(p, MouseButton.Left);
        app.Window.MouseUp(p, MouseButton.Left);
        RunTicks(app, 150);
        Assert.True(app.ViewModel.DeckA!.IsPlaying);

        app.Window.MouseDown(p, MouseButton.Left);
        app.Window.MouseUp(p, MouseButton.Left);
        RunTicks(app, 50);
        Assert.False(app.ViewModel.DeckA.IsPlaying);
    }

    [AvaloniaFact]
    public async Task Mouse_hold_pauses_the_on_air_deck_in_two_tap_mode()
    {
        using var app = await TwoDecksPlaying();
        UseTwoTapPause(app);
        var play = app.Window.FindControl<DeckView>("DeckAView")!.FindControl<Button>("PlayButton")!;
        Point p = Centre(app, play);
        Assert.True(app.ViewModel.DeckA!.PauseNeedsConfirm);

        app.Window.MouseDown(p, MouseButton.Left);
        RunTicks(app, 800);
        app.Window.MouseUp(p, MouseButton.Left);
        RunTicks(app, 50);

        Assert.False(app.ViewModel.DeckA.IsPlaying);
    }

    [AvaloniaFact]
    public async Task Touch_hold_pauses_the_on_air_deck_in_two_tap_mode()
    {
        using var app = await TwoDecksPlaying();
        UseTwoTapPause(app);
        var play = app.Window.FindControl<DeckView>("DeckAView")!.FindControl<Button>("PlayButton")!;
        Point p = Centre(app, play);

        var touch = app.Window.TouchBegin(p, RawInputModifiers.None);
        RunTicks(app, 800);
        app.Window.TouchEnd(touch, p, RawInputModifiers.None);
        RunTicks(app, 50);

        Assert.False(app.ViewModel.DeckA!.IsPlaying);
    }

    [AvaloniaFact]
    public async Task Mouse_click_pauses_the_cue_deck()
    {
        using var app = await TwoDecksPlaying();
        var play = app.Window.FindControl<DeckView>("DeckBView")!.FindControl<Button>("PlayButton")!;
        Point p = Centre(app, play);

        app.Window.MouseDown(p, MouseButton.Left);
        app.Window.MouseUp(p, MouseButton.Left);
        RunTicks(app, 50);

        Assert.False(app.ViewModel.DeckB!.IsPlaying);
    }

    /// <summary>The reported bug: slide the crossfader to B, then pause A.</summary>
    [AvaloniaFact]
    public async Task After_sliding_to_B_a_click_pauses_A()
    {
        using var app = await TwoDecksPlaying();
        var slider = app.Window.FindControl<Slider>("Crossfader")!;
        Point thumb = Centre(app, CrossfaderThumb(app));
        Point right = slider.TranslatePoint(new Point(slider.Bounds.Width - 2, slider.Bounds.Height / 2), app.Window)!.Value;
        app.Window.MouseDown(thumb, MouseButton.Left);
        app.Window.MouseMove(right);
        app.Window.MouseUp(right, MouseButton.Left);
        RunTicks(app, 200);

        var playA = app.Window.FindControl<DeckView>("DeckAView")!.FindControl<Button>("PlayButton")!;
        Point p = Centre(app, playA);
        app.Window.MouseMove(p);
        app.Window.MouseDown(p, MouseButton.Left);
        app.Window.MouseUp(p, MouseButton.Left);
        RunTicks(app, 100);

        Assert.False(app.ViewModel.DeckA!.IsPlaying, $"A still playing; crossfader {app.ViewModel.Mixer!.Crossfader}, A on air: {app.ViewModel.DeckA.IsOnAir}, hint: {app.ViewModel.DeckA.Hint}");
        var (main, cue) = app.Mix.Render(TimeSpan.FromSeconds(0.3));
        Assert.True(cue < 0.01, $"A should be silent in the headphones; cue level {cue}");
    }

    [AvaloniaFact]
    public async Task Touch_tap_pauses_the_cue_deck()
    {
        using var app = await TwoDecksPlaying();
        var play = app.Window.FindControl<DeckView>("DeckBView")!.FindControl<Button>("PlayButton")!;
        Point p = Centre(app, play);

        var touch = app.Window.TouchBegin(p, RawInputModifiers.None);
        app.Window.TouchEnd(touch, p, RawInputModifiers.None);
        RunTicks(app, 50);

        Assert.False(app.ViewModel.DeckB!.IsPlaying);
    }
}
