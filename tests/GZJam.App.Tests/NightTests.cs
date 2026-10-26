using GZJam.App.ViewModels;
using Avalonia.Headless.XUnit;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>
/// Bringing the night back (#96): after a crash the queue and decks come back as they were,
/// paused; after a normal close too, with the decks at the start of their songs (#117). Two runs
/// of GZJam on one data folder.
/// </summary>
public sealed class NightTests : IDisposable
{
    private readonly string _work = Directory.CreateTempSubdirectory("gzjam-night-test-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_work, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    /// <summary>The first night: three songs queued, Deck 1 playing a tone at +2%, cued, for a second.</summary>
    private static async Task<(List<string> Queue, string OnDeck, TimeSpan At)> ANight(AppHarness app)
    {
        AppHarness.WaitFor(() => File.Exists(app.ViewModel.NightPath));   // kept from the start
        var songs = app.ViewModel.Library.Results.Take(3).ToList();
        app.ViewModel.Queue.Add(songs);
        DeckViewModel deck = app.ViewModel.Deck1!;
        Track tone = app.ToneTrack("Mid-dance", seconds: 30);
        Assert.True(await app.ViewModel.LoadToDeckAsync(deck, tone));
        deck.TempoPercent = 2;
        deck.IsCueOn = true;
        deck.TogglePlayCommand.Execute(null);
        app.ViewModel.AutoPlay!.IsOn = true;
        app.Play(1.0);
        Assert.True(deck.IsPlaying);
        app.ViewModel.WriteNightNow();
        // Auto-play may have taken the next song onto Deck 2: what's queued is what was kept.
        return (app.ViewModel.Queue.Items.Select(i => i.Track.Path).ToList(), tone.Path, deck.Deck.Position);
    }

    [AvaloniaFact]
    public async Task After_a_crash_the_queue_and_decks_come_back_paused()
    {
        (List<string> queue, string onDeck, TimeSpan at) night;
        using (var first = new AppHarness(workDirectory: _work))
            night = await ANight(first);   // no normal close: as if it crashed

        using var app = new AppHarness(workDirectory: _work);
        AppHarness.WaitFor(() => app.ViewModel.Deck1!.HasTrack);
        app.Screenshot("night-back");

        Assert.Equal(night.queue, app.ViewModel.Queue.Items.Select(i => i.Track.Path));
        DeckViewModel deck = app.ViewModel.Deck1!;
        Assert.Equal(night.onDeck, deck.Track!.Path);
        Assert.False(deck.IsPlaying);   // never by itself
        Assert.Equal(night.at.TotalSeconds, deck.Deck.Position.TotalSeconds, 0.1);
        Assert.Equal(2, deck.TempoPercent);
        Assert.True(deck.IsCueOn);
        Assert.False(app.ViewModel.AutoPlay!.IsOn);
        Assert.False(app.ViewModel.Deck2!.IsPlaying);
        Assert.Equal("GZJam didn't close normally: the queue and decks are back as they were, paused. Auto-play is off until you turn it on.",
            app.ViewModel.Notice);
    }

    [AvaloniaFact]
    public async Task After_a_normal_close_the_queue_and_decks_come_back_at_the_start()
    {
        (List<string> queue, string onDeck, TimeSpan at) night;
        using (var first = new AppHarness(workDirectory: _work))
        {
            night = await ANight(first);
            first.ViewModel.CloseNight();
        }
        Assert.True(night.at > TimeSpan.FromSeconds(0.5));   // it was mid-song

        using var app = new AppHarness(workDirectory: _work);
        AppHarness.WaitFor(() => app.ViewModel.Deck1!.HasTrack && app.ViewModel.Notice is not null);

        Assert.Equal(night.queue, app.ViewModel.Queue.Items.Select(i => i.Track.Path));
        DeckViewModel deck = app.ViewModel.Deck1!;
        Assert.Equal(night.onDeck, deck.Track!.Path);
        Assert.False(deck.IsPlaying);
        Assert.Equal(deck.Deck.Start.TotalSeconds, deck.Deck.Position.TotalSeconds, 0.1);   // the start, not where it was
        Assert.Equal(2, deck.TempoPercent);
        Assert.True(deck.IsCueOn);
        Assert.False(app.ViewModel.AutoPlay!.IsOn);
        Assert.Equal($"The decks and queue from last time are back ({night.queue.Count} songs queued).", app.ViewModel.Notice);
    }

    [AvaloniaFact]
    public async Task After_a_normal_close_with_nothing_queued_the_decks_come_back()
    {
        string onDeck;
        using (var first = new AppHarness(workDirectory: _work))
        {
            AppHarness.WaitFor(() => File.Exists(first.ViewModel.NightPath));
            Track tone = first.ToneTrack("Next up", seconds: 30);
            Assert.True(await first.ViewModel.LoadToDeckAsync(first.ViewModel.Deck2!, tone));
            onDeck = tone.Path;
            first.ViewModel.CloseNight();
        }

        using var app = new AppHarness(workDirectory: _work);
        AppHarness.WaitFor(() => app.ViewModel.Deck2!.HasTrack && app.ViewModel.Notice is not null);

        Assert.Equal(onDeck, app.ViewModel.Deck2!.Track!.Path);
        Assert.False(app.ViewModel.Deck1!.HasTrack);
        Assert.Empty(app.ViewModel.Queue.Items);
        Assert.Equal("The decks from last time are back.", app.ViewModel.Notice);
    }

    [AvaloniaFact]
    public async Task A_song_thats_gone_since_is_left_out_with_a_note()
    {
        (List<string> queue, string onDeck, TimeSpan at) night;
        using (var first = new AppHarness(workDirectory: _work))
            night = await ANight(first);
        File.Delete(night.onDeck);

        using var app = new AppHarness(workDirectory: _work);
        AppHarness.WaitFor(() => app.ViewModel.Notice is not null);

        Assert.Equal(night.queue.Count, app.ViewModel.Queue.Items.Count);
        Assert.False(app.ViewModel.Deck1!.HasTrack);
        Assert.EndsWith("1 song couldn't be found (moved, or on a drive that isn't plugged in).", app.ViewModel.Notice);
    }

    [AvaloniaFact]
    public void A_first_start_says_nothing_and_starts_keeping_the_night()
    {
        using var app = new AppHarness(workDirectory: _work);
        AppHarness.WaitFor(() => File.Exists(app.ViewModel.NightPath));
        Assert.Null(app.ViewModel.Notice);
        Assert.Empty(app.ViewModel.Queue.Items);
        Assert.False(NightState.Load(app.ViewModel.NightPath)!.ClosedNormally);
    }

    [AvaloniaFact]
    public void A_change_to_the_queue_is_kept_within_a_moment()
    {
        using var app = new AppHarness(workDirectory: _work);
        AppHarness.WaitFor(() => File.Exists(app.ViewModel.NightPath));
        Track song = app.ViewModel.Library.Results[0];
        app.ViewModel.Queue.Add([song]);

        AppHarness.WaitFor(() =>
        {
            app.ViewModel.Tick();
            return NightState.Load(app.ViewModel.NightPath)?.Queue.Contains(song.Path) == true;
        }, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void A_damaged_night_file_is_ignored()
    {
        string path = Path.Combine(_work, NightState.FileName);
        File.WriteAllText(path, "{ not json");
        Assert.Null(NightState.Load(path));
    }
}
