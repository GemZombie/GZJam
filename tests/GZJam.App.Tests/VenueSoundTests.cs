using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.Audio;

namespace GZJam.App.Tests;

/// <summary>
/// A venue's own mono and loudness target (#440), set in its room EQ panel beside its EQ, so a
/// venue's sound is all in one place. Settings has the defaults: a venue starts "as in Settings".
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

    private static VenueChoice Target(RoomEqViewModel room, double lufs) => room.TargetChoices.Single(c => Equals(c.Value, lufs));

    [AvaloniaFact]
    public void A_venue_sets_its_own_mono_and_loudness_target_in_its_panel()
    {
        using var app = new AppHarness(settings: new AppSettings());   // mono, matching at -16: the defaults
        SettingsViewModel settings = app.ViewModel.Settings;
        RoomEqViewModel room = app.ViewModel.RoomEq!;
        DeckViewModel deck = Loaded(app);
        double usual = deck.Deck.GainDb;

        // A new venue: as in Settings, and says what that is.
        room.NewVenue("Grand Hall");
        Assert.Same(room.MonoChoices[0], room.MonoChoice);
        Assert.Equal("As in Settings: mono", room.MonoChoice.Label);
        Assert.Equal("As in Settings: usual", room.TargetChoice.Label);
        Assert.Null(settings.VenueNote);

        // Its own, set in its panel: heard at once.
        room.MonoChoice = room.MonoChoices.Single(c => Equals(c.Value, false));
        room.TargetChoice = Target(room, -20);
        Assert.False(app.Mix.Main.Mono);
        Assert.False(app.Mix.Cue.Mono);
        Assert.False(app.ViewModel.Mixer!.IsMono);
        Assert.Equal(usual - 4, deck.Deck.GainDb, 1);
        Assert.Equal("Grand Hall has its own mono and matched level (in its room EQ panel). These are the defaults.", settings.VenueNote);

        // Settings keeps the defaults, untouched; changing one the venue has its own of isn't heard.
        Assert.True(settings.Mono);
        Assert.Equal(-16, settings.LoudnessTargetChoice.Value);
        settings.LoudnessTargetChoice = settings.LoudnessTargetChoices.Single(c => c.Value == -18);
        Assert.Equal(usual - 4, deck.Deck.GainDb, 1);
        Assert.Equal("As in Settings: 2 dB lower", room.TargetChoices[0].Label);

        // No venue: the defaults, heard at once.
        room.SelectedVenue = RoomEqViewModel.NoVenue;
        Assert.True(app.Mix.Main.Mono);
        Assert.Equal(usual - 2, deck.Deck.GainDb, 1);

        // A venue that follows Settings follows a change there at once.
        room.NewVenue("Studio B");
        settings.LoudnessTargetChoice = settings.LoudnessTargetChoices.Single(c => c.Value == -16);
        Assert.Equal(usual, deck.Deck.GainDb, 1);
        settings.Mono = false;
        Assert.False(app.Mix.Main.Mono);
        settings.Mono = true;

        // Back to the hall: its own again; and back to "as in Settings".
        room.SelectedVenue = "Grand Hall";
        Assert.False(app.Mix.Main.Mono);
        Assert.Equal(usual - 4, deck.Deck.GainDb, 1);
        room.TargetChoice = room.TargetChoices[0];
        room.MonoChoice = room.MonoChoices[0];
        Assert.True(app.Mix.Main.Mono);
        Assert.Equal(usual, deck.Deck.GainDb, 1);
        Assert.Null(settings.VenueNote);
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
        RoomEqViewModel room = app.ViewModel.RoomEq!;
        Assert.False(app.Mix.Main.Mono);
        Assert.False(app.ViewModel.Mixer!.IsMono);
        Assert.Equal("Stereo", room.MonoChoice.Label);
        Assert.Equal("4 dB lower", room.TargetChoice.Label);
        Assert.True(app.ViewModel.Settings.Mono);                           // the defaults, as they were
        Assert.Equal(-16, app.ViewModel.Settings.LoudnessTargetChoice.Value);
        Assert.Equal(-6, app.Mix.Main.RoomEq.Low);
    }
}
