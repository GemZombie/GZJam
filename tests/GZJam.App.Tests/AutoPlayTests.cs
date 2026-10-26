using System.Diagnostics;
using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>Auto-play through the queue, with real (offline) playback and a real-time gap.</summary>
public sealed class AutoPlayTests
{
    private static (AppHarness App, AutoPlayViewModel Auto, List<Track> Tracks) Setup(int queued, double seconds = 1, double gap = 0.3)
    {
        var app = new AppHarness();
        app.Settings.GapSeconds = gap;
        var tracks = Enumerable.Range(1, queued).Select(i => app.ToneTrack($"Song {i}", seconds)).ToList();
        app.ViewModel.Queue.Add(tracks);
        return (app, app.ViewModel.AutoPlay!, tracks);
    }

    /// <summary>Plays audio in small steps with UI ticks, as the app does, until a condition holds.</summary>
    private static TimeSpan PlayUntil(AppHarness app, Func<bool> condition, double timeoutSeconds = 10) =>
        PlayUntil(app, condition, out _, timeoutSeconds);

    /// <summary>
    /// The same, and when the step that made the condition hold began: whatever made it hold
    /// happened at or after then. (On a slow machine a step takes a while, so the moment the
    /// condition is seen can be well after it began to hold.)
    /// </summary>
    private static TimeSpan PlayUntil(AppHarness app, Func<bool> condition, out long lastStepStarted, double timeoutSeconds = 10)
    {
        var watch = Stopwatch.StartNew();
        lastStepStarted = Stopwatch.GetTimestamp();
        while (!condition())
        {
            if (watch.Elapsed.TotalSeconds > timeoutSeconds)
                throw new TimeoutException("Auto-play didn't get there in time.");
            lastStepStarted = Stopwatch.GetTimestamp();
            app.Play(0.033);
            Thread.Sleep(15);
        }
        return watch.Elapsed;
    }

    [AvaloniaFact]
    public void The_switch_is_grayed_out_until_there_is_something_to_play()
    {
        using var app = new AppHarness();
        AutoPlayViewModel auto = app.ViewModel.AutoPlay!;
        Assert.False(auto.CanToggle);

        app.ViewModel.Queue.Add([app.ToneTrack("Anything")]);

        Assert.True(auto.CanToggle);
    }

    [AvaloniaFact]
    public void Turning_on_loads_the_queue_onto_both_decks_and_starts_the_first()
    {
        var (app, auto, tracks) = Setup(queued: 3);
        using var _ = app;
        var (d1, d2) = (app.ViewModel.Deck1!, app.ViewModel.Deck2!);

        auto.IsOn = true;
        app.Play(0.1);

        Assert.Equal(tracks[0], d1.Track);
        Assert.True(d1.IsOnAir);
        Assert.Equal(tracks[1], d2.Track);
        Assert.True(d2.IsReady);
        Assert.False(d2.IsPlaying);
        Assert.Equal([tracks[2]], app.ViewModel.Queue.Items.Select(i => i.Track));
        Assert.Equal("Next: Deck 2", auto.Status);
    }

    [AvaloniaFact]
    public void Plays_through_the_queue_alternating_decks_with_the_gap()
    {
        var (app, auto, tracks) = Setup(queued: 3, gap: 0.5);
        using var _ = app;
        var (d1, d2) = (app.ViewModel.Deck1!, app.ViewModel.Deck2!);
        auto.IsOn = true;

        PlayUntil(app, () => d1.IsFinished, out long finishedBy);
        Assert.StartsWith("Deck 2 in", auto.Status);
        Assert.False(d2.IsPlaying);   // waiting out the gap

        // Timed from the start of the step in which Deck 1 finished (the gap began then or after),
        // to when Deck 2 is seen playing (it started then or before): at least the gap.
        PlayUntil(app, () => d2.IsPlaying);
        double gap = Stopwatch.GetElapsedTime(finishedBy).TotalSeconds;
        Assert.InRange(gap, 0.45, 1.5);
        Assert.Equal(1, app.ViewModel.Mixer!.Crossfader);
        Assert.True(d2.IsOnAir);

        // The deck that finished is refilled with the next song, ready for the switch back.
        app.Play(0.1);
        Assert.Equal(tracks[2], d1.Track);
        Assert.True(d1.IsReady);
        Assert.Empty(app.ViewModel.Queue.Items);

        PlayUntil(app, () => d1.IsPlaying);
        Assert.Equal(0, app.ViewModel.Mixer.Crossfader);
    }

    [AvaloniaFact]
    public void Through_the_gap_the_crossfader_slides_over_and_the_next_deck_counts_down()
    {
        var (app, auto, _) = Setup(queued: 3, gap: 2);
        using var _app = app;
        var (d1, d2) = (app.ViewModel.Deck1!, app.ViewModel.Deck2!);
        var mixer = app.ViewModel.Mixer!;
        auto.IsOn = true;
        PlayUntil(app, () => d1.IsFinished);
        Assert.Equal("STARTS IN 2", d2.Status);
        Assert.True(d2.ShowsStatus);

        // Halfway through: the crossfader is on its way, and the light blinks.
        var lights = new HashSet<bool>();
        PlayUntil(app, () =>
        {
            lights.Add(d2.StatusLight);
            return d2.StartsIn < 1;
        });
        Assert.InRange(mixer.Crossfader, 0.3, 0.75);
        Assert.Equal("STARTS IN 1", d2.Status);
        Assert.Equal([false, true], lights.Order());
        Assert.False(d2.IsPlaying);
        AppHarness.Settle(50);
        app.Screenshot("autoplay-gap");

        PlayUntil(app, () => d2.IsPlaying);
        Assert.Equal(1, mixer.Crossfader);
        Assert.Equal("ON AIR", d2.Status);
        Assert.Null(d2.StartsIn);
    }

    [AvaloniaFact]
    public void Grabbing_the_crossfader_in_the_gap_takes_over_from_auto_play()
    {
        var (app, auto, _) = Setup(queued: 3, gap: 1);
        using var _app = app;
        var (d1, d2) = (app.ViewModel.Deck1!, app.ViewModel.Deck2!);
        var mixer = app.ViewModel.Mixer!;
        auto.IsOn = true;
        PlayUntil(app, () => d1.IsFinished);
        PlayUntil(app, () => mixer.Crossfader > 0.1);

        mixer.Crossfader = 0.5;   // the DJ wants a blend
        Assert.False(mixer.IsGliding);
        PlayUntil(app, () => d2.IsPlaying);
        Assert.Equal(0.5, mixer.Crossfader);   // left where the DJ put it; the song still starts
    }

    [AvaloniaFact]
    public void Turning_off_in_the_gap_stops_the_countdown()
    {
        var (app, auto, _) = Setup(queued: 3, gap: 2);
        using var _app = app;
        var (d1, d2) = (app.ViewModel.Deck1!, app.ViewModel.Deck2!);
        auto.IsOn = true;
        PlayUntil(app, () => d1.IsFinished);
        Assert.True(d2.IsStarting);

        auto.IsOn = false;
        Assert.False(d2.IsStarting);
        Assert.Equal("", d2.Status);
    }

    [AvaloniaFact]
    public void Turning_off_lets_the_song_finish_and_starts_nothing()
    {
        var (app, auto, _) = Setup(queued: 3, gap: 0);
        using var _app = app;
        var (d1, d2) = (app.ViewModel.Deck1!, app.ViewModel.Deck2!);
        auto.IsOn = true;
        app.Play(0.2);

        auto.IsOn = false;
        Assert.True(d1.IsPlaying);   // the current song carries on

        PlayUntil(app, () => d1.IsFinished);
        app.Play(0.5);
        Assert.False(d2.IsPlaying);
    }

    [AvaloniaFact]
    public void A_fade_out_moves_on_to_the_next_song()
    {
        var (app, auto, _) = Setup(queued: 2, seconds: 30, gap: 0.2);
        using var _app = app;
        app.Settings.FadeOutSeconds = 1;
        var (d1, d2) = (app.ViewModel.Deck1!, app.ViewModel.Deck2!);
        auto.IsOn = true;
        app.Play(0.2);

        d1.FadeOutCommand.Execute(null);

        PlayUntil(app, () => d2.IsPlaying, timeoutSeconds: 5);
        Assert.True(d1.IsFinished);
        Assert.True(d2.IsOnAir);
    }

    [AvaloniaFact]
    public void Stops_when_the_queue_runs_out()
    {
        var (app, auto, _) = Setup(queued: 1, gap: 0);
        using var _app = app;
        auto.IsOn = true;
        Assert.Equal("Queue is empty", auto.Status);

        PlayUntil(app, () => app.ViewModel.Deck1!.IsFinished);
        app.Play(0.1);

        Assert.False(auto.IsOn);
        Assert.Equal("Auto-play stopped: the queue is empty.", app.ViewModel.Notice);
    }

    [AvaloniaFact]
    public void Tracks_queued_later_fill_the_empty_next_deck()
    {
        var (app, auto, _) = Setup(queued: 1, seconds: 30);
        using var _app = app;
        auto.IsOn = true;
        app.Play(0.1);
        Assert.False(app.ViewModel.Deck2!.HasTrack);

        Track later = app.ToneTrack("Added later");
        app.ViewModel.Queue.Add([later]);
        app.Play(0.1);

        Assert.Equal(later, app.ViewModel.Deck2.Track);
        Assert.Equal("Next: Deck 2", auto.Status);
    }

    [AvaloniaFact]
    public void A_deck_finishing_in_the_headphones_does_not_advance_the_set()
    {
        var (app, auto, _) = Setup(queued: 0);
        using var _app = app;
        var (d1, d2) = (app.ViewModel.Deck1!, app.ViewModel.Deck2!);
        d1.Load(app.ToneTrack("Room", 30));
        d2.Load(app.ToneTrack("Headphones only", 1));
        app.ViewModel.Queue.Add([app.ToneTrack("Queued")]);
        auto.IsOn = true;   // starts Deck 1 for the room
        d2.IsCueOn = true;
        d2.TogglePlayCommand.Execute(null);

        PlayUntil(app, () => !d2.IsPlaying);
        app.Play(0.5);

        Assert.True(d1.IsOnAir);
        Assert.Equal("Room", d1.Title);
        // The room never heard Deck 2's song, so it's still the next one, rewound and ready.
        Assert.Equal("Headphones only", d2.Title);
        Assert.True(d2.IsReady);
        Assert.False(d2.IsFinished);
        Assert.InRange(d2.Deck.Position.TotalSeconds, 0, 0.05);
        Assert.Single(app.ViewModel.Queue.Items);
    }

    [AvaloniaFact]
    public void Gap_buttons_adjust_and_are_remembered()
    {
        using var app = new AppHarness();
        AutoPlayViewModel auto = app.ViewModel.AutoPlay!;
        Assert.Equal("3.0 s", auto.GapText);

        auto.GapUpCommand.Execute(null);
        auto.GapUpCommand.Execute(null);
        Assert.Equal("4.0 s", auto.GapText);
        Assert.Equal(4, app.Settings.GapSeconds);

        for (int i = 0; i < 20; i++)
            auto.GapDownCommand.Execute(null);
        Assert.Equal("0.0 s", auto.GapText);
    }

    // ───────────── Fade after a set time (#98) ─────────────

    /// <summary>Moves a deck to just before a place in its song (a seek: not counted as playing past anything).</summary>
    private static void JustBefore(AppHarness app, DeckViewModel deck, TimeSpan at)
    {
        deck.Deck.Seek(at - TimeSpan.FromSeconds(0.2));
        app.Play(0.01);
    }

    [AvaloniaFact]
    public void With_a_limit_each_song_fades_out_after_it_and_the_next_one_starts()
    {
        var (app, auto, tracks) = Setup(queued: 3, seconds: 75);
        using var _ = app;
        app.Settings.FadeOutSeconds = 0.5;
        var (d1, d2) = (app.ViewModel.Deck1!, app.ViewModel.Deck2!);
        auto.FadeAfterUpCommand.Execute(null);   // Off › 1:00
        Assert.Equal("1:00", auto.FadeAfterText);

        auto.IsOn = true;
        app.Play(0.1);
        TimeSpan fadesAt = d1.Deck.Start + TimeSpan.FromSeconds(60);
        Assert.Equal(fadesAt, d1.Deck.FadesAt);
        Assert.Equal($"0:00 / fade {Format.Duration(fadesAt)}", d1.ReadoutCaption);
        Assert.InRange(d1.Deck.Remaining.TotalSeconds, 59.5, 61);   // to the end of the fade, not the song

        JustBefore(app, d1, fadesAt);
        Assert.False(d1.Deck.IsFading);
        PlayUntil(app, () => d1.Deck.IsFading);
        Assert.InRange(d1.Deck.Position.TotalSeconds, fadesAt.TotalSeconds, fadesAt.TotalSeconds + 0.5);

        PlayUntil(app, () => d2.IsOnAir);
        Assert.Equal(d2.Deck.Start + TimeSpan.FromSeconds(60), d2.Deck.FadesAt);   // the next song has it too
        Assert.Null(d1.Deck.AutoFade);   // out of the room
        Assert.Equal(tracks[2], d1.Track);
    }

    [AvaloniaFact]
    public void A_song_moved_past_the_limit_plays_on_and_a_short_one_plays_to_its_end()
    {
        var (app, auto, _) = Setup(queued: 2, seconds: 75);
        using var _app = app;
        var d1 = app.ViewModel.Deck1!;
        auto.FadeAfterUpCommand.Execute(null);
        auto.IsOn = true;
        app.Play(0.1);

        d1.Deck.Seek(TimeSpan.FromSeconds(62));   // the DJ moved it on: past the point, not played past it
        for (int i = 0; i < 10; i++)
            app.Play(0.033);
        Assert.False(d1.Deck.IsFading);
        Assert.Null(d1.Deck.FadesAt);
        Assert.EndsWith("/ 1:15", d1.ReadoutCaption);

        // A limit longer than the song: nothing to fade, it plays to its end as before.
        for (int i = 0; i < 4; i++)
            auto.FadeAfterUpCommand.Execute(null);   // 2:00
        d1.Deck.Seek(TimeSpan.Zero);
        app.Play(0.033);
        Assert.Null(d1.Deck.FadesAt);
    }

    [AvaloniaFact]
    public void Turning_auto_play_off_lets_the_song_play_to_its_end()
    {
        var (app, auto, _) = Setup(queued: 2, seconds: 75);
        using var _app = app;
        var d1 = app.ViewModel.Deck1!;
        auto.FadeAfterUpCommand.Execute(null);
        auto.IsOn = true;
        app.Play(0.1);
        Assert.NotNull(d1.Deck.FadesAt);

        auto.IsOn = false;
        Assert.Null(d1.Deck.AutoFade);
        JustBefore(app, d1, TimeSpan.FromSeconds(60));
        for (int i = 0; i < 15; i++)
            app.Play(0.033);
        Assert.False(d1.Deck.IsFading);
        Assert.True(d1.IsPlaying);
    }

    [AvaloniaFact]
    public void Fade_after_steps_from_off_through_one_to_five_minutes_and_is_remembered()
    {
        using var app = new AppHarness();
        AutoPlayViewModel auto = app.ViewModel.AutoPlay!;
        Assert.Equal("Off", auto.FadeAfterText);
        Assert.False(auto.IsFadeAfterOn);

        auto.FadeAfterUpCommand.Execute(null);
        auto.FadeAfterUpCommand.Execute(null);
        Assert.Equal("1:15", auto.FadeAfterText);
        Assert.Equal(75, app.Settings.AutoFadeAfterSeconds);

        for (int i = 0; i < 30; i++)
            auto.FadeAfterUpCommand.Execute(null);
        Assert.Equal("5:00", auto.FadeAfterText);

        for (int i = 0; i < 16; i++)
            auto.FadeAfterDownCommand.Execute(null);
        Assert.Equal("1:00", auto.FadeAfterText);
        auto.FadeAfterDownCommand.Execute(null);
        Assert.Equal("Off", auto.FadeAfterText);
        Assert.Equal(0, app.Settings.AutoFadeAfterSeconds);
    }

    // ───────────── Wait after each song, stop at a set time (#100) ─────────────

    [AvaloniaFact]
    public void Waiting_after_each_song_lines_up_the_next_and_play_carries_on()
    {
        var (app, auto, tracks) = Setup(queued: 4);
        using var _ = app;
        var (d1, d2) = (app.ViewModel.Deck1!, app.ViewModel.Deck2!);
        auto.WaitAfterEach = true;
        auto.IsOn = true;

        PlayUntil(app, () => d1.IsFinished);
        PlayUntil(app, () => app.ViewModel.Mixer!.Crossfader == 1);   // lined up: the crossfader goes over
        for (int i = 0; i < 20; i++)
            app.Play(0.033);   // well past the gap: it doesn't start by itself
        Assert.False(d2.IsPlaying);
        Assert.Equal("Deck 2 is ready: press play", auto.Status);
        Assert.Equal(tracks[1], d2.Track);

        d2.TogglePlayCommand.Execute(null);   // the teacher is ready
        app.Play(0.1);
        Assert.True(d2.IsOnAir);
        Assert.Equal(tracks[2], d1.Track);   // and the one after is loaded behind it
        Assert.Equal("Next: Deck 1", auto.Status);

        PlayUntil(app, () => d2.IsFinished);
        PlayUntil(app, () => app.ViewModel.Mixer!.Crossfader == 0);
        Assert.False(d1.IsPlaying);
        Assert.Equal("Deck 1 is ready: press play", auto.Status);

        auto.WaitAfterEach = false;   // no longer waiting: it starts
        app.Play(0.1);
        Assert.True(d1.IsOnAir);
    }

    [AvaloniaFact]
    public void At_the_stop_time_the_song_playing_is_the_last()
    {
        var (app, auto, _) = Setup(queued: 4);
        using var _app = app;
        var (d1, d2) = (app.ViewModel.Deck1!, app.ViewModel.Deck2!);
        var now = new DateTime(2026, 10, 30, 22, 50, 0);
        auto.Now = () => now;
        auto.StopAt = new DateTime(2026, 10, 30, 23, 0, 0);
        auto.IsOn = true;

        PlayUntil(app, () => d2.IsPlaying);   // before the time: on to the next as usual

        now = now.AddMinutes(15);
        PlayUntil(app, () => d2.IsFinished);
        for (int i = 0; i < 20; i++)
            app.Play(0.033);
        Assert.False(auto.IsOn);
        Assert.False(d1.IsPlaying);
        Assert.Null(auto.StopAt);   // done: tomorrow's night isn't cut short
        Assert.Equal($"Auto-play stopped: it's past {new DateTime(2026, 10, 30, 23, 0, 0):t}, as set.", app.ViewModel.Notice);
    }

    [AvaloniaFact]
    public void Stop_times_are_every_quarter_hour_from_the_next_one()
    {
        using var app = new AppHarness();
        var auto = app.ViewModel.AutoPlay!;
        auto.Now = () => new DateTime(2026, 10, 30, 21, 52, 10);

        auto.RefreshStopChoices();

        Assert.Equal("Off", auto.StopChoices[0].Label);
        Assert.Equal(new DateTime(2026, 10, 30, 22, 0, 0), auto.StopChoices[1].Value);
        Assert.Equal(new DateTime(2026, 10, 31, 5, 45, 0), auto.StopChoices[^1].Value);   // past midnight too
        Assert.Equal(33, auto.StopChoices.Count);
        Assert.Same(auto.StopChoices[0], auto.StopChoice);

        auto.StopChoice = auto.StopChoices[5];
        Assert.Equal(new DateTime(2026, 10, 30, 23, 0, 0), auto.StopAt);
        auto.StopChoice = null!;   // the list being replaced: not a choice
        Assert.Equal(new DateTime(2026, 10, 30, 23, 0, 0), auto.StopAt);

        auto.Now = () => new DateTime(2026, 10, 30, 22, 0, 0);
        auto.RefreshStopChoices();
        Assert.Equal(new DateTime(2026, 10, 30, 22, 15, 0), auto.StopChoices[1].Value);
        Assert.Equal(auto.StopAt, auto.StopChoice.Value);
    }

    [AvaloniaFact]
    public void The_options_that_are_set_show_under_auto_play()
    {
        using var app = new AppHarness();
        var auto = app.ViewModel.AutoPlay!;
        Assert.Equal("", auto.OptionsText);

        for (int i = 0; i < 3; i++)
            auto.FadeAfterUpCommand.Execute(null);
        auto.WaitAfterEach = true;
        auto.StopAt = new DateTime(2026, 10, 30, 23, 0, 0);

        Assert.Equal($"Fade after 1:30 · Waits after each song · Stops at {auto.StopAt:t}", auto.OptionsText);
    }
}
