using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>A song renamed or moved on disk while it's queued or on a deck (#87): they follow it.</summary>
public sealed class MovedSongAppTests
{
    [AvaloniaFact]
    public async Task The_queue_and_a_playing_deck_follow_a_renamed_song()
    {
        using var app = new AppHarness();
        long root = app.Database.GetRoots().Single().Id;
        string tone = GZJam.Testing.TestWav.WriteTone(Path.Combine(app.MusicPath, "Long Tone.wav"), TimeSpan.FromSeconds(30));
        app.ViewModel.Library.RescanRoot(root);
        AppHarness.WaitFor(() => app.ViewModel.Library.Results.Any(t => t.Path == tone), TimeSpan.FromSeconds(10));
        var songs = new List<Track> { app.ViewModel.Library.Results.First(t => t.Path != tone), app.ViewModel.Library.Results.Single(t => t.Path == tone) };
        app.ViewModel.Queue.Add([songs[0]]);
        DeckViewModel deck = app.ViewModel.Deck1!;
        Assert.True(await app.ViewModel.LoadToDeckAsync(deck, songs[1]));
        deck.TogglePlayCommand.Execute(null);
        app.Play(0.2);

        string queued = songs[0].Path.Replace(".mp3", " (renamed).mp3"), onDeck = songs[1].Path.Replace(".wav", " (renamed).wav");
        File.Move(songs[0].Path, queued);
        File.Move(songs[1].Path, onDeck);
        app.ViewModel.Library.RescanRoot(root);
        AppHarness.WaitFor(() => app.ViewModel.Queue.Items[0].Track.Path == queued, TimeSpan.FromSeconds(10));

        Assert.Equal(songs[0].DisplayTitle, app.ViewModel.Queue.Items[0].Title);
        Assert.Equal(onDeck, deck.Track!.Path);
        Assert.True(deck.IsPlaying);   // plays on

        // The queued one loads from its new name.
        Assert.True(await app.ViewModel.LoadToDeckAsync(app.ViewModel.Deck2!, app.ViewModel.Queue.Items[0].Track));
        Assert.Equal(queued, app.ViewModel.Deck2!.Track!.Path);
    }

    [AvaloniaFact]
    public void A_song_moved_to_another_music_folder_follows_too()
    {
        using var app = new AppHarness();
        Track song = app.ViewModel.Library.Results[0];
        app.ViewModel.Queue.Add([song]);
        string otherFolder = Path.Combine(app.WorkDirectory, "Other Music");
        Directory.CreateDirectory(otherFolder);
        AppHarness.Await(app.ViewModel.Library.AddRootAsync(otherFolder));

        string moved = Path.Combine(otherFolder, Path.GetFileName(song.Path));
        File.Move(song.Path, moved);
        foreach (LibraryRoot root in app.Database.GetRoots())
            app.ViewModel.Library.RescanRoot(root.Id);
        try
        {
            AppHarness.WaitFor(() => app.ViewModel.Queue.Items[0].Track.Path == moved, TimeSpan.FromSeconds(20));
        }
        catch (TimeoutException)
        {
            // Say where the move went: this once followed to the wrong song (the test songs are
            // alike in size and date), which a plain timeout hid.
            var inLibrary = app.Database.Search(null).Where(t => Path.GetFileName(t.Path) == Path.GetFileName(song.Path)).Select(t => t.Path);
            Assert.Fail($"The queue didn't follow the move. Queue: {app.ViewModel.Queue.Items[0].Track.Path}; moved to: {moved} " +
                        $"(exists: {File.Exists(moved)}); in the library: [{string.Join(" | ", inLibrary)}]; scan: {app.ViewModel.Library.ScanStatus ?? "none"}");
        }
    }
}
