using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using GZJam.App.Views;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>A song's EQ: set from a deck's EQ button, or from its right-click menu anywhere (#137), remembered for the song.</summary>
public sealed class EqAppTests
{
    [AvaloniaFact]
    public void Eq_is_remembered_for_the_song_on_any_deck()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var song = app.ToneTrack("Hissy transfer");
        vm.LoadToDeckAsync(vm.Deck1!, song).GetAwaiter().GetResult();
        Assert.False(vm.Deck1!.Eq.IsSet);

        vm.Deck1.Eq.High = -7.3;   // half-dB steps
        vm.Deck1.Eq.Low = -40;     // as far as it goes

        Assert.Equal(-7.5, vm.Deck1.Eq.High);
        Assert.Equal(TrackEq.MinDb, vm.Deck1.Eq.Low);
        Assert.Equal("−7.5 dB", vm.Deck1.Eq.HighText);
        Assert.True(vm.Deck1.Eq.IsSet);
        Assert.Equal((TrackEq.MinDb, 0.0, -7.5), vm.Deck1.Deck.Eq);
        Assert.Equal(new TrackEq(TrackEq.MinDb, 0, -7.5), app.Database.GetEq(song.Path));

        // Another song on the deck starts flat; the first brings its EQ wherever it goes.
        vm.LoadToDeckAsync(vm.Deck1, app.ToneTrack("Clean")).GetAwaiter().GetResult();
        Assert.False(vm.Deck1.Eq.IsSet);
        Assert.Equal((0.0, 0.0, 0.0), vm.Deck1.Deck.Eq);
        vm.LoadToDeckAsync(vm.Deck2!, song).GetAwaiter().GetResult();
        Assert.Equal(-7.5, vm.Deck2!.Eq.High);
        Assert.Equal((TrackEq.MinDb, 0.0, -7.5), vm.Deck2.Deck.Eq);

        // Flat forgets it.
        vm.Deck2.Eq.FlatCommand.Execute(null);
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

        vm.Deck1.Eq.High = -24;
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

        Assert.Equal(-6, vm.Deck2!.Eq.High);
        Assert.Equal(-12, vm.Deck2.Eq.Low);
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
        Assert.Equal("1 kHz", deck.Eq.MidHzText);
        Assert.False(deck.Eq.IsMidMoved);

        deck.Eq.MoveMidCommand.Execute("down");
        deck.Eq.MoveMidCommand.Execute("down");
        Assert.Equal("630 Hz", deck.Eq.MidHzText);
        Assert.Equal(630, deck.Deck.MidHz);
        Assert.True(deck.Eq.IsMidMoved);
        Assert.True(deck.Eq.IsSet);   // moving it alone counts as EQ set, so it's saved
        Assert.Equal(630, app.Database.GetEq(song.Path).MidHz);

        for (int i = 0; i < 20; i++)
            deck.Eq.MoveMidCommand.Execute("up");
        Assert.Equal("5 kHz", deck.Eq.MidHzText);   // as far as it goes

        deck.Eq.Mid = -3;
        deck.Eq.FlatBandCommand.Execute("midhz");     // tapping the frequency: back to 1 kHz, the cut stays
        Assert.Equal((1000.0, -3.0), (deck.Deck.MidHz, deck.Eq.Mid));

        deck.Eq.MoveMidCommand.Execute("up");
        vm.LoadToDeckAsync(vm.Deck2!, song).GetAwaiter().GetResult();
        Assert.Equal("1.25 kHz", vm.Deck2!.Eq.MidHzText);
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
        Assert.Equal("Off", deck.Eq.EvenText);
        deck.Eq.Even = 98;   // in steps of 5
        Assert.Equal(100, deck.Eq.Even);
        Assert.Equal("100", deck.Eq.EvenText);
        Assert.True(deck.Eq.IsSet);
        double after = Spread();

        Assert.InRange(before, 18, 22);
        Assert.True(after < before - 6, $"{before:0.0} dB apart before, {after:0.0} dB after; {deck.LevelText}; threshold {deck.Deck.Even.ThresholdDb:0.0}, ratio {deck.Deck.Even.Ratio}, make-up {deck.Deck.Even.MakeUpDb:0.0}");
        Assert.Equal(100, app.Database.GetEq(song.Path).Even);

        deck.Eq.FlatBandCommand.Execute("even");
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
        vm.Deck1!.Eq.High = -6;
        vm.Deck1.Eq.Even = 40;
        AppHarness.Settle(100);
        Assert.Equal("800 Hz", panel.FindControl<TextBlock>("MidHzText")!.Text);
        Assert.Equal(40, panel.FindControl<GZJam.App.Controls.Fader>("EqEvenFader")!.Value);
        app.Screenshot("eq-panel");

        panel.FindControl<Button>("FlatButton")!.Command!.Execute(null);
        Assert.False(vm.Deck1.Eq.IsSet);
        Assert.Equal(("1 kHz", "Off"), (vm.Deck1.Eq.MidHzText, vm.Deck1.Eq.EvenText));
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

    [AvaloniaFact]
    public void A_song_not_on_a_deck_gets_its_EQ_from_its_menu_heard_in_the_headphones_and_on_a_deck()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var song = app.ToneTrack("Hissy transfer", 60);

        // From the song list's menu: the deck's panel, for the song, with a key to preview it.
        var grid = app.Window.FindControl<DataGrid>("TrackGrid")!;
        var editor = app.Window.ShowSongEq(song, grid, PlacementMode.BottomEdgeAlignedLeft);
        AppHarness.Settle(150);
        Assert.Equal("EQ", editor.Heading);
        Assert.Equal("Hissy transfer", editor.Title);
        Assert.True(editor.CanPreview);
        app.Screenshot("song-eq");

        editor.LessHissCommand.Execute(null);
        editor.Even = 40;
        Assert.Equal(-6, vm.EqFor(song).High);   // saved for the song

        // Previewed: the headphones hear it with its EQ, and follow a change at once.
        editor.PreviewCommand!.Execute(null);
        Assert.Same(song, vm.Preview!.Track);
        Assert.Equal(-6, app.Mix.Preview.Eq.High);
        Assert.Equal(0.4, app.Mix.Preview.Even.Amount, 3);
        editor.Low = -3;
        Assert.Equal(-3, app.Mix.Preview.Eq.Low);
        editor.PreviewCommand.Execute(null);   // a second tap stops it
        Assert.Null(vm.Preview.Track);

        // Loaded later: the deck plays it so.
        vm.LoadToDeckAsync(vm.Deck1!, song).GetAwaiter().GetResult();
        Assert.Equal((-3.0, -6.0), (vm.Deck1!.Eq.Low, vm.Deck1.Eq.High));
        Assert.Equal(-6, vm.Deck1.Deck.Eq.High);

        // Changed from the queue's menu while it's on the deck: the deck follows.
        var again = vm.EqFor(song);
        again.Mid = 2;
        Assert.Equal(2, vm.Deck1.Eq.Mid);
        Assert.Equal(2, vm.Deck1.Deck.Eq.Mid);
        Assert.True(vm.Deck1.Eq.IsSet);

        // And the other way: set on the deck, a song panel opened after shows it.
        vm.Deck1.Eq.Gain = 1.5;
        Assert.Equal(1.5, vm.EqFor(song).Gain);
    }

    [AvaloniaFact]
    public void The_menus_offer_EQ_for_one_song()
    {
        using var app = new AppHarness();
        var grid = app.Window.FindControl<DataGrid>("TrackGrid")!;
        var menu = app.Window.FindControl<ContextMenu>("TrackMenu")!;
        grid.SelectedItems.Add(app.Library.Results[0]);
        menu.Open(grid);
        AppHarness.Settle(100);
        Assert.True(app.Window.FindControl<MenuItem>("TrackEqItem")!.IsEnabled);
        menu.Close();

        grid.SelectedItems.Add(app.Library.Results[1]);
        menu.Open(grid);
        AppHarness.Settle(100);
        Assert.False(app.Window.FindControl<MenuItem>("TrackEqItem")!.IsEnabled);   // one song at a time
        menu.Close();
    }
}
