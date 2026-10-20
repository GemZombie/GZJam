using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>Session history: what counts as played, how it's shown, and that it's saved.</summary>
public sealed class HistoryAppTests
{
    /// <summary>Plays audio in steps, with UI ticks, like the app's 30 fps timer.</summary>
    private static void Listen(AppHarness app, double seconds)
    {
        for (double t = 0; t < seconds; t += 0.5)
            app.Play(0.5);
    }

    private static async Task<DeckViewModel> OnAir(AppHarness app, Track track)
    {
        DeckViewModel deck1 = app.ViewModel.Deck1!;
        await app.ViewModel.LoadToDeckAsync(deck1, track);
        deck1.TogglePlayCommand.Execute(null);
        return deck1;
    }

    [AvaloniaFact]
    public async Task A_song_the_room_hears_for_30_seconds_goes_into_history()
    {
        using var app = new AppHarness();
        HistoryViewModel history = app.ViewModel.History;
        await OnAir(app, app.ToneTrack("Splanky", 60));

        Listen(app, 25);
        Assert.Empty(history.Plays);

        Listen(app, 6);
        PlayItem play = Assert.Single(history.Plays);
        Assert.Equal("Splanky", play.Title);
        Assert.Equal("Test Band", play.Artist);
        Assert.Equal("1", play.Record.Deck);
        Assert.Equal("This session · 1 song", history.SelectedSession!.Label);
        Assert.Equal("1 song · 1:00", history.Summary);
    }

    [AvaloniaFact]
    public async Task Headphone_previews_and_cueing_never_count()
    {
        using var app = new AppHarness();
        DeckViewModel deck2 = app.ViewModel.Deck2!;
        await app.ViewModel.LoadToDeckAsync(deck2, app.ToneTrack("Cued only", 60));
        deck2.IsCueOn = true;
        deck2.TogglePlayCommand.Execute(null);   // crossfader is on Deck 1, so the room can't hear Deck 2
        app.ViewModel.Preview!.Toggle(app.ToneTrack("Previewed", 60));

        Listen(app, 35);

        Assert.Empty(app.ViewModel.History.Plays);
    }

    [AvaloniaFact]
    public async Task A_song_faded_out_early_does_not_count_but_a_short_song_played_through_does()
    {
        using var app = new AppHarness();
        app.Settings.FadeOutSeconds = 1;
        DeckViewModel deck1 = await OnAir(app, app.ToneTrack("Wrong song", 60));
        Listen(app, 10);
        deck1.FadeOutCommand.Execute(null);
        AppHarness.Settle(1200);
        app.Play(0.1);
        Assert.False(deck1.IsPlaying);
        Assert.Empty(app.ViewModel.History.Plays);

        await OnAir(app, app.ToneTrack("Short tag", 12));
        Listen(app, 13);

        Assert.Equal(["Short tag"], app.ViewModel.History.Plays.Select(p => p.Title));
    }

    [AvaloniaFact]
    public async Task Seeking_does_not_count_as_listening()
    {
        using var app = new AppHarness();
        DeckViewModel deck1 = await OnAir(app, app.ToneTrack("Long", 120));
        Listen(app, 5);

        // Jump ahead while out of the room, then bring it back: only real listening counts.
        app.ViewModel.Mixer!.SetNow(1);
        deck1.SeekTo(0.8);
        app.ViewModel.Mixer.SetNow(0);
        Listen(app, 5);

        Assert.Empty(app.ViewModel.History.Plays);
    }

    [AvaloniaFact]
    public async Task Auto_play_builds_the_set_list_in_order()
    {
        using var app = new AppHarness();
        app.Settings.GapSeconds = 0;
        app.ViewModel.Queue.Add([app.ToneTrack("First", 31), app.ToneTrack("Second", 31), app.ToneTrack("Third", 31)]);
        app.ViewModel.AutoPlay!.IsOn = true;

        Listen(app, 100);

        Assert.Equal(["First", "Second", "Third"], app.ViewModel.History.Plays.Select(p => p.Title));
        Assert.Equal(["1", "2", "1"], app.ViewModel.History.Plays.Select(p => p.Record.Deck));
    }

    [AvaloniaFact]
    public async Task History_is_saved_and_a_restart_continues_the_session()
    {
        using var app = new AppHarness();
        await OnAir(app, app.ToneTrack("Before the restart", 40));
        Listen(app, 31);

        // GZJam restarted mid-gig: a fresh history view over the same database.
        var reopened = new HistoryViewModel(app.Database);

        Assert.Equal("This session · 1 song", reopened.SelectedSession!.Label);
        Assert.Equal(["Before the restart"], reopened.Plays.Select(p => p.Title));
    }

    [AvaloniaFact]
    public void Past_sessions_are_listed_by_date_and_can_be_opened()
    {
        using var app = new AppHarness();
        var saturday = new DateTime(2026, 10, 17, 19, 42, 0, DateTimeKind.Local).ToUniversalTime();
        PlaySession past = app.Database.StartOrContinueSession(saturday, HistoryViewModel.ContinueWithin);
        Track song = app.ToneTrack("Last week's opener");
        app.Database.AddPlay(past.Id, song, saturday, "1", 0);

        var history = new HistoryViewModel(app.Database);

        Assert.Equal(["This session", "Sat Oct 17, 2026 · 7:42 PM · 1 song"], history.Sessions.Select(s => s.Label));
        Assert.Empty(history.Plays);   // nothing played tonight yet

        history.SelectedSession = history.Sessions[1];
        PlayItem play = Assert.Single(history.Plays);
        Assert.Equal("Last week's opener", play.Title);
        Assert.Equal("7:42 PM", play.TimeText);
    }

    [AvaloniaFact]
    public async Task Tabs_switch_between_queue_and_history()
    {
        using var app = new AppHarness();
        Assert.True(app.ViewModel.ShowQueue);

        app.ViewModel.ShowHistory = true;
        AppHarness.Settle(100);

        Assert.True(app.Window.FindControl<Avalonia.Controls.ListBox>("HistoryList")!.IsEffectivelyVisible);
        Assert.False(app.Window.FindControl<Avalonia.Controls.ListBox>("QueueList")!.IsEffectivelyVisible);
        await Task.CompletedTask;
    }
}
