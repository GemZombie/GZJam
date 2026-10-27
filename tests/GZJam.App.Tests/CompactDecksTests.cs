using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using GZJam.App.Views;

namespace GZJam.App.Tests;

/// <summary>Compact decks (#128): smaller decks for more songs, switched by the top bar, a key or the grip.</summary>
public sealed class CompactDecksTests
{
    private static double HeightOf(AppHarness app, string name) => app.Window.FindControl<Control>(name)!.Bounds.Height;

    [AvaloniaFact]
    public void Compact_decks_halve_the_decks_and_mixer_and_give_the_library_the_room()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        double deck = HeightOf(app, "Deck1View"), mixer = HeightOf(app, "MixerPanel"), songs = HeightOf(app, "TrackGrid");

        vm.ToggleCompactDecksCommand.Execute(null);
        AppHarness.Settle(150);
        app.Screenshot("compact-decks");

        Assert.True(app.Settings.CompactDecks);   // remembered
        Assert.Equal(DeckView.CompactHeight, HeightOf(app, "Deck1View"), 0);
        Assert.Equal(DeckView.CompactHeight, HeightOf(app, "Deck2View"), 0);
        Assert.Equal(HeightOf(app, "Deck1View"), HeightOf(app, "MixerPanel"), 0);   // the mixer doesn't hold the row up
        Assert.True(mixer > HeightOf(app, "MixerPanel"));
        Assert.True(HeightOf(app, "TrackGrid") >= songs + deck - DeckView.CompactHeight - 1, "the library gets the room");
        Assert.False(app.Window.FindControl<Control>("PhonesMixRow")!.IsVisible);
        Assert.Equal($"Compact decks, more songs · {(OperatingSystem.IsMacOS() ? "Cmd" : "Ctrl")}+D", vm.CompactDecksTip);   // Cmd on a Mac

        // The side keys sit either side of the crossfader, on its row.
        var crossfader = app.Window.FindControl<Control>("Crossfader")!;
        var left = app.Window.FindControl<Control>("LeftSideButton")!;
        Assert.Equal(Grid.GetRow(crossfader), Grid.GetRow(left));
        Assert.True(crossfader.Bounds.Width > 60);

        vm.ToggleCompactDecksCommand.Execute(null);
        AppHarness.Settle(150);
        Assert.Equal(deck, HeightOf(app, "Deck1View"), 0);
        Assert.Equal(mixer, HeightOf(app, "MixerPanel"), 0);
        Assert.True(app.Window.FindControl<Control>("PhonesMixRow")!.IsVisible);
    }

    [AvaloniaFact]
    public void Ctrl_d_switches_them_and_the_settings_switch_follows()
    {
        using var app = new AppHarness();
        double full = HeightOf(app, "Deck1View");
        app.Window.KeyPress(Key.D, OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control, PhysicalKey.D, "d");
        AppHarness.Settle(100);
        Assert.True(app.ViewModel.CompactDecks);
        Assert.True(app.ViewModel.Settings.CompactDecks);

        app.ViewModel.Settings.CompactDecks = false;   // Settings › Decks
        AppHarness.Settle(100);
        Assert.Equal(full, HeightOf(app, "Deck1View"), 0);
    }

    [AvaloniaFact]
    public void The_grip_under_the_decks_drags_them_smaller_and_bigger_and_a_tap_switches()
    {
        using var app = new AppHarness();
        Point Grip()
        {
            var grip = app.Window.FindControl<Control>("DeckGrip")!;
            return grip.TranslatePoint(new Point(grip.Bounds.Width / 2, grip.Bounds.Height / 2), app.Window)!.Value;
        }

        Point at = Grip();
        app.MouseDrag(at, at - new Point(0, 60));
        Assert.True(app.ViewModel.CompactDecks);

        at = Grip();
        app.MouseDrag(at, at + new Point(0, 60));
        Assert.False(app.ViewModel.CompactDecks);

        at = Grip();
        app.Window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
        app.Window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
        AppHarness.Settle(100);
        Assert.True(app.ViewModel.CompactDecks);
    }

    [AvaloniaFact]
    public void Four_compact_decks_lose_their_waveforms_and_stay_shorter_than_the_full_ones()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        vm.Settings.FourDecks = true;
        vm.LoadToDeckAsync(vm.Deck1!, app.ToneTrack("Tone A")).GetAwaiter().GetResult();
        vm.CompactDecks = true;
        AppHarness.Settle(150);
        app.Screenshot("compact-four-decks");

        var deck1 = app.Window.FindControl<DeckView>("Deck1View")!;
        Assert.True(deck1.IsMini);
        Assert.False(deck1.FindControl<Control>("WavePanel")!.IsVisible);
        Assert.True(app.Window.FindControl<Control>("Deck3View")!.IsVisible);
        double side = HeightOf(app, "LeftDecks");
        Assert.True(side < DeckView.FullHeight - 60, $"four compact decks take {side} px");
        Assert.True(HeightOf(app, "Deck1View") < DeckView.CompactHeight, "each, less than a compact deck");
        Assert.Equal(side, HeightOf(app, "MixerPanel"), 0);   // the mixer fills the row beside them
    }

    [AvaloniaFact]
    public void Touch_mode_compact_decks_keep_the_mixer_within_the_decks()
    {
        using var app = new AppHarness();
        app.ViewModel.Settings.TouchMode = true;
        app.ViewModel.CompactDecks = true;
        AppHarness.Settle(150);
        app.Screenshot("compact-decks-touch");
        Assert.Equal(HeightOf(app, "Deck1View"), HeightOf(app, "MixerPanel"), 0);
        Assert.Equal(40, HeightOf(app, "Crossfader"), 0);
    }
}
