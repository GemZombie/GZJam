using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.App.Views;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>Jumps: set from the waveform's menu, saved per song, armed with the JUMP key.</summary>
public sealed class JumpAppTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [AvaloniaFact]
    public void A_jump_is_set_in_two_steps_saved_for_the_song_and_happens_on_air()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var song = app.ToneTrack("Five minute tune", seconds: 30);
        vm.LoadToDeckAsync(vm.Deck1!, song).GetAwaiter().GetResult();
        DeckViewModel deck = vm.Deck1!;
        Assert.False(deck.HasJumps);

        Assert.True(deck.StartJump(S(4)));
        Assert.True(deck.IsSettingJump);
        Assert.Equal("Now right-click where it lands", deck.Hint);
        Assert.True(deck.LandJump(S(20)));

        Assert.True(deck.HasJumps);
        Assert.True(deck.IsJumpArmed);
        Assert.Equal([new SongJump(S(4), S(20))], app.Database.GetJumps(song.Path));
        Assert.Equal(14, deck.Deck.Remaining.TotalSeconds, 0);   // 30 s less the 16 skipped

        // The JUMP key shows over the waveform, lit.
        var key = app.Window.FindControl<DeckView>("Deck1View")!.FindControl<Button>("JumpButton")!;
        AppHarness.Settle(50);
        Assert.True(key.IsEffectivelyVisible);

        deck.TogglePlayCommand.Execute(null);   // on air: seeking is locked, but the jump still happens
        for (int i = 0; i < 25; i++)
            app.Play(0.2);
        Assert.InRange(deck.Deck.Position.TotalSeconds, 20.5, 21.5);
        Assert.False(deck.IsJumpArmed);

        // Loaded again later: the jump is there, armed.
        vm.Deck2!.TogglePlayCommand.Execute(null);
        vm.LoadToDeckAsync(vm.Deck2, song).GetAwaiter().GetResult();
        Assert.True(vm.Deck2.IsJumpArmed);
        Assert.Single(vm.Deck2.JumpMarkers);
    }

    [AvaloniaFact]
    public void The_jump_key_cancels_and_rearms()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        vm.LoadToDeckAsync(vm.Deck1!, app.ToneTrack("Tune", seconds: 30)).GetAwaiter().GetResult();
        DeckViewModel deck = vm.Deck1!;
        deck.StartJump(S(5));
        deck.LandJump(S(15));

        deck.ToggleJumpCommand.Execute(null);
        Assert.False(deck.IsJumpArmed);
        Assert.Equal(30, deck.Deck.Remaining.TotalSeconds, 0);   // nothing skipped now

        deck.TogglePlayCommand.Execute(null);
        for (int i = 0; i < 35; i++)
            app.Play(0.2);
        Assert.InRange(deck.Deck.Position.TotalSeconds, 6.5, 7.5);   // played straight through

        deck.ToggleJumpCommand.Execute(null);
        Assert.True(deck.IsJumpArmed);
    }

    [AvaloniaFact]
    public void Impossible_jumps_are_refused_with_a_reason()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        vm.LoadToDeckAsync(vm.Deck1!, app.ToneTrack("Tune", seconds: 30)).GetAwaiter().GetResult();
        DeckViewModel deck = vm.Deck1!;

        Assert.False(deck.StartJump(S(1)));
        Assert.Equal("A jump can't leave that close to the start or end", deck.Hint);

        deck.StartJump(S(10));
        Assert.False(deck.LandJump(S(11)));
        Assert.Equal("Land the jump further away", deck.Hint);
        Assert.True(deck.LandJump(S(5)));   // backward: plays 5-10 again

        deck.StartJump(S(20));
        deck.LandJump(S(25));
        Assert.False(deck.CanAddJump);
        Assert.False(deck.StartJump(S(12)));
        Assert.Equal("Two jumps already: remove one first", deck.Hint);

        deck.RemoveJump(0);
        Assert.Equal([new SongJump(S(20), S(25))], app.Database.GetJumps(deck.Track!.Path));
    }

    [AvaloniaFact]
    public void The_waveform_menu_offers_jumps()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        vm.LoadToDeckAsync(vm.Deck1!, app.ToneTrack("Tune", seconds: 30)).GetAwaiter().GetResult();
        var view = app.Window.FindControl<DeckView>("Deck1View")!;
        var menu = view.FindControl<ContextMenu>("TrimMenu")!;

        menu.Open(view.FindControl<Control>("Seek")!);
        AppHarness.Settle(100);
        var headers = menu.Items.OfType<MenuItem>().Select(i => (string)i.Header!).ToList();
        menu.Close();

        Assert.Contains("Jump", headers);
        Assert.Contains(headers, h => h.StartsWith("Jump from the playhead", StringComparison.Ordinal));
    }
}
