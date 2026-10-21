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
