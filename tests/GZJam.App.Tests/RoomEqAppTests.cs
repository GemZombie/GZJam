using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.App.Views;
using GZJam.Audio;

namespace GZJam.App.Tests;

/// <summary>
/// The room EQ for the venue you're at (#272): the key under MAIN rolls it down, a venue holds
/// its EQ, and the speakers (never the headphones) get it, as three bands or ten.
/// </summary>
public sealed class RoomEqAppTests
{
    [AvaloniaFact]
    public void A_soundcheck_in_a_muddy_room_with_both_views()
    {
        using var app = new AppHarness();
        RoomEqViewModel room = app.ViewModel.RoomEq!;
        var key = app.Window.FindControl<ToggleButton>("RoomEqKey")!;
        Assert.True(key.IsVisible);
        Assert.Equal(RoomEqViewModel.NoVenue, room.VenueName);
        Assert.True(app.Mix.Main.RoomEq.IsFlat);

        // The key rolls the panel down.
        key.IsChecked = true;
        AppHarness.Settle(150);
        Assert.True(room.IsShown);
        Assert.True(app.Window.FindControl<Panel>("RoomEqRow")!.IsVisible);
        app.Screenshot("room-eq-no-venue");

        Assert.Equal("DC", room.NewVenue("  DC "));
        Assert.Null(room.NewVenue("dc"));   // taken, whatever the case
        Assert.Equal("DC", room.VenueName);
        Assert.Equal([RoomEqViewModel.NoVenue, "DC"], room.VenueChoices);
        AppHarness.Settle(100);
        Assert.Equal("DC", app.Window.FindControl<RoomEqPanel>("RoomEqPanel")!.FindControl<ComboBox>("VenueBox")!.SelectedItem);

        var chipRoom = app.Window.FindControl<StackPanel>("MainChipRoom")!;
        Assert.False(chipRoom.IsVisible);   // flat: the MAIN chip as ever

        room.PresetCommand.Execute("boomy2");
        AppHarness.Settle(150);
        Assert.True(room.IsOn);
        Assert.True(chipRoom.IsVisible);   // the room the speakers are set up for, always in view
        Assert.Equal("DC", app.Window.FindControl<TextBlock>("MainChipVenue")!.Text);
        Assert.Contains("DC", room.KeyTip);
        Assert.Equal(-6, app.Mix.Main.RoomEq.Low);
        Assert.Equal(250, app.Mix.Main.RoomEq.MidHz);
        Assert.True(app.Mix.Cue.RoomEq.IsFlat);   // never the headphones
        room.MoveMidCommand.Execute("up");
        Assert.Equal(315, app.Mix.Main.RoomEq.MidHz);
        app.Screenshot("room-eq-simple");

        // Advanced starts with the same curve, so nothing heard changes.
        RoomEq simple = app.Mix.Main.RoomEq;
        room.ShowAdvancedCommand.Execute(null);
        AppHarness.Settle(150);
        Assert.Equal(RoomEqView.Advanced, app.Mix.Main.RoomEq.View);
        foreach (double hz in RoomEq.BandHz)
            Assert.Equal(simple.ResponseDb(hz), app.Mix.Main.RoomEq.ResponseDb(hz), 0.5);
        app.Screenshot("room-eq-advanced");

        // Back to Simple untouched: the three bands again.
        room.ShowSimpleCommand.Execute(null);
        Assert.Equal(RoomEqView.Simple, app.Mix.Main.RoomEq.View);
        Assert.False(room.IsSetInAdvanced);

        // A band set on its own: Simple can't show it, and says so, while the room keeps hearing it.
        room.ShowAdvancedCommand.Execute(null);
        room.Bands[7].Value = -4;   // 4 kHz, a harsh spot
        room.ShowSimpleCommand.Execute(null);
        AppHarness.Settle(150);
        Assert.True(room.IsSetInAdvanced);
        Assert.Equal(RoomEqView.Advanced, app.Mix.Main.RoomEq.View);
        Assert.Equal(-4, app.Mix.Main.RoomEq.Bands[7], 0.01);
        app.Screenshot("room-eq-set-in-advanced");
        room.UseThreeBandsCommand.Execute(null);
        Assert.False(room.IsSetInAdvanced);
        Assert.Equal(RoomEqView.Simple, app.Mix.Main.RoomEq.View);
        Assert.Equal(315, app.Mix.Main.RoomEq.MidHz);

        // No venue: flat, and the venue keeps its EQ for next time.
        room.SelectedVenue = RoomEqViewModel.NoVenue;
        AppHarness.Settle(100);
        Assert.True(app.Mix.Main.RoomEq.IsFlat);
        Assert.False(room.IsOn);
        Assert.False(chipRoom.IsVisible);
        room.SelectedVenue = "DC";
        Assert.Equal(-6, app.Mix.Main.RoomEq.Low);

        // Put away: the EQ stays on the speakers.
        key.IsChecked = false;
        AppHarness.Settle(150);
        Assert.False(app.Window.FindControl<Panel>("RoomEqRow")!.IsVisible);
        Assert.Equal(-6, app.Mix.Main.RoomEq.Low);
    }

    [AvaloniaFact]
    public void The_venue_comes_back_after_a_restart_and_says_so()
    {
        string work;
        using (var first = new AppHarness())
        {
            work = first.WorkDirectory;
            first.ViewModel.RoomEq!.NewVenue("The Grand Hall");
            first.ViewModel.RoomEq.PresetCommand.Execute("boomy1");
            first.ViewModel.RoomEq.NewVenue("Studio B");
            first.ViewModel.RoomEq.SelectedVenue = "The Grand Hall";
            string path = Path.Combine(work, "settings.json");
            first.Settings.Save(path);

            using var second = new AppHarness(workDirectory: work, settings: AppSettings.Load(path), start: false);
            second.Start();
            RoomEqViewModel room = second.ViewModel.RoomEq!;
            Assert.Equal("The Grand Hall", room.VenueName);
            Assert.Equal(-3, second.Mix.Main.RoomEq.Low);
            Assert.Equal(-2, second.Mix.Main.RoomEq.Mid);
            Assert.Equal([RoomEqViewModel.NoVenue, "Studio B", "The Grand Hall"], room.VenueChoices);
            Assert.Equal("Venue: The Grand Hall, its room EQ on the speakers", second.ViewModel.Notice);
        }   // the first harness deletes its folder
    }

    [AvaloniaFact]
    public void Renaming_and_deleting_a_venue()
    {
        using var app = new AppHarness();
        RoomEqViewModel room = app.ViewModel.RoomEq!;
        room.NewVenue("Hot Jam");
        room.NewVenue("Lindy Loft");
        room.PresetCommand.Execute("padded2");

        Assert.False(room.RenameVenue("hot jam"));   // taken
        Assert.True(room.RenameVenue("The Loft"));
        Assert.Equal("The Loft", room.VenueName);
        Assert.Equal(4, app.Mix.Main.RoomEq.Low);
        Assert.False(room.RenameVenue(RoomEqViewModel.NoVenue));

        room.DeleteVenue();
        Assert.Equal(RoomEqViewModel.NoVenue, room.VenueName);
        Assert.True(app.Mix.Main.RoomEq.IsFlat);
        Assert.Equal([RoomEqViewModel.NoVenue, "Hot Jam"], room.VenueChoices);
    }

    [AvaloniaFact]
    public void Beside_compact_decks_the_key_gives_way_to_the_crossfader()
    {
        using var app = new AppHarness();
        var key = app.Window.FindControl<ToggleButton>("RoomEqKey")!;
        Assert.True(key.IsVisible);

        app.ViewModel.CompactDecks = true;
        AppHarness.Settle(150);
        Assert.False(key.IsVisible);   // the crossfader sits between 1 and 2 there

        app.ViewModel.CompactDecks = false;
        AppHarness.Settle(150);
        Assert.True(key.IsVisible);
    }
}
