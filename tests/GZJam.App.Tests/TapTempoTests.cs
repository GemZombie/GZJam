using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using GZJam.App.ViewModels;
using GZJam.App.Views;

namespace GZJam.App.Tests;

/// <summary>Tap tempo: tapping along to a deck to set the song's BPM (issue #11).</summary>
public sealed class TapTempoTests
{
    private static void Taps(TapTempo tempo, double bpm, int count, double start = 0, double wobble = 0)
    {
        for (int i = 0; i < count; i++)
            tempo.Tap(TimeSpan.FromSeconds(start + i * 60 / bpm + (i % 2 == 0 ? wobble : -wobble)));
    }

    [Fact]
    public void Even_taps_give_the_tempo_once_there_are_four()
    {
        var tempo = new TapTempo();
        Taps(tempo, 120, 3);
        Assert.Null(tempo.Bpm);

        tempo.Tap(TimeSpan.FromSeconds(1.5));
        Assert.Equal(120, tempo.Bpm!.Value, 3);
        Assert.False(tempo.IsSteady);   // four taps: not yet enough to trust

        Taps(tempo, 120, 8);   // from 0 s again: earlier than the last tap, so it starts over
        Assert.Equal(8, tempo.Count);
        Assert.True(tempo.IsSteady);
    }

    [Fact]
    public void A_tap_a_little_off_barely_moves_it_and_a_wobbly_hand_isnt_steady()
    {
        var tempo = new TapTempo();
        Taps(tempo, 180, 12);
        tempo.Tap(TimeSpan.FromSeconds(12 * 60.0 / 180 + 0.04));   // 40 ms late
        Assert.InRange(tempo.Bpm!.Value, 179, 180);
        Assert.True(tempo.IsSteady);

        var wobbly = new TapTempo();
        Taps(wobbly, 180, 12, wobble: 0.05);   // every tap 50 ms early or late
        Assert.InRange(wobbly.Bpm!.Value, 175, 185);
        Assert.False(wobbly.IsSteady);
    }

    [Fact]
    public void A_pause_starts_again()
    {
        var tempo = new TapTempo();
        Taps(tempo, 100, 6);
        Taps(tempo, 140, 2, start: 10);
        Assert.Equal(2, tempo.Count);
        Assert.Null(tempo.Bpm);
        Taps(tempo, 140, 4, start: 20);
        Assert.Equal(140, tempo.Bpm!.Value, 3);
    }

    [AvaloniaFact]
    public void Tapping_on_a_deck_with_its_tempo_changed_sets_the_songs_own_bpm()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var song = app.ToneTrack("Tune", seconds: 30);
        vm.LoadToDeckAsync(vm.Deck2!, song).GetAwaiter().GetResult();
        DeckViewModel deck = vm.Deck2!;
        deck.TempoPercent = -2.5;
        var view = app.Window.FindControl<DeckView>("Deck2View")!;
        var panel = view.FindControl<Border>("TapPanel")!;
        double height = view.Bounds.Height;

        deck.StartTapTempo();
        AppHarness.Settle(50);
        Assert.True(panel.IsEffectivelyVisible);
        Assert.Equal(height, view.Bounds.Height, 1);   // over the waveform: the deck stays its size
        Assert.False(deck.SetTappedBpmCommand.CanExecute(null));
        Assert.Equal("Tap along to the beat", deck.TapDetail);

        // The DJ hears the song 2.5% slower and taps that: 256.5 BPM heard.
        for (int i = 0; i < 9; i++)
            deck.Tap(TimeSpan.FromSeconds(100 + i * 60 / 256.5));
        Assert.Equal(263.1, deck.TappedBpm);
        Assert.Equal("9 taps · steady", deck.TapDetail);
        Assert.Equal("Deck at -2.5%: you're hearing 256.5", deck.TapTempoNote!.Replace('−', '-'));

        deck.SetTappedBpmCommand.Execute(null);
        Assert.False(deck.IsTapping);
        Assert.Equal(263.1, deck.Track!.ManualBpm);
        Assert.Equal(263.1, app.Database.GetManualBpm(song.Path));
        Assert.Equal(263.1, deck.TrackBpm);
        AppHarness.Settle(50);
        Assert.False(panel.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void The_pad_taps_by_mouse_or_space_and_escape_or_cancel_puts_it_away_unchanged()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var song = app.ToneTrack("Tune", seconds: 30);
        vm.LoadToDeckAsync(vm.Deck1!, song).GetAwaiter().GetResult();
        DeckViewModel deck = vm.Deck1!;
        var view = app.Window.FindControl<DeckView>("Deck1View")!;
        var pad = view.FindControl<Button>("TapPad")!;

        deck.StartTapTempo();
        AppHarness.Settle(50);
        Assert.True(pad.IsFocused);

        Point at = pad.TranslatePoint(new Point(pad.Bounds.Width / 2, pad.Bounds.Height / 2), app.Window)!.Value;
        app.Window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);   // counted on the press, not the release
        Assert.Equal("1 tap: keep going", deck.TapDetail);
        app.Window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
        app.Window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        app.Window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Assert.Equal("2 taps: keep going", deck.TapDetail);

        app.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.False(deck.IsTapping);
        Assert.Null(deck.Track!.ManualBpm);

        deck.StartTapTempo();
        Assert.Equal("Tap along to the beat", deck.TapDetail);   // a fresh start
        deck.CancelTapCommand.Execute(null);
        Assert.False(deck.IsTapping);
        Assert.Null(app.Database.GetManualBpm(song.Path));
    }

    [AvaloniaFact]
    public void Tap_tempo_is_on_the_decks_bpm_menu_not_the_librarys_and_a_new_song_puts_it_away()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        vm.LoadToDeckAsync(vm.Deck1!, app.ToneTrack("Tune", seconds: 30)).GetAwaiter().GetResult();
        DeckViewModel deck = vm.Deck1!;

        var deckMenu = new MenuItem();
        BpmMenu.Fill(deckMenu, vm, [deck.Track!], deck: deck);
        var item = deckMenu.Items.OfType<MenuItem>().Single(i => (string?)i.Header == "Tap tempo…");
        var libraryMenu = new MenuItem();
        BpmMenu.Fill(libraryMenu, vm, [deck.Track!]);
        Assert.DoesNotContain(libraryMenu.Items.OfType<MenuItem>(), i => (string?)i.Header == "Tap tempo…");

        item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Assert.True(deck.IsTapping);

        vm.LoadToDeckAsync(deck, app.ToneTrack("Another", seconds: 30)).GetAwaiter().GetResult();
        Assert.False(deck.IsTapping);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void With_four_decks_it_fits_on_the_short_waveform_without_growing_the_deck(bool touch)
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        vm.Settings.TouchMode = touch;
        vm.Settings.FourDecks = true;
        vm.LoadToDeckAsync(vm.Deck3!, app.ToneTrack("Tune", seconds: 30)).GetAwaiter().GetResult();
        var view = app.Window.FindControl<DeckView>("Deck3View")!;
        AppHarness.Settle(100);
        double height = view.Bounds.Height;

        vm.Deck3!.StartTapTempo();
        AppHarness.Settle(100);

        Assert.Equal(height, view.Bounds.Height, 1);
        var panel = view.FindControl<Border>("TapPanel")!;
        foreach (string name in new[] { "TapPad", "TapSetButton", "TapCancelButton" })
        {
            var button = view.FindControl<Button>(name)!;
            Rect inPanel = new(button.TranslatePoint(default, panel)!.Value, button.Bounds.Size);
            Assert.True(new Rect(panel.Bounds.Size).Contains(inPanel), $"{name} at {inPanel} in a panel {panel.Bounds.Size}");
        }
    }
}
