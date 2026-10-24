using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using GZJam.App.ViewModels;

namespace GZJam.App.Tests;

/// <summary>Search filters from the app: typed, or tapped in with the BPM button.</summary>
public sealed class SearchAppTests
{
    [Theory]
    [InlineData("basie", "bpm:130-150", "basie bpm:130-150")]
    [InlineData("basie bpm:100-160 jump", "~bpm:140", "basie jump ~bpm:140")]   // replaces, never stacks
    [InlineData("~bpm:180 rating:yes", null, "rating:yes")]
    [InlineData("", "bpm:60-110", "bpm:60-110")]
    public void A_bpm_filter_replaces_the_one_in_the_search_text(string text, string? filter, string expected) =>
        Assert.Equal(expected, LibraryViewModel.ReplaceBpmFilter(text, filter));

    [AvaloniaFact]
    public void The_bpm_button_finds_tracks_near_a_decks_tempo()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        vm.LoadToDeckAsync(vm.Deck1!, app.ToneTrack("Tone")).GetAwaiter().GetResult();   // tagged 150 BPM
        vm.Deck1!.TempoPercent = -4;   // the room hears 144
        int everything = app.Library.Results.Count;

        var button = app.Window.FindControl<Button>("BpmFilterButton")!;
        button.Flyout!.ShowAt(button);
        AppHarness.Settle(200);
        var items = ((MenuFlyout)button.Flyout).Items.OfType<MenuItem>().ToList();
        Assert.Equal("Near Deck 1 (144 BPM)", items[0].Header);
        Assert.False(items[1].IsEnabled);   // nothing on Deck 2
        items[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        button.Flyout.Hide();
        AppHarness.Settle();

        Assert.Equal("~bpm:144", app.Library.SearchText);
        Assert.Equal("BPM ≈144", app.Library.BpmFilterLabel);
        Assert.NotEmpty(app.Library.Results);
        Assert.True(app.Library.Results.Count < everything);
        Assert.All(app.Library.Results, t => Assert.InRange(t.Bpm!.Value, 144 * 0.92, 144 * 1.08));

        app.Library.SetBpmFilter(null);
        AppHarness.Settle();
        Assert.Equal("", app.Library.SearchText);
        Assert.Equal(everything, app.Library.Results.Count);
    }

    [AvaloniaFact]
    public void How_near_a_bpm_search_reaches_is_a_setting_and_the_results_follow_it()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        app.Library.SearchText = "~bpm:180";
        AppHarness.Settle();
        int wide = app.Library.Results.Count;
        Assert.All(app.Library.Results, t => Assert.InRange(t.Bpm!.Value, 180 * 0.92, 180 * 1.08));
        Assert.Equal("±8%", vm.Settings.NearBpmText);

        for (int i = 0; i < 6; i++)
            vm.Settings.NearBpmCloserCommand.Execute(null);
        AppHarness.Settle();

        Assert.Equal("±2%", vm.Settings.NearBpmText);
        Assert.Equal(2, app.Settings.NearBpmPercent);   // remembered
        Assert.All(app.Library.Results, t => Assert.InRange(t.Bpm!.Value, 180 * 0.98, 180 * 1.02));
        Assert.True(app.Library.Results.Count < wide, $"{app.Library.Results.Count} of {wide}");

        vm.Settings.NearBpmPercent = 0;   // never closer than 1%
        Assert.Equal("±1%", vm.Settings.NearBpmText);
    }
}
