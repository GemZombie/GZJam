using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using GZJam.App.ViewModels;

namespace GZJam.App.Tests;

/// <summary>Fade out on air takes two taps like pause (#130); the queue's Delete (#133) and Space (#134) keys.</summary>
public sealed class QueueKeysAndFadeTests
{
    private static void UseTwoTaps(AppHarness app) =>
        app.ViewModel.Settings.PauseChoice = app.ViewModel.Settings.PauseChoices.Single(c => c.Value == OnAirPause.TapTwice);

    [AvaloniaFact]
    public async Task With_two_taps_fade_out_on_air_needs_a_second_tap()
    {
        using var app = new AppHarness();
        app.Settings.FadeOutSeconds = 1;
        UseTwoTaps(app);
        DeckViewModel a = app.ViewModel.Deck1!;
        await app.ViewModel.LoadToDeckAsync(a, app.ToneTrack("Live", seconds: 60));
        a.TogglePlayCommand.Execute(null);
        app.Play(0.2);
        Assert.True(a.IsOnAir);

        a.FadeOutCommand.Execute(null);
        Assert.True(a.IsFadeArmed);
        Assert.Equal("Tap again to fade out", a.Hint);
        app.Play(0.5);
        Assert.True(app.Play(0.1).Main > 0.1, "still playing at full level");

        // The play key after an armed FADE OUT: arms pause instead; it doesn't fade or pause.
        a.TogglePlayCommand.Execute(null);
        Assert.False(a.IsFadeArmed);
        Assert.True(a.IsPauseArmed);
        Assert.True(a.IsPlaying);

        a.FadeOutCommand.Execute(null);
        a.FadeOutCommand.Execute(null);
        Assert.False(a.IsFadeArmed);
        AppHarness.Settle(1200);   // the fade runs on the clock
        app.Play(0.1);
        Assert.False(a.IsPlaying);   // faded out and stopped
    }

    [AvaloniaFact]
    public async Task Off_air_or_with_one_tap_fade_out_goes_at_once()
    {
        using var app = new AppHarness();
        app.Settings.FadeOutSeconds = 1;
        DeckViewModel a = app.ViewModel.Deck1!, b = app.ViewModel.Deck2!;
        await app.ViewModel.LoadToDeckAsync(a, app.ToneTrack("Live", seconds: 60));
        a.TogglePlayCommand.Execute(null);
        app.Play(0.2);
        a.FadeOutCommand.Execute(null);   // one tap is the default
        Assert.False(a.IsFadeArmed);
        AppHarness.Settle(1200);   // the fade runs on the clock
        app.Play(0.1);
        Assert.False(a.IsPlaying);

        UseTwoTaps(app);
        await app.ViewModel.LoadToDeckAsync(b, app.ToneTrack("Cued", seconds: 60));
        b.TogglePlayCommand.Execute(null);   // the crossfader is on Deck 1's side: Deck 2 is off air
        app.Play(0.2);
        Assert.False(b.IsOnAir);
        b.FadeOutCommand.Execute(null);
        Assert.False(b.IsFadeArmed);
        AppHarness.Settle(1200);   // the fade runs on the clock
        app.Play(0.1);
        Assert.False(b.IsPlaying);
    }

    private static ListBox Queue(AppHarness app) => app.Window.FindControl<ListBox>("QueueList")!;

    private static void Press(AppHarness app, Key key, PhysicalKey physical)
    {
        app.Window.KeyPress(key, RawInputModifiers.None, physical, null);
        AppHarness.Settle(100);
    }

    [AvaloniaFact]
    public void Delete_in_the_queue_selects_the_next_song_and_keeps_the_keyboard_there()
    {
        using var app = new AppHarness();
        var songs = app.Library.Results.Take(4).ToList();
        app.ViewModel.Queue.Add(songs);
        AppHarness.Settle(100);
        var queue = Queue(app);
        queue.SelectedIndex = 1;
        ((ListBoxItem)queue.ContainerFromIndex(1)!).Focus(NavigationMethod.Directional);
        AppHarness.Settle(100);

        Press(app, Key.Delete, PhysicalKey.Delete);
        Assert.Equal([songs[0], songs[2], songs[3]], app.ViewModel.Queue.Items.Select(i => i.Track));
        Assert.Equal(1, queue.SelectedIndex);   // the song that took its place
        Assert.True(queue.ContainerFromIndex(1)!.IsFocused);

        Press(app, Key.Down, PhysicalKey.ArrowDown);   // the arrows go on working
        Assert.Equal(2, queue.SelectedIndex);
        Press(app, Key.Delete, PhysicalKey.Delete);    // the last: the one above is selected
        Assert.Equal([songs[0], songs[2]], app.ViewModel.Queue.Items.Select(i => i.Track));
        Assert.Equal(1, queue.SelectedIndex);
    }

    [AvaloniaFact]
    public void Space_in_the_queue_previews_the_selected_song_and_again_stops_it()
    {
        using var app = new AppHarness();
        var songs = app.Library.Results.Take(2).ToList();
        app.ViewModel.Queue.Add(songs);
        AppHarness.Settle(100);
        var queue = Queue(app);
        queue.SelectedIndex = 1;
        ((ListBoxItem)queue.ContainerFromIndex(1)!).Focus(NavigationMethod.Directional);
        AppHarness.Settle(100);

        Press(app, Key.Space, PhysicalKey.Space);
        Assert.Same(songs[1], app.ViewModel.Preview!.Track);
        Assert.Equal(2, app.ViewModel.Queue.Items.Count);   // still queued

        Press(app, Key.Space, PhysicalKey.Space);
        Assert.Null(app.ViewModel.Preview.Track);
    }
}
