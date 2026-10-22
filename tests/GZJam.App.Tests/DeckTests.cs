using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using GZJam.App.ViewModels;
using GZJam.App.Views;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>Decks, crossfader, deck protection and headphone preview, through the real window and an offline mixer.</summary>
public sealed class DeckTests
{
    private static Point Centre(AppHarness app, string deckView)
    {
        var view = app.Window.FindControl<DeckView>(deckView)!;
        return view.TranslatePoint(new Point(view.Bounds.Width / 2, view.Bounds.Height / 2), app.Window)!.Value;
    }

    /// <summary>Drags a track onto a deck and lets go (or only holds it over the deck).</summary>
    private static void Drop(AppHarness app, string deckView, Track track, bool release = true) =>
        app.DragTracks(new MainWindow.TrackDrag([track], null), Centre(app, deckView), release);

    [AvaloniaFact]
    public void Dropping_a_track_on_a_deck_loads_it()
    {
        using var app = new AppHarness();
        Track tone = app.ToneTrack("Tain't What You Do");

        Drop(app, "Deck2View", tone);

        DeckViewModel b = app.ViewModel.Deck2!;
        Assert.Equal(tone, b.Track);
        Assert.Equal("Tain't What You Do", b.Title);
        Assert.Equal("", b.Status);                 // loaded, not playing
        Assert.False(b.IsCueOn);
        Assert.Equal("-0:20", b.ReadoutText);
        Assert.False(b.IsPlaying);
    }

    [AvaloniaFact]
    public void The_crossfader_picks_the_room_and_cue_buttons_pick_the_headphones()
    {
        using var app = new AppHarness();
        var (a, b) = (app.ViewModel.Deck1!, app.ViewModel.Deck2!);
        Drop(app, "Deck1View", app.ToneTrack("Room"));
        Drop(app, "Deck2View", app.ToneTrack("Next"));

        a.TogglePlayCommand.Execute(null);
        var (main, cue) = app.Play(0.3);
        Assert.True(main > 0.4 && cue < 0.01, $"A in the room only: main {main}, cue {cue}");
        Assert.Equal("ON AIR", a.Status);

        // Cue B: it plays in the headphones while the room still hears only A.
        b.ToggleCueCommand.Execute(null);
        b.TogglePlayCommand.Execute(null);
        a.TogglePlayCommand.Execute(null);   // pause A to measure B alone
        (main, cue) = app.Play(0.3);
        Assert.True(main < 0.01 && cue > 0.4, $"B in the headphones only: main {main}, cue {cue}");
        Assert.Equal("OFF AIR", b.Status);

        // Moving the crossfader to B puts it in the room; the headphones are unchanged.
        app.ViewModel.Mixer!.Crossfader = 1;
        app.Play(0.1);
        (main, cue) = app.Play(0.3);
        Assert.True(main > 0.4 && cue > 0.4, $"B in both: main {main}, cue {cue}");
        Assert.Equal("ON AIR", b.Status);
        Assert.True(b.IsCueOn);
    }

    [AvaloniaFact]
    public void Snapping_the_crossfader_swaps_which_deck_is_live()
    {
        using var app = new AppHarness();
        var (a, b) = (app.ViewModel.Deck1!, app.ViewModel.Deck2!);
        Drop(app, "Deck1View", app.ToneTrack("One"));
        Drop(app, "Deck2View", app.ToneTrack("Two"));
        a.TogglePlayCommand.Execute(null);
        b.TogglePlayCommand.Execute(null);
        app.Play(0.2);
        Assert.True(a.IsOnAir);

        app.ViewModel.Mixer!.SnapTo2Command.Execute(null);
        AppHarness.Settle(400);   // the snap glides over 300 ms
        app.ViewModel.Tick();

        Assert.Equal(1, app.ViewModel.Mixer.Crossfader, 3);
        Assert.True(b.IsOnAir);
        Assert.Equal("OFF AIR", a.Status);
        Assert.Equal("2 · LIVE", app.ViewModel.Mixer.LiveText);

        app.ViewModel.Mixer.Crossfader = 0.5;
        Assert.Equal("BLEND", app.ViewModel.Mixer.LiveText);
        app.ViewModel.Tick();
        Assert.True(a.IsOnAir && b.IsOnAir);
    }

    [AvaloniaFact]
    public void A_playing_deck_refuses_a_new_track_by_default()
    {
        using var app = new AppHarness();
        DeckViewModel a = app.ViewModel.Deck1!;
        Track first = app.ToneTrack("First");
        Drop(app, "Deck1View", first);
        a.TogglePlayCommand.Execute(null);
        app.Play(0.2);

        // Dragging over shows the deck refusing; dropping doesn't replace the track.
        Drop(app, "Deck1View", app.ToneTrack("Second"), release: false);
        Assert.Contains("drop-blocked", app.Window.FindControl<DeckView>("Deck1View")!.FindControl<Border>("Frame")!.Classes);
        app.Window.EndDrag();
        Drop(app, "Deck1View", app.ToneTrack("Second"));

        Assert.Equal(first, a.Track);
        Assert.True(a.IsPlaying);
        Assert.Equal("Deck 1 is playing. Pause it first to load a new track.", app.ViewModel.Notice);
        Assert.False(app.ViewModel.CanLoad(a));

        // Paused, the same deck takes the track.
        a.HoldPause();
        Drop(app, "Deck1View", app.ToneTrack("Second"));
        Assert.Equal("Second", a.Title);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ask_mode_loads_only_when_confirmed(bool confirm)
    {
        using var app = new AppHarness();
        app.Settings.LoadOntoPlayingDeck = PlayingDeckLoad.Ask;
        DeckViewModel a = app.ViewModel.Deck1!;
        await app.ViewModel.LoadToDeckAsync(a, app.ToneTrack("First"));
        a.TogglePlayCommand.Execute(null);
        string? asked = null;
        app.ViewModel.ConfirmAsync = (heading, _) =>
        {
            asked = heading;
            return Task.FromResult(confirm);
        };

        bool loaded = await app.ViewModel.LoadToDeckAsync(a, app.ToneTrack("Second"));

        Assert.Equal("Replace the track on Deck 1?", asked);
        Assert.Equal(confirm, loaded);
        Assert.Equal(confirm ? "Second" : "First", a.Title);
    }

    [AvaloniaFact]
    public async Task Allow_mode_loads_without_asking()
    {
        using var app = new AppHarness();
        app.Settings.LoadOntoPlayingDeck = PlayingDeckLoad.Allow;
        DeckViewModel a = app.ViewModel.Deck1!;
        await app.ViewModel.LoadToDeckAsync(a, app.ToneTrack("First"));
        a.TogglePlayCommand.Execute(null);

        Assert.True(await app.ViewModel.LoadToDeckAsync(a, app.ToneTrack("Second")));
        Assert.Equal("Second", a.Title);
        Assert.False(a.IsPlaying);   // a freshly loaded deck waits to be started
    }

    [AvaloniaFact]
    public async Task An_on_air_deck_pauses_with_one_tap_by_default()
    {
        using var app = new AppHarness();
        DeckViewModel a = app.ViewModel.Deck1!;
        await app.ViewModel.LoadToDeckAsync(a, app.ToneTrack("Live"));
        a.TogglePlayCommand.Execute(null);
        app.Play(0.2);
        Assert.True(a.IsOnAir);

        a.TogglePlayCommand.Execute(null);

        Assert.False(a.IsPlaying);
        Assert.True(app.Play(0.3).Main < 0.01);
    }

    private static void UseTwoTapPause(AppHarness app) =>
        app.ViewModel.Settings.PauseChoice = app.ViewModel.Settings.PauseChoices.Single(c => c.Value == OnAirPause.TapTwice);

    [AvaloniaFact]
    public async Task Two_tap_mode_pauses_an_on_air_deck_on_the_second_tap()
    {
        using var app = new AppHarness();
        UseTwoTapPause(app);
        DeckViewModel a = app.ViewModel.Deck1!;
        await app.ViewModel.LoadToDeckAsync(a, app.ToneTrack("Live"));
        a.TogglePlayCommand.Execute(null);
        app.Play(0.2);

        a.TogglePlayCommand.Execute(null);
        Assert.True(a.IsPlaying);
        Assert.True(a.IsPauseArmed);
        Assert.Equal("Tap again to pause", a.Hint);

        a.TogglePlayCommand.Execute(null);
        Assert.False(a.IsPlaying);
        Assert.False(a.IsPauseArmed);
    }

    [AvaloniaFact]
    public async Task Two_tap_mode_also_pauses_with_a_hold()
    {
        using var app = new AppHarness();
        UseTwoTapPause(app);
        DeckViewModel a = app.ViewModel.Deck1!;
        await app.ViewModel.LoadToDeckAsync(a, app.ToneTrack("Live"));
        a.TogglePlayCommand.Execute(null);
        app.Play(0.2);

        app.Window.FindControl<DeckView>("Deck1View")!.SimulateHold();

        Assert.False(a.IsPlaying);
    }

    [AvaloniaFact]
    public async Task Two_tap_mode_only_applies_to_decks_the_room_can_hear()
    {
        using var app = new AppHarness();
        UseTwoTapPause(app);
        DeckViewModel b = app.ViewModel.Deck2!;   // crossfader is on A, so B is off air
        await app.ViewModel.LoadToDeckAsync(b, app.ToneTrack("Cueing"));
        b.TogglePlayCommand.Execute(null);
        b.TogglePlayCommand.Execute(null);
        Assert.False(b.IsPlaying);
    }

    [AvaloniaFact]
    public async Task Seeking_is_blocked_on_air_but_allowed_while_cueing()
    {
        using var app = new AppHarness();
        var (a, b) = (app.ViewModel.Deck1!, app.ViewModel.Deck2!);
        await app.ViewModel.LoadToDeckAsync(a, app.ToneTrack("Room"));
        await app.ViewModel.LoadToDeckAsync(b, app.ToneTrack("Cue"));
        a.TogglePlayCommand.Execute(null);
        b.TogglePlayCommand.Execute(null);
        app.Play(0.5);

        Assert.False(a.CanSeek);
        a.SeekTo(0.75);
        Assert.InRange(a.Deck.Position.TotalSeconds, 0.3, 0.8);

        Assert.True(b.CanSeek);
        b.SeekTo(0.75);
        Assert.InRange(b.Deck.Position.TotalSeconds, 14.9, 15.1);
    }

    [AvaloniaFact]
    public async Task Tapping_the_readout_cycles_and_is_remembered()
    {
        using var app = new AppHarness();
        DeckViewModel a = app.ViewModel.Deck1!;
        await app.ViewModel.LoadToDeckAsync(a, app.ToneTrack("Tempo", 20));
        a.TempoPercent = -4;

        Assert.Equal("-0:20", a.ReadoutText[..5]);
        a.CycleReadoutCommand.Execute(null);
        Assert.Equal("0:00", a.ReadoutText);
        a.CycleReadoutCommand.Execute(null);
        Assert.Equal("144", a.ReadoutText);                    // 150 BPM at -4%
        Assert.Equal("BPM · from 150 · -4.0%", a.ReadoutCaption);
        Assert.Equal(DeckReadout.Bpm, app.Settings.DeckReadouts["1"]);
    }

    [AvaloniaFact]
    public async Task Fade_out_stops_the_deck()
    {
        using var app = new AppHarness();
        app.Settings.FadeOutSeconds = 1;
        DeckViewModel a = app.ViewModel.Deck1!;
        await app.ViewModel.LoadToDeckAsync(a, app.ToneTrack("Long one"));
        a.TogglePlayCommand.Execute(null);
        app.Play(0.2);

        a.FadeOutCommand.Execute(null);
        AppHarness.Settle(1200);
        app.Play(0.1);

        Assert.False(a.IsPlaying);
        Assert.True(app.Play(0.2).Main < 0.01);
    }

    [AvaloniaFact]
    public void Preview_plays_in_the_headphones_and_a_second_tap_stops_it()
    {
        using var app = new AppHarness();
        PreviewViewModel preview = app.ViewModel.Preview!;
        Track tone = app.ToneTrack("Audition");

        preview.Toggle(tone);
        var (main, cue) = app.Play(0.3);

        Assert.True(preview.IsActive);
        Assert.Equal("Audition", preview.Title);
        Assert.True(main < 0.01 && cue > 0.4, $"preview only in the headphones: main {main}, cue {cue}");

        preview.SeekTo(0.5);
        app.Play(0.1);
        Assert.StartsWith("0:10", preview.TimeText);

        preview.Toggle(tone);
        Assert.False(preview.IsActive);
        Assert.True(app.Play(0.3).Cue < 0.01);
    }

    [AvaloniaFact]
    public void Dragging_from_the_queue_onto_a_deck_loads_it_and_takes_it_off_the_queue()
    {
        using var app = new AppHarness();
        var queue = app.ViewModel.Queue;
        Track first = app.ToneTrack("Maybe this one"), second = app.ToneTrack("Or this one");
        queue.Add([first, second]);
        AppHarness.Settle(100);
        QueueItem item = queue.Items[1];

        // With the mouse, from the song's row in the queue to the deck.
        var row = (Control)app.Window.FindControl<ListBox>("QueueList")!.ContainerFromIndex(1)!;
        Point from = row.TranslatePoint(new Point(80, row.Bounds.Height / 2), app.Window)!.Value;
        app.MouseDrag(from, Centre(app, "Deck2View"));

        Assert.Equal(second, app.ViewModel.Deck2!.Track);
        Assert.Equal([first], queue.Items.Select(i => i.Track));
    }

    [AvaloniaFact]
    public void Load_menu_items_show_when_a_deck_is_protected()
    {
        using var app = new AppHarness();
        DeckViewModel a = app.ViewModel.Deck1!;
        Drop(app, "Deck1View", app.ToneTrack("Busy"));
        a.TogglePlayCommand.Execute(null);

        var grid = app.Window.FindControl<DataGrid>("TrackGrid")!;
        grid.SelectedIndex = 0;
        app.Window.FindControl<ContextMenu>("TrackMenu")!.Open(grid);
        AppHarness.Settle(100);

        var loadA = app.Window.FindControl<MenuItem>("TrackLoad1")!;
        var loadB = app.Window.FindControl<MenuItem>("TrackLoad2")!;
        Assert.False(loadA.IsEnabled);
        Assert.Equal("Load to Deck 1  (playing)", loadA.Header);
        Assert.True(loadB.IsEnabled);
    }
}
