using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.Audio;

namespace GZJam.App.Tests;

/// <summary>
/// A venue remembers its sound system's other settings too (#440): mono and the loudness target,
/// its own once changed with it loaded; until then, and with no venue, they're as in Settings.
/// </summary>
public sealed class VenueSoundTests
{
    private static DeckViewModel Loaded(AppHarness app)
    {
        DeckViewModel deck = app.ViewModel.Deck2!;
        AppHarness.Await(app.ViewModel.LoadToDeckAsync(deck, app.ToneTrack("Loud remaster")));
        AppHarness.WaitFor(() => deck.Waveform is not null, TimeSpan.FromSeconds(30));   // measured
        return deck;
    }

    [AvaloniaFact]
    public void A_venue_keeps_its_own_mono_and_loudness_target()
    {
        using var app = new AppHarness(settings: new AppSettings());   // mono, matching at -16: the defaults
        SettingsViewModel settings = app.ViewModel.Settings;
        RoomEqViewModel room = app.ViewModel.RoomEq!;
        DeckViewModel deck = Loaded(app);
        double usual = deck.Deck.GainDb;
        Assert.Null(settings.VenueNote);

        // A new venue starts as Settings has it.
        room.NewVenue("Grand Hall");
        Assert.True(app.Mix.Main.Mono);
        Assert.Equal(-16, settings.LoudnessTargetChoice.Value);
        Assert.Equal("Mono and Loudness target are saved for Grand Hall.", settings.VenueNote);

        // Changed with it loaded: the venue's own.
        settings.Mono = false;
        settings.LoudnessTargetChoice = settings.LoudnessTargetChoices.Single(c => c.Value == -20);
        Assert.False(app.Mix.Main.Mono);
        Assert.False(app.Mix.Cue.Mono);
        Assert.Equal(usual - 4, deck.Deck.GainDb, 1);
        Assert.True(app.Settings.Mono);                 // the no-venue settings untouched
        Assert.Equal(-16, app.Settings.LoudnessTarget);

        // No venue: as Settings had them, heard at once.
        room.SelectedVenue = RoomEqViewModel.NoVenue;
        Assert.True(app.Mix.Main.Mono);
        Assert.True(app.ViewModel.Mixer!.IsMono);
        Assert.Equal(usual, deck.Deck.GainDb, 1);
        Assert.Equal(-16, settings.LoudnessTargetChoice.Value);

        // Another venue never set: as Settings has them too.
        room.NewVenue("Studio B");
        Assert.True(app.Mix.Main.Mono);
        Assert.Equal(usual, deck.Deck.GainDb, 1);

        // Back to the hall: its own again.
        room.SelectedVenue = "Grand Hall";
        Assert.False(app.Mix.Main.Mono);
        Assert.Equal(usual - 4, deck.Deck.GainDb, 1);

        // Deleted while loaded: back to Settings' own.
        room.DeleteVenue();
        Assert.True(app.Mix.Main.Mono);
        Assert.Equal(usual, deck.Deck.GainDb, 1);
    }

    [AvaloniaFact]
    public void The_venue_loaded_at_startup_brings_its_mono_and_target()
    {
        var saved = new AppSettings
        {
            Venues = [new Venue { Name = "DC", Eq = new RoomEq { Low = -6 }, Mono = false, LoudnessTarget = -19.5 }],
            Venue = "DC",
        };
        AppSettings settings = AppSettings.Migrate(saved);
        Assert.Equal(-20, settings.Venues[0].LoudnessTarget);   // a hand-edited target: the nearest choice

        using var app = new AppHarness(settings: settings);
        Assert.False(app.Mix.Main.Mono);
        Assert.False(app.ViewModel.Mixer!.IsMono);
        Assert.Equal(-20, app.ViewModel.Settings.LoudnessTargetChoice.Value);
        Assert.Equal(-6, app.Mix.Main.RoomEq.Low);
    }
}
