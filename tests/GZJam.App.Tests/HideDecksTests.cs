using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using GZJam.App.Controls;
using GZJam.App.ViewModels;

namespace GZJam.App.Tests;

/// <summary>Hidden decks (#164): one line for what the room hears and what's next, for the most songs.</summary>
public sealed class HideDecksTests
{
    private static readonly RawInputModifiers Command = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    private static T Find<T>(AppHarness app, string name) where T : Control => app.Window.FindControl<T>(name)!;

    private static Point Grip(AppHarness app)
    {
        var grip = Find<Control>(app, "DeckGrip");
        return grip.TranslatePoint(new Point(grip.Bounds.Width / 2, grip.Bounds.Height / 2), app.Window)!.Value;
    }

    [AvaloniaFact]
    public void Ctrl_Shift_D_hides_the_decks_for_more_songs_and_brings_them_back()
    {
        using var app = new AppHarness();
        double songs = Find<Control>(app, "TrackGrid").Bounds.Height;

        app.Window.KeyPress(Key.D, Command | RawInputModifiers.Shift, PhysicalKey.D, "D");
        AppHarness.Settle(150);
        Assert.True(app.Settings.DecksHidden);   // remembered
        Assert.False(Find<Control>(app, "DecksRow").IsVisible);
        Assert.True(Find<Control>(app, "AirLine").IsVisible);
        Assert.True(Find<Control>(app, "TrackGrid").Bounds.Height > songs + 200, "the library gets the room");
        Assert.True(Find<Control>(app, "AirLine").Bounds.Height < 70, "one line");
        Assert.Equal("Nothing on air", Find<TextBlock>(app, "AirTitle").Text);
        Assert.False(Find<Button>(app, "AirPlayButton").IsEffectivelyEnabled);

        app.Window.KeyPress(Key.D, Command | RawInputModifiers.Shift, PhysicalKey.D, "D");
        AppHarness.Settle(150);
        Assert.False(app.ViewModel.DecksHidden);
        Assert.True(Find<Control>(app, "DecksRow").IsVisible);
        Assert.False(app.ViewModel.CompactDecks);   // as they were

        // Compact, then hidden: Ctrl+D (or the top bar's key) brings them back compact.
        app.ViewModel.CompactDecks = true;
        app.ViewModel.DecksHidden = true;
        Assert.Equal(DeckSize.Hidden, app.ViewModel.DeckSize);
        app.Window.KeyPress(Key.D, Command, PhysicalKey.D, "d");
        AppHarness.Settle(150);
        Assert.False(app.ViewModel.DecksHidden);
        Assert.True(app.ViewModel.CompactDecks);
        Assert.True(Find<Control>(app, "MixerPanel").Bounds.Height > 50, "the mixer is back at its size");
    }

    [AvaloniaFact]
    public void The_line_shows_the_song_on_air_its_bar_and_whats_next_and_pauses_with_two_taps()
    {
        using var app = new AppHarness();
        app.Settings.OnAirPause = OnAirPause.TapTwice;
        var vm = app.ViewModel;
        vm.LoadToDeckAsync(vm.Deck1!, app.ToneTrack("Jump Steady", 60)).GetAwaiter().GetResult();
        vm.Mixer!.Crossfader = 0;
        vm.DecksHidden = true;
        app.Play(0.05);
        var line = vm.AirLine!;

        // Loaded on the room's side, not playing yet: the key starts it.
        Assert.Same(vm.Deck1, line.Now);
        Assert.Equal("NEXT", line.NextFrom);
        Assert.False(Find<AirBar>(app, "AirLineBar").IsVisible);
        vm.AddToQueue([app.Library.Results[0]]);
        app.Play(0.05);
        Assert.Equal("NEXT · QUEUE", line.NextFrom);
        Assert.StartsWith(app.Library.Results[0].DisplayTitle, line.NextText);

        Find<Button>(app, "AirPlayButton").Command!.Execute(null);
        app.Play(0.1);
        Assert.True(vm.Deck1!.IsOnAir);
        Assert.Equal("Jump Steady", Find<TextBlock>(app, "AirTitle").Text);
        Assert.StartsWith("-0:5", Find<TextBlock>(app, "AirTimeLeft").Text);
        Assert.True(Find<AirBar>(app, "AirLineBar").IsVisible);
        vm.Deck1.Deck.Seek(TimeSpan.FromSeconds(30));
        app.Play(0.1);
        Assert.InRange(Find<AirBar>(app, "AirLineBar").Progress, 0.45, 0.55);

        // The other deck loaded: that's next.
        vm.LoadToDeckAsync(vm.Deck2!, app.ToneTrack("Blue Lou", 60)).GetAwaiter().GetResult();
        app.Play(0.05);
        Assert.Equal("NEXT · DECK 2", line.NextFrom);
        Assert.Equal("Blue Lou · Test Band", line.NextText);
        app.Screenshot("hidden-decks");

        // Two taps on air, as on the deck, with the hint on the line.
        var play = Find<Button>(app, "AirPlayButton");
        play.Command!.Execute(null);
        app.Play(0.05);
        Assert.True(vm.Deck1.IsPlaying);
        Assert.Equal("Tap again to pause", vm.Deck1.Hint);
        play.Command!.Execute(null);
        app.Play(0.05);
        Assert.False(vm.Deck1.IsPlaying);
    }

    [AvaloniaFact]
    public void The_switch_in_the_top_bar_goes_to_any_size_and_Settings_follows()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        Button Key(string name) => Find<Button>(app, name);
        Assert.Contains("on", Key("FullDecksButton").Classes);

        Key("HiddenDecksButton").Command!.Execute(Key("HiddenDecksButton").CommandParameter);   // straight from full
        AppHarness.Settle(100);
        app.Screenshot("deck-size-switch-hidden");
        Assert.True(vm.DecksHidden);
        Assert.Contains("on", Key("HiddenDecksButton").Classes);
        Assert.DoesNotContain("on", Key("FullDecksButton").Classes);
        Assert.Equal(DeckSize.Hidden, vm.Settings.DeckSizeChoice.Value);

        Key("CompactDecksButton").Command!.Execute(Key("CompactDecksButton").CommandParameter);
        AppHarness.Settle(100);
        Assert.False(vm.DecksHidden);
        Assert.True(vm.CompactDecks);
        Assert.Contains("on", Key("CompactDecksButton").Classes);
        Assert.Contains("+D", ToolTip.GetTip(Key("CompactDecksButton")) as string);
        Assert.Contains("+Shift+D", ToolTip.GetTip(Key("HiddenDecksButton")) as string);

        Key("FullDecksButton").Command!.Execute(Key("FullDecksButton").CommandParameter);
        AppHarness.Settle(100);
        Assert.Equal(DeckSize.Full, vm.DeckSize);
        Assert.False(app.Settings.CompactDecks || app.Settings.DecksHidden);

        // Settings › Decks › Deck size, the same three.
        vm.Settings.DeckSizeChoice = vm.Settings.DeckSizeChoices.First(c => c.Value == DeckSize.Hidden);
        AppHarness.Settle(100);
        Assert.True(vm.DecksHidden);
        Assert.Contains("on", Key("HiddenDecksButton").Classes);
    }

    [AvaloniaFact]
    public void The_grip_follows_the_finger_a_step_or_two_and_a_tap_brings_hidden_decks_back()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;

        var at = Grip(app);
        app.MouseDrag(at, at - new Point(0, 40));   // a little: compact
        Assert.Equal(DeckSize.Compact, vm.DeckSize);

        at = Grip(app);
        app.MouseDrag(at, at + new Point(0, 40));
        Assert.Equal(DeckSize.Full, vm.DeckSize);

        at = Grip(app);
        app.MouseDrag(at, at - new Point(0, 120));   // further: hidden, in one drag
        Assert.Equal(DeckSize.Hidden, vm.DeckSize);

        at = Grip(app);
        app.MouseDrag(at, at + new Point(0, 40));   // a little down: back as they were
        Assert.False(vm.DecksHidden);
        Assert.True(vm.CompactDecks);

        vm.DecksHidden = true;
        AppHarness.Settle(100);
        at = Grip(app);
        app.Window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
        app.Window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
        AppHarness.Settle(100);
        Assert.False(vm.DecksHidden);
        Assert.True(vm.CompactDecks);
    }
}
