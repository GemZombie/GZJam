using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.App.Views;

namespace GZJam.App.Tests;

/// <summary>Four decks: 3 under 1 and 4 under 2, half height; the crossfader fades 1 and 3 against 2 and 4.</summary>
public sealed class FourDeckTests
{
    private static AppHarness FourDecks()
    {
        var app = new AppHarness();
        Assert.True(app.ViewModel.SetDeckCount(4));
        AppHarness.Settle(100);
        return app;
    }

    private static DeckView View(AppHarness app, int number) => app.Window.FindControl<DeckView>($"Deck{number}View")!;

    [AvaloniaFact]
    public void Four_decks_fit_in_the_space_of_two()
    {
        using var app = new AppHarness();
        double twoDeckHeight = View(app, 1).Bounds.Height;
        Assert.False(View(app, 3).IsVisible);
        Assert.Equal(2, app.ViewModel.ActiveDecks.Count);

        app.ViewModel.SetDeckCount(4);
        AppHarness.Settle(100);

        Assert.Equal(4, app.ViewModel.ActiveDecks.Count);
        Assert.Equal(4, app.Settings.DeckCount);   // remembered
        foreach (int n in new[] { 1, 2, 3, 4 })
        {
            Assert.True(View(app, n).IsVisible);
            Assert.True(View(app, n).IsCompact);
        }
        // Deck 3 is under Deck 1, and the pair is no taller than one deck was.
        var (top, bottom) = (View(app, 1).Bounds, View(app, 3).Bounds);
        Assert.Equal(top.X, bottom.X, 1);
        Assert.InRange(top.Height + bottom.Height + DeckView.CompactGap, twoDeckHeight - 1, twoDeckHeight + 1);

        app.ViewModel.SetDeckCount(2);
        AppHarness.Settle(100);
        Assert.False(View(app, 3).IsVisible);
        Assert.False(View(app, 1).IsCompact);
        Assert.Equal(twoDeckHeight, View(app, 1).Bounds.Height, 1);
    }

    [AvaloniaFact]
    public void Decks_1_and_3_are_on_the_left_of_the_crossfader_2_and_4_on_the_right()
    {
        using var app = FourDecks();
        var vm = app.ViewModel;
        vm.LoadToDeckAsync(vm.Deck3!, app.ToneTrack("Three")).GetAwaiter().GetResult();
        vm.LoadToDeckAsync(vm.Deck4!, app.ToneTrack("Four")).GetAwaiter().GetResult();

        vm.Mixer!.Crossfader = 0;
        vm.Deck3!.TogglePlayCommand.Execute(null);
        vm.Deck4!.TogglePlayCommand.Execute(null);
        app.Play(0.3);
        app.Play(0.3);

        Assert.True(vm.Deck3.IsOnAir);
        Assert.True(vm.Deck4.IsOffAir);
        Assert.Equal("3 · LIVE", vm.Mixer.LiveText);
        var (main, _) = app.Play(0.3);
        Assert.InRange(main, 0.3f, 1f);   // the room hears Deck 3

        vm.Mixer.Crossfader = 1;
        app.Play(0.1);
        Assert.True(vm.Deck4.IsOnAir);
        Assert.True(vm.Deck3.IsOffAir);
        Assert.Equal("4 · LIVE", vm.Mixer.LiveText);
    }

    [AvaloniaFact]
    public void A_second_song_on_the_live_side_waits_until_the_first_is_faded_out()
    {
        using var app = FourDecks();
        var vm = app.ViewModel;
        vm.LoadToDeckAsync(vm.Deck1!, app.ToneTrack("One")).GetAwaiter().GetResult();
        vm.LoadToDeckAsync(vm.Deck3!, app.ToneTrack("Three")).GetAwaiter().GetResult();
        vm.Mixer!.Crossfader = 0;
        vm.Deck1!.TogglePlayCommand.Execute(null);
        app.Play(0.1);

        vm.Deck3!.TogglePlayCommand.Execute(null);

        Assert.False(vm.Deck3.IsPlaying);
        Assert.Equal("Deck 1 is playing. Fade it out first", vm.Deck3.Hint);

        // Off the room's side, it can play (to listen in the headphones).
        vm.Mixer.Crossfader = 1;
        vm.Deck3.TogglePlayCommand.Execute(null);
        Assert.True(vm.Deck3.IsPlaying);
    }

    [AvaloniaFact]
    public void Auto_play_goes_round_the_decks_in_number_order()
    {
        using var app = FourDecks();
        app.Settings.GapSeconds = 0.2;
        var vm = app.ViewModel;
        var tracks = Enumerable.Range(1, 5).Select(i => app.ToneTrack($"Song {i}", 0.8)).ToList();
        vm.Queue.Add(tracks);
        var order = new List<string>();

        vm.AutoPlay!.IsOn = true;
        var watch = Stopwatch.StartNew();
        while (order.Count < 5 && watch.Elapsed < TimeSpan.FromSeconds(20))
        {
            if (vm.ActiveDecks.FirstOrDefault(d => d.IsOnAir) is { } live && (order.Count == 0 || order[^1] != live.Label))
            {
                order.Add(live.Label);
                // Each switch crosses the crossfader: odd decks left, even decks right.
                Assert.Equal(int.Parse(live.Label) % 2 == 1 ? 0 : 1, vm.Mixer!.Crossfader);
            }
            app.Play(0.033);
            Thread.Sleep(10);
        }

        Assert.Equal(["1", "2", "3", "4", "1"], order);
    }

    [AvaloniaFact]
    public void Going_back_to_two_decks_waits_for_deck_3_and_4_to_stop_and_then_empties_them()
    {
        using var app = FourDecks();
        var vm = app.ViewModel;
        vm.LoadToDeckAsync(vm.Deck3!, app.ToneTrack("Three")).GetAwaiter().GetResult();
        vm.LoadToDeckAsync(vm.Deck4!, app.ToneTrack("Four")).GetAwaiter().GetResult();
        vm.Mixer!.Crossfader = 1;
        vm.Deck3!.TogglePlayCommand.Execute(null);

        Assert.False(vm.SetDeckCount(2));
        Assert.True(vm.FourDecks);
        Assert.Equal("Deck 3 is playing. Stop it before going back to two decks.", vm.Notice);

        vm.Deck3.TogglePlayCommand.Execute(null);
        Assert.True(vm.SetDeckCount(2));
        Assert.Null(vm.Deck3.Track);
        Assert.Null(vm.Deck4!.Track);
        Assert.Equal(2, app.Settings.DeckCount);
    }

    [AvaloniaFact]
    public void A_compact_deck_keeps_eject_rating_and_bpm_in_its_more_menu()
    {
        using var app = FourDecks();
        var vm = app.ViewModel;
        vm.LoadToDeckAsync(vm.Deck3!, app.ToneTrack("Three")).GetAwaiter().GetResult();
        var view = View(app, 3);
        var more = view.FindControl<Button>("MoreButton")!;
        Assert.True(more.IsEffectivelyVisible);
        Assert.False(View(app, 3).FindControl<Button>("RatingButton")!.IsEffectivelyVisible);

        more.Flyout!.ShowAt(more);
        AppHarness.Settle(200);
        var items = view.MoreMenu.Items.OfType<MenuItem>().ToList();
        Assert.Equal(["Rating", "Energy", "Detect BPM in range", "Eject"], items.Select(i => (string)i.Header!));

        items[^1].Command!.Execute(null);
        more.Flyout.Hide();
        Assert.Null(vm.Deck3!.Track);
    }

    [AvaloniaFact]
    public void The_track_menu_offers_decks_3_and_4_only_when_they_are_shown()
    {
        using var app = new AppHarness();
        var menu = app.Window.FindControl<ContextMenu>("TrackMenu")!;
        var grid = app.Window.FindControl<DataGrid>("TrackGrid")!;
        grid.SelectedIndex = 0;
        menu.Open(grid);
        AppHarness.Settle(100);
        Assert.False(app.Window.FindControl<MenuItem>("TrackLoad3")!.IsVisible);
        menu.Close();

        app.ViewModel.SetDeckCount(4);
        menu.Open(grid);
        AppHarness.Settle(100);
        Assert.True(app.Window.FindControl<MenuItem>("TrackLoad4")!.IsVisible);
        menu.Close();
    }
}
