using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GZJam.App.Controls;
using GZJam.App.Views;

namespace GZJam.App.Tests;

/// <summary>The on-air bar fills as the song the room hears plays (#165).</summary>
public sealed class AirBarTests
{
    [AvaloniaFact]
    public void It_fills_to_where_the_music_ends_and_shows_on_air_only()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var deck = vm.Deck1!;
        // 40 seconds of music, then 20 of silence, trimmed automatically.
        vm.LoadToDeckAsync(deck, app.ToneTrack("Song", seconds: 40, silenceAfter: 20)).GetAwaiter().GetResult();
        AppHarness.WaitFor(() => deck.Deck.End < TimeSpan.FromSeconds(45));
        var bar = app.Window.FindControl<DeckView>("Deck1View")!.FindControl<AirBar>("AirBar")!;
        Assert.False(bar.IsVisible);   // not on air yet

        vm.Mixer!.Crossfader = 0;
        deck.TogglePlayCommand.Execute(null);
        app.Play(0.1);
        Assert.True(bar.IsVisible);
        Assert.InRange(bar.Progress, 0, 0.05);

        // Halfway through the music, not through the file.
        double end = deck.Deck.End.TotalSeconds;
        deck.Deck.Seek(TimeSpan.FromSeconds(end / 2));
        app.Play(0.1);
        Assert.InRange(bar.Progress, 0.45, 0.55);

        // Full as the time left runs out.
        deck.Deck.Seek(TimeSpan.FromSeconds(end - 0.2));
        app.Play(0.1);
        Assert.InRange(bar.Progress, 0.98, 1);

        // In the headphones only, no bar.
        deck.Deck.Seek(TimeSpan.FromSeconds(5));
        vm.Mixer.Crossfader = 1;
        app.Play(0.1);
        Assert.False(bar.IsVisible);
    }

    [AvaloniaFact]
    public void Faster_tempo_is_counted_as_the_time_left_is()
    {
        using var app = new AppHarness();
        var deck = app.ViewModel.Deck1!;
        app.ViewModel.LoadToDeckAsync(deck, app.ToneTrack("Song", seconds: 40)).GetAwaiter().GetResult();
        deck.TempoPercent = 8;
        deck.Deck.Seek(TimeSpan.FromSeconds(20));
        app.Play(0.05);
        // Half the song in either measure: the speed divides both sides alike.
        Assert.InRange(deck.AirProgress, 0.48, 0.52);
        double played = (deck.Deck.Position - deck.Deck.Start).TotalSeconds / 1.08;
        Assert.Equal(played / (played + deck.Deck.Remaining.TotalSeconds), deck.AirProgress, 3);
    }
}
