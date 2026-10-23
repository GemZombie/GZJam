using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using GZJam.App.Controls;
using GZJam.App.ViewModels;
using GZJam.App.Views;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>Dragging the start, end, jump and cue markers on the waveform (issue #12).</summary>
public sealed class MarkerDragTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    /// <summary>
    /// Every hint the deck shows from now on (a hint goes after 2.5 s, which a slow settle can
    /// outlast).
    /// </summary>
    private static List<string> Hints(DeckViewModel deck)
    {
        var hints = new List<string>();
        deck.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DeckViewModel.Hint) && deck.Hint is { } hint)
                hints.Add(hint);
        };
        return hints;
    }

    private static (CloseUpWaveform CloseUp, SeekBar Whole) Waveforms(AppHarness app)
    {
        AppHarness.Settle(100);
        var view = app.Window.FindControl<DeckView>("Deck1View")!;
        return (view.FindControl<CloseUpWaveform>("CloseUp")!, view.FindControl<SeekBar>("Seek")!);
    }

    /// <summary>Where <paramref name="seconds"/> into the song is on the whole song, in the window.</summary>
    private static Point OnWhole(AppHarness app, SeekBar whole, DeckViewModel deck, double seconds) =>
        whole.TranslatePoint(new Point(seconds / deck.LengthSeconds * whole.Bounds.Width, whole.Bounds.Height / 2), app.Window)!.Value;

    /// <summary>Where <paramref name="seconds"/> into the song is on the close-up (the playhead in the middle), in the window.</summary>
    private static Point OnCloseUp(AppHarness app, CloseUpWaveform closeUp, DeckViewModel deck, double seconds)
    {
        double secondsPerPixel = closeUp.WindowSeconds / closeUp.Bounds.Width;
        double x = closeUp.Bounds.Width / 2 + (seconds - deck.Deck.Position.TotalSeconds) / secondsPerPixel;
        return closeUp.TranslatePoint(new Point(x, closeUp.Bounds.Height * 0.6), app.Window)!.Value;
    }

    [AvaloniaFact]
    public void A_cue_dragged_on_the_close_up_moves_there_and_is_saved_and_the_song_stays_put()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var song = app.ToneTrack("Tune", seconds: 60);
        vm.LoadToDeckAsync(vm.Deck1!, song).GetAwaiter().GetResult();
        DeckViewModel deck = vm.Deck1!;
        deck.SeekTo(0.5);   // 30 s
        deck.AddCue(S(31));
        var (closeUp, _) = Waveforms(app);
        double secondsPerPixel = closeUp.WindowSeconds / closeUp.Bounds.Width;

        Point cue = OnCloseUp(app, closeUp, deck, 31);
        app.MouseDrag(cue + new Vector(2, 0), cue + new Vector(62, 0));   // grabbed a little off the line

        Assert.Equal(31 + 60 * secondsPerPixel, deck.Cues.Single().At.TotalSeconds, 1);
        Assert.Equal(deck.Cues, app.Database.GetCues(song.Path));
        Assert.Equal(S(30), deck.Deck.Position);   // the marker moved, not the song
    }

    [AvaloniaFact]
    public void On_air_markers_can_still_be_dragged_though_the_song_cannot()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        vm.LoadToDeckAsync(vm.Deck1!, app.ToneTrack("Tune", seconds: 60)).GetAwaiter().GetResult();
        DeckViewModel deck = vm.Deck1!;
        deck.AddCue(S(40));
        vm.Mixer!.Crossfader = 0;
        deck.TogglePlayCommand.Execute(null);
        app.Play(0.2);
        Assert.True(deck.IsOnAir);
        var (_, whole) = Waveforms(app);
        int lockedPresses = 0;
        whole.LockedPressed += (_, _) => lockedPresses++;

        app.MouseDrag(OnWhole(app, whole, deck, 40), OnWhole(app, whole, deck, 45));
        Assert.InRange(deck.Cues.Single().At.TotalSeconds, 44.5, 45.5);
        Assert.InRange(deck.Deck.Position.TotalSeconds, 0, 2);   // still playing from the top
        Assert.Equal(0, lockedPresses);

        // Away from a marker the song is locked, as before, and says why.
        Point from = OnWhole(app, whole, deck, 20), to = OnWhole(app, whole, deck, 25);
        app.Window.MouseDown(from, MouseButton.Left, RawInputModifiers.None);
        app.Window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        app.Window.MouseUp(to, MouseButton.Left, RawInputModifiers.None);
        Assert.Equal("Move the crossfader off Deck 1 to jump around in it", deck.Hint);
        Assert.InRange(deck.Deck.Position.TotalSeconds, 0, 2);
        Assert.Equal(1, lockedPresses);

        // A tap on a marker, without dragging, is a tap: locked, so it says why too.
        Point cue = OnWhole(app, whole, deck, 45);
        app.Window.MouseDown(cue, MouseButton.Left, RawInputModifiers.None);
        app.Window.MouseUp(cue, MouseButton.Left, RawInputModifiers.None);
        Assert.Equal(2, lockedPresses);
        Assert.InRange(deck.Deck.Position.TotalSeconds, 0, 2);
        Assert.Equal(45, deck.Cues.Single().At.TotalSeconds, 0);
    }

    [AvaloniaFact]
    public void A_click_on_a_marker_without_dragging_goes_there_like_anywhere_else()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        vm.LoadToDeckAsync(vm.Deck1!, app.ToneTrack("Tune", seconds: 60)).GetAwaiter().GetResult();
        DeckViewModel deck = vm.Deck1!;
        deck.AddCue(S(40));
        var (_, whole) = Waveforms(app);

        Point cue = OnWhole(app, whole, deck, 40) + new Vector(2, 0);
        app.Window.MouseDown(cue, MouseButton.Left, RawInputModifiers.None);
        app.Window.MouseUp(cue, MouseButton.Left, RawInputModifiers.None);
        AppHarness.Settle(50);

        Assert.InRange(deck.Deck.Position.TotalSeconds, 39.5, 41);
        Assert.Equal(S(40), deck.Cues.Single().At);
    }

    [AvaloniaFact]
    public void Dragging_the_start_sets_it_by_hand_and_a_deck_waiting_there_follows_it()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var song = app.ToneTrack("Applause first", seconds: 30, silenceBefore: 4);
        vm.LoadToDeckAsync(vm.Deck1!, song).GetAwaiter().GetResult();
        DeckViewModel deck = vm.Deck1!;
        AppHarness.WaitFor(() => deck.Waveform is not null);
        AppHarness.Settle(100);
        double start = deck.Deck.Start.TotalSeconds;
        Assert.InRange(start, 3.5, 4.1);
        Assert.Equal(start, deck.Deck.Position.TotalSeconds, 1);   // cued up at the start
        var (_, whole) = Waveforms(app);

        app.MouseDrag(OnWhole(app, whole, deck, start), OnWhole(app, whole, deck, 2.5));   // a little earlier

        Assert.True(deck.IsStartManual);
        Assert.InRange(deck.Deck.Start.TotalSeconds, 2.3, 2.7);
        Assert.Equal(deck.Deck.Start.TotalSeconds, deck.Deck.Position.TotalSeconds, 1);   // followed it
        Assert.Equal(deck.Deck.Start, app.Database.GetTrim(song.Path).Start);
    }

    [AvaloniaFact]
    public void Either_end_of_a_jump_drags_it_stays_armed_and_a_drag_to_somewhere_it_cant_go_goes_back()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var song = app.ToneTrack("Tune", seconds: 60);
        vm.LoadToDeckAsync(vm.Deck1!, song).GetAwaiter().GetResult();
        DeckViewModel deck = vm.Deck1!;
        deck.StartJump(S(20));
        deck.LandJump(S(40));
        var (_, whole) = Waveforms(app);
        var hints = Hints(deck);

        app.MouseDrag(OnWhole(app, whole, deck, 40), OnWhole(app, whole, deck, 45));
        SongJump jump = deck.SongJumps.Single();
        Assert.Equal(20, jump.From.TotalSeconds, 0);
        Assert.InRange(jump.To.TotalSeconds, 44.5, 45.5);
        Assert.Equal([jump], app.Database.GetJumps(song.Path));
        Assert.True(deck.IsJumpArmed);
        Assert.Equal(jump.To, deck.Deck.Jumps.Single().To);

        // Where it leaves, dragged right up to where it lands: refused, and it stays where it was.
        app.MouseDrag(OnWhole(app, whole, deck, 20), OnWhole(app, whole, deck, jump.To.TotalSeconds - 1));
        Assert.Equal(jump, deck.SongJumps.Single());
        Assert.Equal(["Land the jump further away"], hints);
    }

    [AvaloniaFact]
    public void A_cue_dragged_onto_another_goes_back()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        vm.LoadToDeckAsync(vm.Deck1!, app.ToneTrack("Tune", seconds: 60)).GetAwaiter().GetResult();
        DeckViewModel deck = vm.Deck1!;
        deck.AddCue(S(10));
        deck.AddCue(S(30));
        var (_, whole) = Waveforms(app);
        var hints = Hints(deck);

        app.MouseDrag(OnWhole(app, whole, deck, 30), OnWhole(app, whole, deck, 10.3));

        Assert.Equal([new SongCue(1, S(10)), new SongCue(2, S(30))], deck.Cues);
        Assert.Equal(["Cue 1 is already there"], hints);
    }

    [Fact]
    public void A_press_grabs_the_nearest_marker_within_reach()
    {
        (MarkerKind, int, double)[] markers = [(MarkerKind.Cue, 1, 0.50), (MarkerKind.JumpFrom, 0, 0.53), (MarkerKind.Start, 0, 0.10)];
        static double X(double fraction) => fraction * 100;

        Assert.Equal((MarkerKind.JumpFrom, 0, 0.53), MarkerGrab.Find(markers, X, 52, reach: 5));   // nearer the jump
        Assert.Equal((MarkerKind.Cue, 1, 0.50), MarkerGrab.Find(markers, X, 49, reach: 5));
        Assert.Null(MarkerGrab.Find(markers, X, 20, reach: 5));
        Assert.Equal((MarkerKind.Start, 0, 0.10), MarkerGrab.Find(markers, X, 20, reach: 14));   // a finger reaches further
    }
}
