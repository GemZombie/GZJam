using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using GZJam.App.Controls;
using GZJam.App.ViewModels;
using GZJam.App.Views;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>Cues: numbered points marked from the waveform's menu, gone to at a tap on their key.</summary>
public sealed class CueAppTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static IReadOnlyList<Button> CueKeys(AppHarness app, string deck = "Deck1View")
    {
        AppHarness.Settle(50);
        var keys = app.Window.FindControl<DeckView>(deck)!.FindControl<ItemsControl>("CueKeys")!;
        return keys.IsEffectivelyVisible ? keys.GetVisualDescendants().OfType<Button>().ToList() : [];
    }

    [AvaloniaFact]
    public void A_cue_key_goes_there_at_once_even_on_air_and_the_cues_are_saved_for_the_song()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var song = app.ToneTrack("Long tune", seconds: 30);
        vm.LoadToDeckAsync(vm.Deck1!, song).GetAwaiter().GetResult();
        DeckViewModel deck = vm.Deck1!;
        Assert.Empty(CueKeys(app));

        Assert.True(deck.AddCue(S(24)));   // the outro
        Assert.True(deck.AddCue(S(10)));
        Assert.Equal([new SongCue(1, S(24)), new SongCue(2, S(10))], app.Database.GetCues(song.Path));
        Assert.Equal(["1", "2"], CueKeys(app).Select(k => k.Content?.ToString()));
        Assert.Equal(0.8, deck.CueMarkers[0].At, 3);

        deck.TogglePlayCommand.Execute(null);   // on air: seeking is locked, but a cue key isn't
        app.Play(3);
        Assert.True(deck.IsOnAir);
        Assert.False(deck.CanSeek);

        CueKeys(app)[0].Command!.Execute(CueKeys(app)[0].CommandParameter);
        Assert.InRange(deck.Deck.Position.TotalSeconds, 24, 24.5);
        Assert.True(deck.IsPlaying);
        app.Play(1);
        Assert.InRange(deck.Deck.Position.TotalSeconds, 24.8, 25.6);

        // Loaded again later: the cues are there.
        vm.Deck2!.TogglePlayCommand.Execute(null);
        vm.LoadToDeckAsync(vm.Deck2, song).GetAwaiter().GetResult();
        Assert.Equal([1, 2], vm.Deck2.Cues.Select(c => c.Number));
        Assert.Equal(2, CueKeys(app, "Deck2View").Count);
    }

    [AvaloniaFact]
    public void A_cue_key_works_on_a_paused_deck_and_a_jump_to_a_cue_isnt_listening()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        vm.LoadToDeckAsync(vm.Deck1!, app.ToneTrack("Tune", seconds: 30)).GetAwaiter().GetResult();
        DeckViewModel deck = vm.Deck1!;
        deck.AddCue(S(20));

        deck.GoToCueCommand.Execute(1);
        Assert.InRange(deck.Deck.Position.TotalSeconds, 19.9, 20.1);
        Assert.False(deck.IsPlaying);

        deck.GoToCueCommand.Execute(3);   // no cue 3: nothing happens
        Assert.InRange(deck.Deck.Position.TotalSeconds, 19.9, 20.1);
    }

    [AvaloniaFact]
    public void Cues_are_numbered_one_to_four_and_keep_their_numbers()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        vm.LoadToDeckAsync(vm.Deck1!, app.ToneTrack("Tune", seconds: 30)).GetAwaiter().GetResult();
        DeckViewModel deck = vm.Deck1!;

        foreach (double at in new[] { 2.0, 8, 14, 20 })
            Assert.True(deck.AddCue(S(at)));
        Assert.False(deck.CanAddCue);
        Assert.False(deck.AddCue(S(26)));
        Assert.Equal("Four cues already: remove one first", deck.Hint);

        deck.RemoveCue(2);
        Assert.Equal([1, 3, 4], deck.Cues.Select(c => c.Number));
        Assert.Equal(2, deck.NextCueNumber);
        Assert.False(deck.AddCue(S(14.5)));
        Assert.Equal("Cue 3 is already there", deck.Hint);
        Assert.True(deck.AddCue(S(26)));
        Assert.Equal([new SongCue(1, S(2)), new SongCue(2, S(26)), new SongCue(3, S(14)), new SongCue(4, S(20))],
            app.Database.GetCues(deck.Track!.Path));

        deck.EjectCommand.Execute(null);
        Assert.Empty(deck.Cues);
        Assert.Empty(CueKeys(app));
    }

    [AvaloniaFact]
    public void The_waveform_menu_marks_and_removes_cues()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        vm.LoadToDeckAsync(vm.Deck1!, app.ToneTrack("Tune", seconds: 30)).GetAwaiter().GetResult();
        vm.Deck1!.AddCue(S(12));
        var view = app.Window.FindControl<DeckView>("Deck1View")!;
        var menu = view.FindControl<ContextMenu>("TrimMenu")!;

        menu.Open(view.FindControl<Control>("Seek")!);
        AppHarness.Settle(100);
        var headers = menu.Items.OfType<MenuItem>().Select(i => (string)i.Header!).ToList();
        menu.Close();

        Assert.Contains("Cue", headers);
        Assert.Contains("Cue 2 at the playhead (0:00.0)", headers);
        Assert.Contains("Remove cue 1 (0:12.0)", headers);
    }

    [AvaloniaFact]
    public void Four_decks_go_to_a_cue_from_the_more_menu_and_the_jump_key_fits_the_short_waveform()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        vm.Settings.TouchMode = true;
        vm.Settings.FourDecks = true;
        vm.LoadToDeckAsync(vm.Deck4!, app.ToneTrack("Tune", seconds: 30)).GetAwaiter().GetResult();
        DeckViewModel deck = vm.Deck4!;   // a lower deck: as tall as what's on it (an upper one takes the spare room)
        deck.AddCue(S(15));
        AppHarness.Settle(200);
        var view = app.Window.FindControl<DeckView>("Deck4View")!;
        var seek = view.FindControl<SeekBar>("Seek")!;
        (double height, double waveform) = (view.Bounds.Height, seek.Bounds.Height);
        Assert.Empty(CueKeys(app, "Deck4View"));   // no room over a four-deck waveform

        // A jump's key shows, without making the deck or its waveform taller: with its margin, it
        // fits the shortest a four-deck waveform gets (in a short window), even in touch mode.
        deck.StartJump(S(5));
        deck.LandJump(S(10));
        AppHarness.Settle(200);
        Assert.Equal(height, view.Bounds.Height, 1);
        Assert.Equal(waveform, seek.Bounds.Height, 1);
        var jump = view.FindControl<Button>("JumpButton")!;
        Assert.True(jump.IsEffectivelyVisible);
        Assert.True(jump.Bounds.Height + jump.Margin.Top + jump.Margin.Bottom <= seek.MinHeight,
            $"JUMP {jump.Bounds.Height} high with its margin, on a waveform at least {seek.MinHeight} high");

        view.MoreMenu.ShowAt(view.FindControl<Button>("MoreButton")!);
        AppHarness.Settle(100);
        var go = view.MoreMenu.Items.OfType<MenuItem>().Single(i => (string)i.Header! == "Go to cue 1 (0:15.0)");
        view.MoreMenu.Hide();
        go.Command!.Execute(go.CommandParameter);
        Assert.InRange(deck.Deck.Position.TotalSeconds, 14.9, 15.1);
    }

    [AvaloniaFact]
    public void Touch_cue_keys_are_finger_sized_and_sit_on_the_waveform()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        vm.Settings.TouchMode = true;
        vm.LoadToDeckAsync(vm.Deck1!, app.ToneTrack("Tune", seconds: 30)).GetAwaiter().GetResult();
        foreach (double at in new[] { 5.0, 10, 15, 20 })
            vm.Deck1!.AddCue(S(at));
        AppHarness.Settle(200);

        var view = app.Window.FindControl<DeckView>("Deck1View")!;
        var seek = view.FindControl<SeekBar>("Seek")!;
        var keys = CueKeys(app);
        Assert.Equal(4, keys.Count);
        foreach (Button key in keys)
        {
            Assert.True(key.Bounds.Height >= 40 && key.Bounds.Width >= 44, $"key {key.Bounds.Size}");
            var topLeft = key.TranslatePoint(default, seek)!.Value;
            Assert.True(topLeft.Y >= 0 && topLeft.Y + key.Bounds.Height <= seek.Bounds.Height, $"key at {topLeft.Y} on a waveform {seek.Bounds.Height} high");
            Assert.True(topLeft.X + key.Bounds.Width < seek.Bounds.Width / 2, "the keys stay on the left half");
        }
    }
}
