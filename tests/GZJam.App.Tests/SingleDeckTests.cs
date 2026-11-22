using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.App.Views;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>
/// One deck (#462): the song the room hears, with what's next under it, for DJs who play from a
/// list. Two players underneath, so the next song is ready and auto-play's gap works as with two.
/// </summary>
public sealed class SingleDeckTests
{
    private static AppHarness OneDeck(double gap = 0.3)
    {
        var app = new AppHarness();
        app.Settings.GapSeconds = gap;
        Assert.True(app.ViewModel.SetDeckCount(1));
        AppHarness.Settle(100);
        return app;
    }

    private static T Find<T>(AppHarness app, string name) where T : Control => app.Window.FindControl<T>(name)!;

    private static void PlayUntil(AppHarness app, Func<bool> condition, double timeoutSeconds = 10)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed.TotalSeconds > timeoutSeconds)
                throw new TimeoutException("It didn't get there in time.");
            app.Play(0.033);
            Thread.Sleep(15);
        }
    }

    [AvaloniaFact]
    public void One_deck_shows_one_player_the_mixer_without_a_crossfader_and_the_next_strip()
    {
        using var app = OneDeck();
        var vm = app.ViewModel;

        Assert.True(vm.SingleDeck);
        Assert.Equal(1, app.Settings.DeckCount);   // remembered
        Assert.Equal(2, vm.ActiveDecks.Count);     // two players underneath
        Assert.True(Find<DeckView>(app, "Deck1View").IsEffectivelyVisible);
        Assert.False(Find<DeckView>(app, "Deck2View").IsEffectivelyVisible);
        Assert.True(Find<DeckView>(app, "Deck1View").IsSingle);
        Assert.False(Find<Control>(app, "Crossfader").IsVisible);
        Assert.True(Find<Border>(app, "NextStrip").IsEffectivelyVisible);
        Assert.Same(vm.Deck2, vm.AirLine!.Waiting);

        // The deck is as wide as two decks and the gap between them were.
        DeckView deck = Find<DeckView>(app, "Deck1View");
        Control mixer = Find<Control>(app, "MixerPanel");
        Assert.True(deck.Bounds.Width > mixer.Bounds.Width * 3);

        // No CUE, headphones key, Eject or tempo fader on it.
        Assert.False(Find<Control>(deck, "CueButton").IsEffectivelyVisible);
        Assert.False(Find<Control>(deck, "PreviewButton").IsEffectivelyVisible);
        Assert.False(Find<Control>(deck, "TempoFader").IsEffectivelyVisible);
        Assert.False(Find<Control>(deck, "DeckName").IsEffectivelyVisible);

        vm.SetDeckCount(2);
        AppHarness.Settle(100);
        Assert.False(vm.SingleDeck);
        Assert.True(Find<DeckView>(app, "Deck2View").IsEffectivelyVisible);
        Assert.True(Find<Control>(app, "Crossfader").IsVisible);
        Assert.False(Find<Border>(app, "NextStrip").IsVisible);
        Assert.True(Find<Control>(deck, "CueButton").IsEffectivelyVisible);
        Assert.Null(vm.AirLine.Waiting);
    }

    private static T Find<T>(Control parent, string name) where T : Control =>
        parent.GetLogicalDescendants().OfType<T>().First(c => c.Name == name);

    [AvaloniaFact]
    public void Playing_the_next_song_puts_it_in_the_room_and_it_takes_the_deck()
    {
        using var app = OneDeck();
        var vm = app.ViewModel;
        Track first = app.ToneTrack("First", 1), second = app.ToneTrack("Second", 20);
        AppHarness.Await(vm.LoadToDeckAsync(vm.Deck1!, first));
        AppHarness.Await(vm.LoadToDeckAsync(vm.Deck2!, second));

        vm.Deck1!.TogglePlayCommand.Execute(null);
        app.Play(0.1);
        Assert.True(vm.Deck1.IsOnAir);

        // While a song is playing to the room, the next one can't start over it.
        vm.Deck2!.TogglePlayCommand.Execute(null);
        app.Play(0.05);
        Assert.False(vm.Deck2.IsPlaying);
        Assert.NotNull(vm.Deck2.Hint);

        // Once it's over, the song that ended stays on screen, and Next plays to the room.
        PlayUntil(app, () => !vm.Deck1.IsPlaying);
        Assert.Same(vm.Deck1, vm.AirLine!.Now);
        vm.Deck2.TogglePlayCommand.Execute(null);
        app.Play(0.1);
        Assert.True(vm.Deck2.IsOnAir);
        AppHarness.Settle(50);
        Assert.Same(vm.Deck2, vm.AirLine.Now);
        Assert.Same(vm.Deck1, vm.AirLine.Waiting);
        Assert.True(Find<DeckView>(app, "Deck2View").IsEffectivelyVisible);
        Assert.False(Find<DeckView>(app, "Deck1View").IsEffectivelyVisible);
        Assert.Equal("NOW PLAYING", vm.Deck2.RowName);
        Assert.Equal("NEXT", vm.Deck1.RowName);
    }

    [AvaloniaFact]
    public void Through_auto_plays_gap_the_song_that_ended_stays_on_screen_until_the_next_starts()
    {
        using var app = OneDeck(gap: 1);
        var vm = app.ViewModel;
        var tracks = Enumerable.Range(1, 3).Select(i => app.ToneTrack($"Song {i}", 1)).ToList();
        vm.Queue.Add(tracks);
        vm.AutoPlay!.IsOn = true;
        app.Play(0.1);
        Assert.Same(vm.Deck1, vm.AirLine!.Now);
        Assert.Equal(tracks[1], vm.AirLine.Waiting?.Track);   // read ahead onto the other player

        PlayUntil(app, () => vm.Deck2!.IsStarting);
        PlayUntil(app, () => vm.Mixer!.Crossfader > 0.6);   // the crossfader is across, out of sight
        Assert.Same(vm.Deck1, vm.AirLine.Now);
        Assert.StartsWith("STARTS IN", vm.Deck2!.Status);

        PlayUntil(app, () => vm.Deck2.IsOnAir);
        app.Play(0.05);
        Assert.Same(vm.Deck2, vm.AirLine.Now);
    }

    [AvaloniaFact]
    public void A_song_dropped_on_the_next_strip_loads_the_waiting_player()
    {
        using var app = OneDeck();
        var vm = app.ViewModel;
        Track playing = app.ToneTrack("Playing"), next = app.ToneTrack("Next");
        AppHarness.Await(vm.LoadToDeckAsync(vm.Deck1!, playing));
        vm.Deck1!.TogglePlayCommand.Execute(null);
        app.Play(0.1);

        Assert.Same(vm.Deck2, app.Window.FindControl<Border>("NextStrip")!.DataContext);
        Assert.True(vm.CanLoad(vm.Deck2!));
        Assert.False(vm.CanLoad(vm.Deck1));   // the one playing is protected, as on a deck
        AppHarness.Await(vm.LoadToDeckAsync(vm.Deck2!, next));
        AppHarness.Settle(50);
        Assert.Equal("Next", Find<TextBlock>(app, "NextTitle").Text);
    }

    [AvaloniaFact]
    public void Going_to_one_deck_turns_cue_off_and_the_setting_offers_one_two_and_four()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        vm.Deck2!.IsCueOn = true;
        Assert.Equal([1, 2, 4], vm.Settings.DeckCountChoices.Select(c => c.Value));
        Assert.Equal(2, vm.Settings.DeckCountChoice.Value);

        vm.Settings.DeckCountChoice = vm.Settings.DeckCountChoices[0];
        AppHarness.Settle(50);

        Assert.True(vm.SingleDeck);
        Assert.False(vm.Deck2.IsCueOn);
        Assert.Equal(1, vm.Settings.DeckCountChoice.Value);
        Assert.False(vm.Settings.FourDecks);
    }

    [AvaloniaFact]
    public void Screenshot_one_deck()
    {
        using var app = OneDeck();
        var vm = app.ViewModel;
        AppHarness.Await(vm.LoadToDeckAsync(vm.Deck1!, app.ToneTrack("I Found My Yellow Basket", 160)));
        AppHarness.Await(vm.LoadToDeckAsync(vm.Deck2!, app.ToneTrack("Tain't What You Do", 187)));
        vm.Deck1!.TogglePlayCommand.Execute(null);
        app.Play(0.5);
        app.Screenshot("single-deck");
        vm.DeckSize = DeckSize.Compact;
        app.Screenshot("single-deck-compact");
        vm.DeckSize = DeckSize.Hidden;
        app.Screenshot("single-deck-hidden");
    }
}
