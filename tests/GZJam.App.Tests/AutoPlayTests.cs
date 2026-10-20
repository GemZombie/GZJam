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
    private static TimeSpan PlayUntil(AppHarness app, Func<bool> condition, double timeoutSeconds = 10)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed.TotalSeconds > timeoutSeconds)
                throw new TimeoutException("Auto-play didn't get there in time.");
            app.Play(0.033);
            Thread.Sleep(15);
        }
        return watch.Elapsed;
    }

    [AvaloniaFact]
    public void The_switch_is_greyed_out_until_there_is_something_to_play()
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

        PlayUntil(app, () => d1.IsFinished);
        Assert.StartsWith("Deck 2 in", auto.Status);
        Assert.False(d2.IsPlaying);   // waiting out the gap

        TimeSpan gap = PlayUntil(app, () => d2.IsPlaying);
        Assert.InRange(gap.TotalSeconds, 0.4, 1.5);
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
}
