using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using GZJam.App.Views;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>A deck's EQ: set from its EQ button, remembered for the song.</summary>
public sealed class EqAppTests
{
    [AvaloniaFact]
    public void Eq_is_remembered_for_the_song_on_any_deck()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var song = app.ToneTrack("Hissy transfer");
        vm.LoadToDeckAsync(vm.Deck1!, song).GetAwaiter().GetResult();
        Assert.False(vm.Deck1!.IsEqSet);

        vm.Deck1.EqHigh = -7.3;   // half-dB steps
        vm.Deck1.EqLow = -40;     // as far as it goes

        Assert.Equal(-7.5, vm.Deck1.EqHigh);
        Assert.Equal(TrackEq.MinDb, vm.Deck1.EqLow);
        Assert.Equal("−7.5 dB", vm.Deck1.EqHighText);
        Assert.True(vm.Deck1.IsEqSet);
        Assert.Equal((TrackEq.MinDb, 0.0, -7.5), vm.Deck1.Deck.Eq);
        Assert.Equal(new TrackEq(TrackEq.MinDb, 0, -7.5), app.Database.GetEq(song.Path));

        // Another song on the deck starts flat; the first brings its EQ wherever it goes.
        vm.LoadToDeckAsync(vm.Deck1, app.ToneTrack("Clean")).GetAwaiter().GetResult();
        Assert.False(vm.Deck1.IsEqSet);
        Assert.Equal((0.0, 0.0, 0.0), vm.Deck1.Deck.Eq);
        vm.LoadToDeckAsync(vm.Deck2!, song).GetAwaiter().GetResult();
        Assert.Equal(-7.5, vm.Deck2!.EqHigh);
        Assert.Equal((TrackEq.MinDb, 0.0, -7.5), vm.Deck2.Deck.Eq);

        // Flat forgets it.
        vm.Deck2.FlatEqCommand.Execute(null);
        Assert.Equal(TrackEq.Flat, app.Database.GetEq(song.Path));
        vm.Deck2.EjectCommand.Execute(null);
        Assert.Equal((0.0, 0.0, 0.0), vm.Deck2.Deck.Eq);
    }

    [AvaloniaFact]
    public void Eq_can_be_changed_while_the_room_is_listening()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        string hiss = GZJam.Testing.TestWav.WriteTone(Path.Combine(app.WorkDirectory, "hiss.wav"), TimeSpan.FromSeconds(10), frequency: 10000);
        var track = new Track { Path = hiss, Folder = "", FileName = "hiss.wav", Title = "Hiss" };
        vm.LoadToDeckAsync(vm.Deck1!, track).GetAwaiter().GetResult();
        vm.Deck1!.TogglePlayCommand.Execute(null);
        app.Play(0.3);
        float before = app.Play(0.3).Main;

        vm.Deck1.EqHigh = -24;
        app.Play(0.3);
        float after = app.Play(0.3).Main;

        Assert.True(vm.Deck1.IsOnAir);
        Assert.InRange(after, 0, before * 0.15f);
    }

    [AvaloniaFact]
    public void The_eq_button_opens_the_sliders_and_lights_up_when_set()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        vm.LoadToDeckAsync(vm.Deck2!, app.ToneTrack("Song")).GetAwaiter().GetResult();
        var button = app.Window.FindControl<DeckView>("Deck2View")!.FindControl<Button>("EqButton")!;
        Assert.True(button.IsEffectivelyVisible);
        Assert.DoesNotContain("on", button.Classes);

        button.Flyout!.ShowAt(button);
        AppHarness.Settle(200);
        var panel = ((Flyout)button.Flyout).Content as EqPanel;
        Assert.NotNull(panel);
        panel.FindControl<Button>("LessHissButton")!.Command!.Execute(null);
        // Press the low fader at the -12 dB line: the cap jumps there.
        var low = panel.FindControl<GZJam.App.Controls.Fader>("EqLowFader")!;
        var at = low.TranslatePoint(new Avalonia.Point(low.Bounds.Width / 2, low.YOf(-12)), app.Window)!.Value;
        app.Window.MouseDown(at, Avalonia.Input.MouseButton.Left);
        app.Window.MouseUp(at, Avalonia.Input.MouseButton.Left);
        AppHarness.Settle(100);

        Assert.Equal(-6, vm.Deck2!.EqHigh);
        Assert.Equal(-12, vm.Deck2.EqLow);
        Assert.Contains("on", button.Classes);
        button.Flyout.Hide();
    }

    [AvaloniaFact]
    public void The_mid_band_moves_a_third_of_an_octave_a_tap_and_is_remembered()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var song = app.ToneTrack("Boomy");
        vm.LoadToDeckAsync(vm.Deck1!, song).GetAwaiter().GetResult();
        var deck = vm.Deck1!;
        Assert.Equal("1 kHz", deck.EqMidHzText);
        Assert.False(deck.IsMidMoved);

        deck.MoveMidCommand.Execute("down");
        deck.MoveMidCommand.Execute("down");
        Assert.Equal("630 Hz", deck.EqMidHzText);
        Assert.Equal(630, deck.Deck.MidHz);
        Assert.True(deck.IsMidMoved);
        Assert.True(deck.IsEqSet);   // moving it alone counts as EQ set, so it's saved
        Assert.Equal(630, app.Database.GetEq(song.Path).MidHz);

        for (int i = 0; i < 20; i++)
            deck.MoveMidCommand.Execute("up");
        Assert.Equal("5 kHz", deck.EqMidHzText);   // as far as it goes

        deck.EqMid = -3;
        deck.FlatBandCommand.Execute("midhz");     // tapping the frequency: back to 1 kHz, the cut stays
        Assert.Equal((1000.0, -3.0), (deck.Deck.MidHz, deck.EqMid));

        deck.MoveMidCommand.Execute("up");
        vm.LoadToDeckAsync(vm.Deck2!, song).GetAwaiter().GetResult();
        Assert.Equal("1.25 kHz", vm.Deck2!.EqMidHzText);
        Assert.Equal(1250, vm.Deck2.Deck.MidHz);
    }

    [AvaloniaFact]
    public void Evening_out_brings_loud_and_quiet_passages_closer_and_is_remembered()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        // Ten seconds loud, then ten seconds 20 dB quieter.
        string path = GZJam.Testing.TestWav.WriteSections(Path.Combine(app.WorkDirectory, "wide.wav"),
            [(TimeSpan.FromSeconds(10), 0.5), (TimeSpan.FromSeconds(10), 0.05)]);
        var song = new Track { Path = path, Folder = "", FileName = "wide.wav", Title = "Wide" };
        var deck = vm.Deck1!;
        vm.LoadToDeckAsync(deck, song).GetAwaiter().GetResult();
        AppHarness.WaitFor(() => deck.LevelText.Contains("LUFS"));   // measured, so evening works around it
        vm.Mixer!.Crossfader = 0;

        double Spread()
        {
            deck.Deck.Seek(TimeSpan.FromSeconds(4));
            deck.TogglePlayCommand.Execute(null);
            app.Play(1);
            float loud = app.Play(0.5).Main;
            deck.Deck.Seek(TimeSpan.FromSeconds(14));
            app.Play(1);
            float quiet = app.Play(0.5).Main;
            deck.TogglePlayCommand.Execute(null);
            return 20 * Math.Log10(loud / quiet);
        }

        double before = Spread();
        Assert.Equal("Off", deck.EqEvenText);
        deck.EqEven = 98;   // in steps of 5
        Assert.Equal(100, deck.EqEven);
        Assert.Equal("100", deck.EqEvenText);
        Assert.True(deck.IsEqSet);
        double after = Spread();

        Assert.InRange(before, 18, 22);
        Assert.True(after < before - 6, $"{before:0.0} dB apart before, {after:0.0} dB after; {deck.LevelText}; threshold {deck.Deck.Even.ThresholdDb:0.0}, ratio {deck.Deck.Even.Ratio}, make-up {deck.Deck.Even.MakeUpDb:0.0}");
        Assert.Equal(100, app.Database.GetEq(song.Path).Even);

        deck.FlatBandCommand.Execute("even");
        Assert.InRange(Spread(), before - 1, before + 1);   // off is exactly as before
        Assert.Equal(TrackEq.Flat, app.Database.GetEq(song.Path));
    }

    [AvaloniaFact]
    public void The_panel_has_evening_out_and_the_mid_frequency_and_flat_resets_them()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        vm.LoadToDeckAsync(vm.Deck1!, app.ToneTrack("Old record")).GetAwaiter().GetResult();
        var button = app.Window.FindControl<DeckView>("Deck1View")!.FindControl<Button>("EqButton")!;
        button.Flyout!.ShowAt(button);
        AppHarness.Settle(200);
        var panel = (EqPanel)((Flyout)button.Flyout).Content!;

        panel.FindControl<Button>("MidDownButton")!.Command!.Execute("down");
        vm.Deck1!.EqHigh = -6;
        vm.Deck1.EqEven = 40;
        AppHarness.Settle(100);
        Assert.Equal("800 Hz", panel.FindControl<TextBlock>("MidHzText")!.Text);
        Assert.Equal(40, panel.FindControl<GZJam.App.Controls.Fader>("EqEvenFader")!.Value);
        app.Screenshot("eq-panel");

        panel.FindControl<Button>("FlatButton")!.Command!.Execute(null);
        Assert.False(vm.Deck1.IsEqSet);
        Assert.Equal(("1 kHz", "Off"), (vm.Deck1.EqMidHzText, vm.Deck1.EqEvenText));
        button.Flyout.Hide();
    }

    [AvaloniaFact]
    public void A_compact_deck_has_its_own_eq_button()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        vm.SetDeckCount(4);
        vm.LoadToDeckAsync(vm.Deck4!, app.ToneTrack("Song")).GetAwaiter().GetResult();
        AppHarness.Settle(100);
        var view = app.Window.FindControl<DeckView>("Deck4View")!;

        Assert.False(view.FindControl<Button>("EqButton")!.IsEffectivelyVisible);
        var compact = view.FindControl<Button>("CompactEqButton")!;
        Assert.True(compact.IsEffectivelyVisible);
        compact.Flyout!.ShowAt(compact);
        AppHarness.Settle(200);
        Assert.True(compact.Flyout.IsOpen);
        compact.Flyout.Hide();
    }
}
