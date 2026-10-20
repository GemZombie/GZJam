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
        await vm.LoadToDeckAsync(vm.Deck1!, app.ToneTrack("One", 60));
        await vm.LoadToDeckAsync(vm.Deck2!, app.ToneTrack("Two", 60));
        vm.Deck1!.TogglePlayCommand.Execute(null);
        vm.Deck2!.TogglePlayCommand.Execute(null);
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

        mixer.SnapTo2Command.Execute(null);
        RunTicks(app, 500);
        Assert.Equal(1, mixer.Crossfader, 3);

        mixer.SnapTo1Command.Execute(null);
        RunTicks(app, 500);
        Assert.Equal(0, mixer.Crossfader, 3);
    }

    [AvaloniaFact]
    public async Task Clicking_the_snap_button_reaches_the_end()
    {
        using var app = await TwoDecksPlaying();
        var b = app.Window.GetVisualDescendants().OfType<Button>().First(x => x.Classes.Contains("side") && (string?)x.Content == "2");
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
        var play = app.Window.FindControl<DeckView>("Deck1View")!.FindControl<Button>("PlayButton")!;
        Point p = Centre(app, play);
        Assert.True(app.ViewModel.Deck1!.IsOnAir);

        app.Window.MouseDown(p, MouseButton.Left);
        app.Window.MouseUp(p, MouseButton.Left);
        RunTicks(app, 50);

        Assert.False(app.ViewModel.Deck1.IsPlaying);
    }

    [AvaloniaFact]
    public async Task Two_mouse_clicks_pause_the_on_air_deck_in_two_tap_mode()
    {
        using var app = await TwoDecksPlaying();
        UseTwoTapPause(app);
        var play = app.Window.FindControl<DeckView>("Deck1View")!.FindControl<Button>("PlayButton")!;
        Point p = Centre(app, play);

        app.Window.MouseDown(p, MouseButton.Left);
        app.Window.MouseUp(p, MouseButton.Left);
        RunTicks(app, 150);
        Assert.True(app.ViewModel.Deck1!.IsPlaying);

        app.Window.MouseDown(p, MouseButton.Left);
        app.Window.MouseUp(p, MouseButton.Left);
        RunTicks(app, 50);
        Assert.False(app.ViewModel.Deck1.IsPlaying);
    }

    [AvaloniaFact]
    public async Task Mouse_hold_pauses_the_on_air_deck_in_two_tap_mode()
    {
        using var app = await TwoDecksPlaying();
        UseTwoTapPause(app);
        var play = app.Window.FindControl<DeckView>("Deck1View")!.FindControl<Button>("PlayButton")!;
        Point p = Centre(app, play);
        Assert.True(app.ViewModel.Deck1!.PauseNeedsConfirm);

        app.Window.MouseDown(p, MouseButton.Left);
        RunTicks(app, 800);
        app.Window.MouseUp(p, MouseButton.Left);
        RunTicks(app, 50);

        Assert.False(app.ViewModel.Deck1.IsPlaying);
    }

    [AvaloniaFact]
    public async Task Touch_hold_pauses_the_on_air_deck_in_two_tap_mode()
    {
        using var app = await TwoDecksPlaying();
        UseTwoTapPause(app);
        var play = app.Window.FindControl<DeckView>("Deck1View")!.FindControl<Button>("PlayButton")!;
        Point p = Centre(app, play);

        var touch = app.Window.TouchBegin(p, RawInputModifiers.None);
        RunTicks(app, 800);
        app.Window.TouchEnd(touch, p, RawInputModifiers.None);
        RunTicks(app, 50);

        Assert.False(app.ViewModel.Deck1!.IsPlaying);
    }

    [AvaloniaFact]
    public async Task Mouse_click_pauses_the_cue_deck()
    {
        using var app = await TwoDecksPlaying();
        var play = app.Window.FindControl<DeckView>("Deck2View")!.FindControl<Button>("PlayButton")!;
        Point p = Centre(app, play);

        app.Window.MouseDown(p, MouseButton.Left);
        app.Window.MouseUp(p, MouseButton.Left);
        RunTicks(app, 50);

        Assert.False(app.ViewModel.Deck2!.IsPlaying);
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

        var playA = app.Window.FindControl<DeckView>("Deck1View")!.FindControl<Button>("PlayButton")!;
        Point p = Centre(app, playA);
        app.Window.MouseMove(p);
        app.Window.MouseDown(p, MouseButton.Left);
        app.Window.MouseUp(p, MouseButton.Left);
        RunTicks(app, 100);

        Assert.False(app.ViewModel.Deck1!.IsPlaying, $"A still playing; crossfader {app.ViewModel.Mixer!.Crossfader}, A on air: {app.ViewModel.Deck1.IsOnAir}, hint: {app.ViewModel.Deck1.Hint}");
        var (main, cue) = app.Mix.Render(TimeSpan.FromSeconds(0.3));
        Assert.True(cue < 0.01, $"A should be silent in the headphones; cue level {cue}");
    }

    /// <summary>Reported: the position bar only worked while paused. Deck 2 is playing in the headphones only.</summary>
    [AvaloniaFact]
    public async Task Clicking_the_position_bar_seeks_a_playing_deck_that_is_out_of_the_room()
    {
        using var app = await TwoDecksPlaying();
        var deck2 = app.ViewModel.Deck2!;
        deck2.IsCueOn = true;
        RunTicks(app, 100);
        Assert.True(deck2.IsPlaying);
        Assert.False(deck2.IsInRoom);
        Assert.True(deck2.CanSeek);

        var seek = app.Window.FindControl<DeckView>("Deck2View")!.FindControl<Avalonia.Controls.Control>("Seek")!;
        Point at75 = seek.TranslatePoint(new Point(seek.Bounds.Width * 0.75, seek.Bounds.Height / 2), app.Window)!.Value;
        app.Window.MouseMove(at75);
        app.Window.MouseDown(at75, MouseButton.Left);
        app.Window.MouseUp(at75, MouseButton.Left);
        RunTicks(app, 100);

        Assert.InRange(deck2.Deck.Position.TotalSeconds, 44, 46.5);   // 75% of 60 s
        Assert.True(deck2.IsPlaying);
    }

    [AvaloniaFact]
    public async Task Touch_tap_pauses_the_cue_deck()
    {
        using var app = await TwoDecksPlaying();
        var play = app.Window.FindControl<DeckView>("Deck2View")!.FindControl<Button>("PlayButton")!;
        Point p = Centre(app, play);

        var touch = app.Window.TouchBegin(p, RawInputModifiers.None);
        app.Window.TouchEnd(touch, p, RawInputModifiers.None);
        RunTicks(app, 50);

        Assert.False(app.ViewModel.Deck2!.IsPlaying);
    }
}
