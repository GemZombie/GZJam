using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using GZJam.App.ViewModels;

namespace GZJam.App.Tests;

/// <summary>Keys for the decks, the crossfader and auto-play (#132): all with Ctrl, Cmd on a Mac.</summary>
public sealed class DeckKeyTests
{
    private static readonly RawInputModifiers Command = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    private static void Press(AppHarness app, Key key, PhysicalKey physical, RawInputModifiers extra = RawInputModifiers.None) =>
        app.Window.KeyPress(key, Command | extra, physical, null);

    private static async Task<AppHarness> WithSongs()
    {
        var app = new AppHarness();
        await app.ViewModel.LoadToDeckAsync(app.ViewModel.Deck1!, app.ToneTrack("One", 60));
        await app.ViewModel.LoadToDeckAsync(app.ViewModel.Deck2!, app.ToneTrack("Two", 60));
        app.Window.FindControl<DataGrid>("TrackGrid")!.Focus();
        return app;
    }

    private static void GlideUntil(AppHarness app, Func<bool> done)
    {
        for (int i = 0; i < 100 && !done(); i++)
        {
            app.Play(0.033);
            Thread.Sleep(15);
        }
    }

    [AvaloniaFact]
    public async Task Ctrl_number_plays_and_pauses_that_deck_and_with_Shift_cues_it()
    {
        using var app = await WithSongs();
        var (d1, d2) = (app.ViewModel.Deck1!, app.ViewModel.Deck2!);

        Press(app, Key.D1, PhysicalKey.Digit1);
        Assert.True(d1.IsPlaying);
        Press(app, Key.D1, PhysicalKey.Digit1);   // the harness pauses with one tap
        Assert.False(d1.IsPlaying);

        Press(app, Key.D2, PhysicalKey.Digit2, RawInputModifiers.Shift);
        Assert.True(d2.IsCueOn);
        Assert.False(d2.IsPlaying);
        Press(app, Key.NumPad2, PhysicalKey.NumPad2, RawInputModifiers.Shift);   // the keypad too
        Assert.False(d2.IsCueOn);

        Press(app, Key.D3, PhysicalKey.Digit3);   // two decks: no Deck 3
        Assert.False(d1.IsPlaying || d2.IsPlaying);
    }

    [AvaloniaFact]
    public async Task On_air_the_key_takes_two_presses_to_pause_like_the_button()
    {
        using var app = await WithSongs();
        app.Settings.OnAirPause = OnAirPause.TapTwice;
        var d1 = app.ViewModel.Deck1!;
        Press(app, Key.D1, PhysicalKey.Digit1);
        app.Play(0.1);
        Assert.True(d1.IsOnAir);

        Press(app, Key.D1, PhysicalKey.Digit1);
        Assert.True(d1.IsPlaying);
        Assert.Equal("Tap again to pause", d1.Hint);
        Press(app, Key.D1, PhysicalKey.Digit1);
        Assert.False(d1.IsPlaying);
    }

    [AvaloniaFact]
    public async Task Ctrl_Alt_number_loads_the_selected_song_from_the_library_or_the_queue()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var grid = app.Window.FindControl<DataGrid>("TrackGrid")!;
        var song = app.Library.Results[2];
        grid.SelectedItem = song;
        grid.Focus();

        Press(app, Key.D2, PhysicalKey.Digit2, RawInputModifiers.Alt);
        AppHarness.WaitFor(() => vm.Deck2!.Track is not null);
        Assert.Equal(song.Path, vm.Deck2!.Track!.Path);

        var queued = app.Library.Results[4];
        vm.AddToQueue([queued]);
        var queue = app.Window.FindControl<ListBox>("QueueList")!;
        AppHarness.Settle();
        queue.SelectedIndex = 0;
        queue.ContainerFromIndex(0)!.Focus();   // as a click on the row does
        Press(app, Key.D1, PhysicalKey.Digit1, RawInputModifiers.Alt);
        AppHarness.WaitFor(() => vm.Deck1!.Track is not null);
        Assert.Equal(queued.Path, vm.Deck1!.Track!.Path);
        Assert.Empty(vm.Queue.Items);   // loaded from the queue: it leaves it, as from its menu
    }

    [AvaloniaFact]
    public async Task Loading_by_key_onto_a_deck_the_room_hears_is_refused_as_always()
    {
        using var app = await WithSongs();
        var d1 = app.ViewModel.Deck1!;
        Press(app, Key.D1, PhysicalKey.Digit1);
        app.Play(0.1);
        var grid = app.Window.FindControl<DataGrid>("TrackGrid")!;
        grid.SelectedItem = app.Library.Results[0];
        grid.Focus();

        Press(app, Key.D1, PhysicalKey.Digit1, RawInputModifiers.Alt);
        AppHarness.Settle();

        Assert.Equal("One", d1.Track!.Title);
        Assert.Equal("Deck 1 is on air. Pause it, or crossfade away from it, to load a new track.", app.ViewModel.Notice);
    }

    [AvaloniaFact]
    public async Task Ctrl_arrows_glide_the_crossfader_but_move_by_word_in_the_search_box()
    {
        using var app = await WithSongs();
        var mixer = app.ViewModel.Mixer!;
        Assert.Equal(0, mixer.Crossfader);

        Press(app, Key.Right, PhysicalKey.ArrowRight);
        GlideUntil(app, () => mixer.Crossfader == 1);
        Assert.Equal(1, mixer.Crossfader);

        app.Window.FindControl<TextBox>("SearchBox")!.Focus();
        Press(app, Key.Left, PhysicalKey.ArrowLeft);
        GlideUntil(app, () => mixer.Crossfader < 1);
        Assert.Equal(1, mixer.Crossfader);

        Press(app, Key.D1, PhysicalKey.Digit1);   // the deck keys still work from there
        Assert.True(app.ViewModel.Deck1!.IsPlaying);
    }

    [AvaloniaFact]
    public void Ctrl_P_turns_auto_play_on_and_off_and_says_when_there_is_nothing_to_play()
    {
        using var app = new AppHarness();
        var auto = app.ViewModel.AutoPlay!;
        app.Window.FindControl<DataGrid>("TrackGrid")!.Focus();

        Press(app, Key.P, PhysicalKey.P);
        Assert.False(auto.IsOn);
        Assert.Equal("Nothing to auto-play: add songs to the queue first.", app.ViewModel.Notice);

        app.ViewModel.AddToQueue([app.ToneTrack("A"), app.ToneTrack("B")]);
        Press(app, Key.P, PhysicalKey.P);
        Assert.True(auto.IsOn);
        Press(app, Key.P, PhysicalKey.P);
        Assert.False(auto.IsOn);
    }
}
