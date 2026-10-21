using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.Audio;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>Loudness matching on the decks, the DJ's own gain, and the setting.</summary>
public sealed class LoudnessAppTests
{
    private static DeckViewModel LoadAndMeasure(AppHarness app, DeckViewModel deck, Track track)
    {
        app.ViewModel.LoadToDeckAsync(deck, track).GetAwaiter().GetResult();
        AppHarness.WaitFor(() => deck.Waveform is not null, TimeSpan.FromSeconds(30));
        return deck;
    }

    [AvaloniaFact]
    public void A_loud_track_is_turned_down_and_a_quiet_one_up()
    {
        using var app = new AppHarness(settings: new AppSettings());   // matching on, as by default
        var vm = app.ViewModel;
        // 440 Hz tones: at 0.5 about -6.7 LUFS, at 0.03 about -31 LUFS with plenty of headroom.
        var loud = LoadAndMeasure(app, vm.Deck1!, app.ToneTrack("Loud remaster"));
        var quiet = LoadAndMeasure(app, vm.Deck2!, app.ToneTrack("Quiet 78", amplitude: 0.03));

        Assert.InRange(loud.Deck.GainDb, -10, -8.5);
        Assert.Equal(LoudnessMatch.MaxBoostDb, quiet.Deck.GainDb, 1);
        Assert.Matches(@"^−6\.\d LUFS · matching −9\.\d dB$", loud.LevelText);
    }

    [AvaloniaFact]
    public void The_djs_gain_adds_to_matching_and_is_remembered()
    {
        using var app = new AppHarness(settings: new AppSettings());   // matching on, as by default
        var vm = app.ViewModel;
        var song = app.ToneTrack("Song");
        var deck = LoadAndMeasure(app, vm.Deck1!, song);
        double matched = deck.Deck.GainDb;

        deck.EqGain = 2.3;

        Assert.Equal(2.5, deck.EqGain);   // half-dB steps
        Assert.Equal(matched + 2.5, deck.Deck.GainDb, 2);
        Assert.True(deck.IsEqSet);
        Assert.Equal(2.5, app.Database.GetEq(song.Path).Gain);

        var again = LoadAndMeasure(app, vm.Deck2!, song);
        Assert.Equal(matched + 2.5, again.Deck.GainDb, 2);
    }

    [AvaloniaFact]
    public void Turning_matching_off_eases_the_level_back_on_a_song_the_room_is_hearing()
    {
        using var app = new AppHarness(settings: new AppSettings());   // matching on, as by default
        var vm = app.ViewModel;
        var deck = LoadAndMeasure(app, vm.Deck1!, app.ToneTrack("Loud remaster", seconds: 30));
        deck.TogglePlayCommand.Execute(null);
        app.Play(0.1);
        double matched = deck.Deck.GainDb;
        Assert.True(matched < -8);

        vm.Settings.MatchLoudness = false;
        app.Play(0.1);
        Assert.InRange(deck.Deck.GainDb, matched, matched + 1);   // no jump: about 3 dB a second

        for (int i = 0; i < 150 && deck.Deck.GainDb < -0.01; i++)
        {
            app.Play(0.033);
            Thread.Sleep(30);
        }
        Assert.Equal(0, deck.Deck.GainDb, 2);
        Assert.EndsWith("loudness matching off", deck.LevelText);
        Assert.False(app.Settings.MatchLoudness);
    }

    [AvaloniaFact]
    public void Off_air_matching_applies_at_once()
    {
        using var app = new AppHarness(settings: new AppSettings());   // matching on, as by default
        app.Settings.MatchLoudness = false;
        var deck = LoadAndMeasure(app, app.ViewModel.Deck2!, app.ToneTrack("Loud remaster"));
        Assert.Equal(0, deck.Deck.GainDb, 2);

        app.ViewModel.Settings.MatchLoudness = true;

        Assert.InRange(deck.Deck.GainDb, -10, -8.5);
    }
}
