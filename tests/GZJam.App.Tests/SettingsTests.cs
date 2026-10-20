namespace GZJam.App.Tests;

public sealed class SettingsTests
{
    [Fact]
    public void Readout_choices_saved_under_the_old_deck_letters_carry_over()
    {
        string path = Path.Combine(Directory.CreateTempSubdirectory("gzjam-settings-").FullName, "settings.json");
        File.WriteAllText(path, """
            { "DeckReadouts": { "A": "Bpm", "B": "Elapsed" }, "LiveDeckPause": "Hold" }
            """);

        AppSettings settings = AppSettings.Load(path);

        Assert.Equal(DeckReadout.Bpm, settings.DeckReadouts["1"]);
        Assert.Equal(DeckReadout.Elapsed, settings.DeckReadouts["2"]);
        Assert.False(settings.DeckReadouts.ContainsKey("A"));
        Assert.Equal(OnAirPause.Tap, settings.OnAirPause);   // the old hold setting no longer applies
    }

    [Fact]
    public void Settings_round_trip()
    {
        string path = Path.Combine(Directory.CreateTempSubdirectory("gzjam-settings-").FullName, "settings.json");
        var settings = new AppSettings { LoadOntoPlayingDeck = PlayingDeckLoad.Ask, OnAirPause = OnAirPause.TapTwice, FadeOutSeconds = 8 };
        settings.DeckReadouts["2"] = DeckReadout.Bpm;

        settings.Save(path);
        AppSettings loaded = AppSettings.Load(path);

        Assert.Equal(PlayingDeckLoad.Ask, loaded.LoadOntoPlayingDeck);
        Assert.Equal(OnAirPause.TapTwice, loaded.OnAirPause);
        Assert.Equal(8, loaded.FadeOutSeconds);
        Assert.Equal(DeckReadout.Bpm, loaded.DeckReadouts["2"]);
    }
}
