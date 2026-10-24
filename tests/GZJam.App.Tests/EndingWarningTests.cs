using Avalonia.Headless.XUnit;

namespace GZJam.App.Tests;

/// <summary>The readout blinks in the last 30 seconds of the song the room hears, as on CDJs.</summary>
public sealed class EndingWarningTests
{
    [AvaloniaFact]
    public void The_readout_blinks_in_the_last_30_seconds_on_air_only()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var deck = vm.Deck1!;
        vm.LoadToDeckAsync(deck, app.ToneTrack("Song", seconds: 60)).GetAwaiter().GetResult();
        vm.Mixer!.Crossfader = 0;
        deck.TogglePlayCommand.Execute(null);
        app.Play(0.2);
        Assert.True(deck.IsOnAir);
        Assert.False(deck.IsEnding);   // a minute left

        deck.Deck.Seek(TimeSpan.FromSeconds(35));
        app.Play(0.1);
        Assert.True(deck.IsEnding);

        // Slowly: dimmed for half of each second.
        var seen = new HashSet<bool>();
        for (int i = 0; i < 40; i++)
        {
            app.Play(0.05);
            seen.Add(deck.IsReadoutDimmed);
        }
        Assert.Equal([false, true], seen.Order());

        // Off air (only in the headphones), it doesn't nag.
        vm.Mixer.Crossfader = 1;
        app.Play(0.1);
        Assert.False(deck.IsEnding);
        Assert.False(deck.IsReadoutDimmed);
    }

    [AvaloniaFact]
    public void Counted_to_where_the_music_ends_not_the_end_of_the_file()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var deck = vm.Deck1!;
        // 40 seconds of music, then 20 of silence, trimmed automatically.
        vm.LoadToDeckAsync(deck, app.ToneTrack("Song", seconds: 40, silenceAfter: 20)).GetAwaiter().GetResult();
        AppHarness.WaitFor(() => deck.Deck.End < TimeSpan.FromSeconds(45));
        vm.Mixer!.Crossfader = 0;
        deck.TogglePlayCommand.Execute(null);
        deck.Deck.Seek(TimeSpan.FromSeconds(15));   // 45 s from the end of the file, 25 from the end of the music
        app.Play(0.1);
        Assert.True(deck.IsEnding);
    }
}
