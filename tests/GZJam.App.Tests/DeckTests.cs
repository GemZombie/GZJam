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
    private static void Drop(AppHarness app, string deckView, Track track, RawDragEventType type = RawDragEventType.Drop)
    {
        var view = app.Window.FindControl<DeckView>(deckView)!;
        Point centre = view.TranslatePoint(new Point(view.Bounds.Width / 2, view.Bounds.Height / 2), app.Window)!.Value;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(MainWindow.TrackFormat, new MainWindow.TrackDrag([track], null)));
        app.Window.DragDrop(centre, RawDragEventType.DragEnter, data, DragDropEffects.Copy, RawInputModifiers.None);
        app.Window.DragDrop(centre, RawDragEventType.DragOver, data, DragDropEffects.Copy, RawInputModifiers.None);
        if (type == RawDragEventType.Drop)
            app.Window.DragDrop(centre, RawDragEventType.Drop, data, DragDropEffects.Copy, RawInputModifiers.None);
        AppHarness.Settle(100);
    }

    [AvaloniaFact]
    public void Dropping_a_track_on_a_deck_loads_it()
    {
        using var app = new AppHarness();
        Track tone = app.ToneTrack("Tain't What You Do");

        Drop(app, "DeckBView", tone);

        DeckViewModel b = app.ViewModel.DeckB!;
        Assert.Equal(tone, b.Track);
        Assert.Equal("Tain't What You Do", b.Title);
        Assert.Equal("CUE", b.Status);              // crossfader starts on A, so B is in the headphones
        Assert.Equal("-0:20", b.ReadoutText);
        Assert.False(b.IsPlaying);
    }

    [AvaloniaFact]
    public void The_live_deck_plays_to_the_room_and_the_other_to_the_headphones()
    {
        using var app = new AppHarness();
        var (a, b) = (app.ViewModel.DeckA!, app.ViewModel.DeckB!);
        Drop(app, "DeckAView", app.ToneTrack("Room"));
        Drop(app, "DeckBView", app.ToneTrack("Cue"));

        a.TogglePlayCommand.Execute(null);
        var (main, cue) = app.Play(0.3);
        Assert.True(main > 0.4 && cue < 0.01, $"A only in the room: main {main}, cue {cue}");
        Assert.Equal("ON AIR", a.Status);

        a.HoldPause();
        b.TogglePlayCommand.Execute(null);
        (main, cue) = app.Play(0.3);
        Assert.True(main < 0.01 && cue > 0.4, $"B only in the headphones: main {main}, cue {cue}");
        Assert.Equal("CUE", b.Status);
    }

    [AvaloniaFact]
    public void Snapping_the_crossfader_swaps_which_deck_is_live()
    {
        using var app = new AppHarness();
        var (a, b) = (app.ViewModel.DeckA!, app.ViewModel.DeckB!);
        Drop(app, "DeckAView", app.ToneTrack("One"));
        Drop(app, "DeckBView", app.ToneTrack("Two"));
        a.TogglePlayCommand.Execute(null);
        b.TogglePlayCommand.Execute(null);
        app.Play(0.2);
        Assert.True(a.IsOnAir);

        app.ViewModel.Mixer!.SnapToBCommand.Execute(null);
        AppHarness.Settle(400);   // the snap glides over 300 ms
        app.ViewModel.Tick();

        Assert.Equal(1, app.ViewModel.Mixer.Crossfader, 3);
        Assert.True(b.IsOnAir);
        Assert.Equal("CUE", a.Status);
        Assert.Equal("B · LIVE", app.ViewModel.Mixer.LiveText);
    }

    [AvaloniaFact]
    public void A_playing_deck_refuses_a_new_track_by_default()
    {
        using var app = new AppHarness();
        DeckViewModel a = app.ViewModel.DeckA!;
        Track first = app.ToneTrack("First");
        Drop(app, "DeckAView", first);
        a.TogglePlayCommand.Execute(null);
        app.Play(0.2);

        // Dragging over shows the deck refusing; dropping doesn't replace the track.
        Drop(app, "DeckAView", app.ToneTrack("Second"), RawDragEventType.DragOver);
        Assert.Contains("drop-blocked", app.Window.FindControl<DeckView>("DeckAView")!.FindControl<Border>("Frame")!.Classes);
        Drop(app, "DeckAView", app.ToneTrack("Second"));

        Assert.Equal(first, a.Track);
        Assert.True(a.IsPlaying);
        Assert.Equal("Deck A is playing. Pause it first to load a new track.", app.ViewModel.Notice);
        Assert.False(app.ViewModel.CanLoad(a));

        // Paused, the same deck takes the track.
        a.HoldPause();
        Drop(app, "DeckAView", app.ToneTrack("Second"));
        Assert.Equal("Second", a.Title);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ask_mode_loads_only_when_confirmed(bool confirm)
    {
        using var app = new AppHarness();
        app.Settings.LoadOntoPlayingDeck = PlayingDeckLoad.Ask;
        DeckViewModel a = app.ViewModel.DeckA!;
        await app.ViewModel.LoadToDeckAsync(a, app.ToneTrack("First"));
        a.TogglePlayCommand.Execute(null);
        string? asked = null;
        app.ViewModel.ConfirmAsync = (heading, _) =>
        {
            asked = heading;
            return Task.FromResult(confirm);
        };

        bool loaded = await app.ViewModel.LoadToDeckAsync(a, app.ToneTrack("Second"));

        Assert.Equal("Replace the track on Deck A?", asked);
        Assert.Equal(confirm, loaded);
        Assert.Equal(confirm ? "Second" : "First", a.Title);
    }

    [AvaloniaFact]
    public async Task Allow_mode_loads_without_asking()
    {
        using var app = new AppHarness();
        app.Settings.LoadOntoPlayingDeck = PlayingDeckLoad.Allow;
        DeckViewModel a = app.ViewModel.DeckA!;
        await app.ViewModel.LoadToDeckAsync(a, app.ToneTrack("First"));
        a.TogglePlayCommand.Execute(null);

        Assert.True(await app.ViewModel.LoadToDeckAsync(a, app.ToneTrack("Second")));
        Assert.Equal("Second", a.Title);
        Assert.False(a.IsPlaying);   // a freshly loaded deck waits to be started
    }

    [AvaloniaFact]
    public async Task The_live_deck_needs_a_hold_to_pause()
    {
        using var app = new AppHarness();
        DeckViewModel a = app.ViewModel.DeckA!;
        await app.ViewModel.LoadToDeckAsync(a, app.ToneTrack("Live"));
        a.TogglePlayCommand.Execute(null);
        app.Play(0.2);

        a.TogglePlayCommand.Execute(null);   // a tap
        Assert.True(a.IsPlaying);
        Assert.Equal("Hold to pause the live deck", a.Hint);

        app.Window.FindControl<DeckView>("DeckAView")!.SimulateHold();
        Assert.False(a.IsPlaying);

        // With tap-to-pause chosen, a tap is enough.
        app.ViewModel.Settings.PauseChoice = app.ViewModel.Settings.PauseChoices.Single(c => c.Value == LiveDeckPause.Tap);
        a.TogglePlayCommand.Execute(null);
        a.TogglePlayCommand.Execute(null);
        Assert.False(a.IsPlaying);
    }

    [AvaloniaFact]
    public async Task A_cue_deck_pauses_with_a_tap()
    {
        using var app = new AppHarness();
        DeckViewModel b = app.ViewModel.DeckB!;
        await app.ViewModel.LoadToDeckAsync(b, app.ToneTrack("Cueing"));
        b.TogglePlayCommand.Execute(null);
        b.TogglePlayCommand.Execute(null);
        Assert.False(b.IsPlaying);
    }

    [AvaloniaFact]
    public async Task Seeking_is_blocked_on_air_but_allowed_while_cueing()
    {
        using var app = new AppHarness();
        var (a, b) = (app.ViewModel.DeckA!, app.ViewModel.DeckB!);
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
        DeckViewModel a = app.ViewModel.DeckA!;
        await app.ViewModel.LoadToDeckAsync(a, app.ToneTrack("Tempo", 20));
        a.TempoPercent = -4;

        Assert.Equal("-0:20", a.ReadoutText[..5]);
        a.CycleReadoutCommand.Execute(null);
        Assert.Equal("0:00", a.ReadoutText);
        a.CycleReadoutCommand.Execute(null);
        Assert.Equal("144", a.ReadoutText);                    // 150 BPM at -4%
        Assert.Equal("BPM · from 150 · -4.0%", a.ReadoutCaption);
        Assert.Equal(DeckReadout.Bpm, app.Settings.DeckReadouts["A"]);
    }

    [AvaloniaFact]
    public async Task Fade_out_stops_the_deck()
    {
        using var app = new AppHarness();
        app.Settings.FadeOutSeconds = 1;
        DeckViewModel a = app.ViewModel.DeckA!;
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
    public void Load_menu_items_show_when_a_deck_is_protected()
    {
        using var app = new AppHarness();
        DeckViewModel a = app.ViewModel.DeckA!;
        Drop(app, "DeckAView", app.ToneTrack("Busy"));
        a.TogglePlayCommand.Execute(null);

        var grid = app.Window.FindControl<DataGrid>("TrackGrid")!;
        grid.SelectedIndex = 0;
        app.Window.FindControl<ContextMenu>("TrackMenu")!.Open(grid);
        AppHarness.Settle(100);

        var loadA = app.Window.FindControl<MenuItem>("TrackLoadA")!;
        var loadB = app.Window.FindControl<MenuItem>("TrackLoadB")!;
        Assert.False(loadA.IsEnabled);
        Assert.Equal("Load to Deck A  (playing)", loadA.Header);
        Assert.True(loadB.IsEnabled);
    }
}
