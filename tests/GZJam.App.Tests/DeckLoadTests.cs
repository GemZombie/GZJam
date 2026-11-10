using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.Audio;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>
/// Loading a deck (#361): the song is read and opened off the window's thread, so a slow or
/// sleeping drive never freezes the screen, and the deck is looked at again once it's ready.
/// </summary>
public sealed class DeckLoadTests
{
    /// <summary>Reads <paramref name="slowPath"/> as a sleeping drive would: after <paramref name="delay"/>.</summary>
    private static void SlowDrive(AppHarness app, string slowPath, TimeSpan delay) =>
        app.Mix.Songs.Reader = path =>
        {
            if (path == slowPath)
                Thread.Sleep(delay);
            return SongCache.ReadFile(path);
        };

    [AvaloniaFact]
    public void A_song_on_a_slow_drive_loads_without_freezing_the_window()
    {
        using var app = new AppHarness();
        DeckViewModel deck = app.ViewModel.Deck1!;
        Track song = app.ToneTrack("On a sleeping drive");
        SlowDrive(app, song.Path, TimeSpan.FromSeconds(1.5));

        var asked = System.Diagnostics.Stopwatch.StartNew();
        Task<bool> loading = app.ViewModel.LoadToDeckAsync(deck, song);

        Assert.True(asked.ElapsedMilliseconds < 500, $"The window waited {asked.ElapsedMilliseconds} ms for the drive");
        Assert.False(loading.IsCompleted);
        Assert.Null(deck.Track);
        Assert.True(AppHarness.Await(loading));
        Assert.Same(song, deck.Track);
        Assert.True(deck.Deck.IsLoaded);
    }

    [AvaloniaFact]
    public void The_song_chosen_last_is_the_one_on_the_deck()
    {
        using var app = new AppHarness();
        DeckViewModel deck = app.ViewModel.Deck1!;
        Track slow = app.ToneTrack("Chosen first, slow to read"), quick = app.ToneTrack("Chosen second");
        SlowDrive(app, slow.Path, TimeSpan.FromSeconds(1));

        Task<bool> first = app.ViewModel.LoadToDeckAsync(deck, slow);
        Task<bool> second = app.ViewModel.LoadToDeckAsync(deck, quick);

        Assert.True(AppHarness.Await(second));
        Assert.False(AppHarness.Await(first));   // overtaken: it doesn't land on top
        Assert.Same(quick, deck.Track);
        Assert.Equal(quick.Path, deck.Deck.TrackPath);
    }

    [AvaloniaFact]
    public void A_deck_that_goes_on_air_while_a_song_opens_isnt_replaced()
    {
        using var app = new AppHarness();
        DeckViewModel deck = app.ViewModel.Deck1!;
        Track playing = app.ToneTrack("Playing", 60), next = app.ToneTrack("Next, slow to read");
        Assert.True(AppHarness.Await(app.ViewModel.LoadToDeckAsync(deck, playing)));
        SlowDrive(app, next.Path, TimeSpan.FromSeconds(1));

        Task<bool> loading = app.ViewModel.LoadToDeckAsync(deck, next);   // paused: allowed
        deck.TogglePlayCommand.Execute(null);                             // and now the room hears it
        Assert.True(deck.IsOnAir);

        Assert.False(AppHarness.Await(loading));
        Assert.Same(playing, deck.Track);
        Assert.True(deck.IsPlaying);
        Assert.Contains("went on air", app.ViewModel.Notice);
    }

    [AvaloniaFact]
    public void Ejecting_while_a_song_opens_leaves_the_deck_empty()
    {
        using var app = new AppHarness();
        DeckViewModel deck = app.ViewModel.Deck1!;
        Track slow = app.ToneTrack("Slow to read");
        SlowDrive(app, slow.Path, TimeSpan.FromSeconds(1));

        Task<bool> loading = app.ViewModel.LoadToDeckAsync(deck, slow);
        deck.EjectCommand.Execute(null);

        Assert.False(AppHarness.Await(loading));
        Assert.Null(deck.Track);
        Assert.False(deck.Deck.IsLoaded);
    }
}
