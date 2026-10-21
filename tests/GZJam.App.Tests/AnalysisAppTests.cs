using System.Globalization;
using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>Track analysis in the app: waveforms on the decks, silence trimming, detected BPMs.</summary>
public sealed class AnalysisAppTests
{
    [AvaloniaFact]
    public void Loading_shows_the_waveform_and_skips_the_silence_at_the_top()
    {
        using var app = new AppHarness();
        DeckViewModel deck = app.ViewModel.Deck1!;
        Track track = app.ToneTrack("Padded", seconds: 5, silenceBefore: 2, silenceAfter: 3);

        app.ViewModel.LoadToDeckAsync(deck, track).GetAwaiter().GetResult();
        AppHarness.WaitFor(() => deck.Waveform is not null);

        Assert.False(deck.IsAnalyzing);
        Assert.InRange(deck.Deck.Position.TotalSeconds, 1.9, 2.0);   // ready at the first note
        Assert.InRange(deck.TrimStart, 0.19, 0.2);
        Assert.InRange(deck.TrimEnd, 0.7, 0.735);
        Assert.Equal("-0:05", deck.ReadoutText);   // time left is music, not trailing silence
        Assert.NotNull(app.Database.GetAnalysis(track.Path, FileVersion.Of(track.Path)!.Value));   // saved for next time
    }

    [AvaloniaFact]
    public void The_song_ends_where_the_music_ends()
    {
        using var app = new AppHarness();
        DeckViewModel deck = app.ViewModel.Deck1!;
        Track track = app.ToneTrack("Padded", seconds: 2, silenceAfter: 4);
        app.ViewModel.LoadToDeckAsync(deck, track).GetAwaiter().GetResult();
        AppHarness.WaitFor(() => deck.Waveform is not null);

        deck.TogglePlayCommand.Execute(null);
        for (int i = 0; i < 40 && !deck.IsFinished; i++)
            app.Play(0.1);

        Assert.True(deck.IsFinished);   // so auto-play's gap starts now, not after 4 s of silence
        Assert.InRange(deck.Deck.Position.TotalSeconds, 2, 2.6);
    }

    [AvaloniaFact]
    public void A_detected_bpm_shows_in_the_queue_and_fits_the_chosen_range()
    {
        using var app = new AppHarness();
        Track drums = app.BeatTrack("Drums", bpm: 180);
        app.ViewModel.Queue.Add([drums]);
        QueueItem item = app.ViewModel.Queue.Items[0];

        AppHarness.WaitFor(() => item.BpmText != "");
        Assert.InRange(double.Parse(item.BpmText, CultureInfo.CurrentCulture), 177, 183);

        // "My music is 60 to 120": 180 can only be double time, so it shows as 90.
        app.ViewModel.Settings.BpmMax = 120;
        app.ViewModel.Settings.BpmMin = 60;

        Assert.InRange(double.Parse(item.BpmText, CultureInfo.CurrentCulture), 88, 92);
        Assert.Equal((60, 120), (app.Settings.BpmMin, app.Settings.BpmMax));
    }
}
