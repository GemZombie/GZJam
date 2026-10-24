using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using GZJam.App.Controls;
using GZJam.App.ViewModels;
using GZJam.App.Views;

namespace GZJam.App.Tests;

/// <summary>The close-up waveform: a few seconds around the playhead, over the whole song (issue #14).</summary>
public sealed class CloseUpTests
{
    private static (DeckView View, CloseUpWaveform CloseUp, SeekBar Whole) Deck(AppHarness app, string name = "Deck1View")
    {
        AppHarness.Settle(100);
        var view = app.Window.FindControl<DeckView>(name)!;
        return (view, view.FindControl<CloseUpWaveform>("CloseUp")!, view.FindControl<SeekBar>("Seek")!);
    }

    private static Point Centre(AppHarness app, Control control, double dx = 0) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2 + dx, control.Bounds.Height / 2), app.Window)!.Value;

    [AvaloniaFact]
    public void Two_decks_show_a_close_up_over_the_whole_song_in_the_same_space()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        double empty = Deck(app).View.Bounds.Height;
        vm.LoadToDeckAsync(vm.Deck1!, app.ToneTrack("Tune", seconds: 40)).GetAwaiter().GetResult();
        DeckViewModel deck = vm.Deck1!;
        AppHarness.WaitFor(() => deck.CloseUpWaveform?.Count > 1000, TimeSpan.FromSeconds(20));
        var (view, closeUp, whole) = Deck(app);

        Assert.InRange(deck.CloseUpWaveform!.Count, 3990, 4010);   // 40 s, a slice every 10 ms
        Assert.True(closeUp.IsEffectivelyVisible && whole.IsEffectivelyVisible);
        Assert.InRange(closeUp.Bounds.Height / whole.Bounds.Height, 1.8, 2.2);   // two thirds and a third
        Assert.Equal(empty, view.Bounds.Height, 1);   // the same height as with no song: the close-up takes no extra room
        Assert.Equal(8, closeUp.WindowSeconds);
        Assert.Equal(8.0 / 40, whole.CloseUp, 3);   // the box on the whole song
        Assert.True(whole.ShowCloseUp);
    }

    [AvaloniaFact]
    public void The_wheel_shows_more_or_less_of_the_song_on_every_deck_and_it_is_remembered()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        vm.LoadToDeckAsync(vm.Deck1!, app.ToneTrack("Tune", seconds: 60)).GetAwaiter().GetResult();
        var (_, closeUp, _) = Deck(app);

        app.Window.MouseWheel(Centre(app, closeUp), new Vector(0, 1));   // away: closer in
        AppHarness.Settle(50);
        Assert.Equal(8 / 1.25, closeUp.WindowSeconds, 3);
        Assert.Equal(8 / 1.25, app.Settings.CloseUpSeconds, 3);
        Assert.Equal(8 / 1.25, vm.Deck2!.CloseUpSeconds, 3);

        for (int i = 0; i < 30; i++)
            app.Window.MouseWheel(Centre(app, closeUp), new Vector(0, -1));
        AppHarness.Settle(50);
        Assert.Equal(CloseUpWaveform.MaxWindowSeconds, closeUp.WindowSeconds);
    }

    [AvaloniaFact]
    public void Dragging_the_close_up_moves_through_the_song_like_a_record_but_not_on_air()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        vm.LoadToDeckAsync(vm.Deck1!, app.ToneTrack("Tune", seconds: 60)).GetAwaiter().GetResult();
        DeckViewModel deck = vm.Deck1!;
        deck.SeekTo(0.5);   // 30 s
        var (_, closeUp, _) = Deck(app);
        double secondsPerPixel = closeUp.WindowSeconds / closeUp.Bounds.Width;

        // Dragged left by 100 px: the song moves left, so the playhead goes forward.
        Point from = Centre(app, closeUp);
        app.Window.MouseDown(from, MouseButton.Left, RawInputModifiers.None);
        app.Window.MouseMove(from - new Vector(50, 0), RawInputModifiers.LeftMouseButton);
        app.Window.MouseMove(from - new Vector(100, 0), RawInputModifiers.LeftMouseButton);
        app.Window.MouseUp(from - new Vector(100, 0), MouseButton.Left, RawInputModifiers.None);
        AppHarness.Settle(50);
        Assert.Equal(30 + 100 * secondsPerPixel, deck.Deck.Position.TotalSeconds, 1);

        // A click goes to that spot.
        app.Window.MouseDown(Centre(app, closeUp, -80), MouseButton.Left, RawInputModifiers.None);
        app.Window.MouseUp(Centre(app, closeUp, -80), MouseButton.Left, RawInputModifiers.None);
        AppHarness.Settle(50);
        Assert.Equal(30 + 20 * secondsPerPixel, deck.Deck.Position.TotalSeconds, 1);

        // On air: locked, with the reason.
        vm.Mixer!.Crossfader = 0;
        deck.TogglePlayCommand.Execute(null);
        app.Play(0.2);
        double before = deck.Deck.Position.TotalSeconds;
        app.Window.MouseDown(Centre(app, closeUp), MouseButton.Left, RawInputModifiers.None);
        app.Window.MouseMove(Centre(app, closeUp, -100), RawInputModifiers.LeftMouseButton);
        app.Window.MouseUp(Centre(app, closeUp, -100), MouseButton.Left, RawInputModifiers.None);
        AppHarness.Settle(50);
        Assert.InRange(deck.Deck.Position.TotalSeconds, before, before + 0.5);
        Assert.Equal("Move the crossfader off Deck 1 to jump around in it", deck.Hint);
    }

    [AvaloniaFact]
    public void The_close_up_menu_sets_points_where_it_was_clicked()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        vm.LoadToDeckAsync(vm.Deck1!, app.ToneTrack("Tune", seconds: 60)).GetAwaiter().GetResult();
        DeckViewModel deck = vm.Deck1!;
        deck.SeekTo(0.5);   // 30 s, in the middle of the close-up
        var (view, closeUp, _) = Deck(app);
        double secondsPerPixel = closeUp.WindowSeconds / closeUp.Bounds.Width;

        Point at = Centre(app, closeUp, 100);   // 100 px after the playhead
        app.Window.MouseDown(at, MouseButton.Right, RawInputModifiers.None);
        app.Window.MouseUp(at, MouseButton.Right, RawInputModifiers.None);
        AppHarness.Settle(200);
        var menu = view.FindControl<ContextMenu>("CloseUpMenu")!;
        Assert.True(menu.IsOpen);
        MenuItem cue = menu.Items.OfType<MenuItem>().First(i => ((string)i.Header!).StartsWith("Cue 1 here", StringComparison.Ordinal));
        cue.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        menu.Close();

        Assert.Equal(30 + 100 * secondsPerPixel, deck.Cues.Single().At.TotalSeconds, 1);
        Assert.Equal(TimeSpan.FromSeconds(30), deck.Deck.Position);   // a right-click doesn't seek
    }

    /// <summary>The close-up drawn on its own, as pixels (BGRA).</summary>
    private static byte[] Draw(CloseUpWaveform closeUp, int width, int height)
    {
        closeUp.Measure(new Size(width, height));
        closeUp.Arrange(new Rect(0, 0, width, height));
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(width, height));
        bitmap.Render(closeUp);
        byte[] pixels = new byte[width * height * 4];
        IntPtr buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(pixels.Length);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, width, height), buffer, pixels.Length, width * 4);
            System.Runtime.InteropServices.Marshal.Copy(buffer, pixels, 0, pixels.Length);
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);
        }
        return pixels;
    }

    [AvaloniaFact]
    public void Bars_keep_their_shape_as_the_song_moves_past()
    {
        using var app = new AppHarness();
        string beat = GZJam.Testing.TestWav.WriteSwingBeat(Path.Combine(app.WorkDirectory, "beat.wav"), 140, TimeSpan.FromSeconds(20));
        var closeUp = new CloseUpWaveform { Waveform = new GZJam.Audio.TrackAnalyzer(app.Audio).Detail(beat), Length = 20, WindowSeconds = 8, TrimEnd = 1 };
        const int width = 400, height = 60;
        double barSeconds = 2 * 8.0 / width;

        closeUp.Progress = 10.0 / 20;
        byte[] before = Draw(closeUp, width, height);
        closeUp.Progress = (10 + 7.5 * barSeconds) / 20;   // seven and a half bars on: everything 15 px to the left
        byte[] after = Draw(closeUp, width, height);

        // Away from the playhead (where played turns to unplayed) and the edges, the picture is
        // the same, shifted: no bar has changed shape.
        int same = 0, compared = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 20; x < 150; x++)
            {
                compared++;
                if (Enumerable.Range(0, 4).All(c => Math.Abs(after[(y * width + x) * 4 + c] - before[(y * width + x + 15) * 4 + c]) <= 2))
                    same++;
            }
        }
        Assert.True(same >= compared * 0.98, $"{same} of {compared} pixels the same, shifted");
    }

    [AvaloniaFact]
    public void While_playing_the_playhead_moves_on_between_reports_and_eases_towards_them()
    {
        var closeUp = new CloseUpWaveform { Length = 100, TempoPercent = 0 };
        closeUp.Progress = 0.3;   // 30 s
        closeUp.IsPlaying = true;
        Thread.Sleep(200);
        Assert.InRange(closeUp.Playhead, 30.15, 30.4);   // moving on by itself

        closeUp.Progress = (closeUp.Playhead + 0.05) / 100;   // a report a little ahead: eased, not jumped to
        double eased = closeUp.Playhead;
        Assert.InRange(closeUp.Progress * 100 - eased, 0.03, 0.06);

        closeUp.Progress = 0.6;   // a seek: straight there
        Assert.Equal(60, closeUp.Playhead, 1);

        closeUp.IsPlaying = false;
        closeUp.Progress = 0.5;
        Assert.Equal(50, closeUp.Playhead, 3);   // paused: exactly where the deck is
    }

    [AvaloniaFact]
    public void A_report_off_the_clock_is_caught_up_over_the_next_moments_not_in_one_frame()
    {
        var closeUp = new CloseUpWaveform { Length = 100, TempoPercent = 0 };
        closeUp.Progress = 0.3;
        closeUp.IsPlaying = true;
        Thread.Sleep(100);

        // Reports come in 20 and 40 ms steps: one 30 ms ahead. Before, a tenth of it showed at
        // once, a visible jerk zoomed in tight (3 ms is most of a pixel at 2 s across).
        // (Less the time that really passed meanwhile, as the song moved on; after one report on
        // the clock, so nothing is still being compiled while it's timed.)
        closeUp.Progress = closeUp.Playhead / 100;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        double before = closeUp.Playhead;
        closeUp.Progress = (before + 0.03) / 100;
        double after = closeUp.Playhead;
        Assert.InRange(after - before - clock.Elapsed.TotalSeconds, -0.0005, 0.0005);

        // Over the next moments it catches up the correction (3 ms) as well as moving on.
        clock.Restart();
        double from = closeUp.Playhead;
        Thread.Sleep(600);
        double moved = closeUp.Playhead - from - clock.Elapsed.TotalSeconds;
        Assert.InRange(moved, 0.002, 0.005);   // give or take the test's own timing
    }

    [Fact]
    public void Zoomed_in_tight_a_bar_blends_the_slices_either_side_instead_of_repeating_one()
    {
        var waveform = new GZJam.Audio.Waveform([0, 0, 0, 0], [0, 0, 0, 0], [0, 0, 0, 0], [0, 100, 200, 200]);

        // Bars of 0.8 slices: each one's level is between the slices around its middle.
        var loud = Enumerable.Range(0, 3).Select(i => CloseUpWaveform.BarLevels(waveform, 0.5 + i * 0.8, 1.3 + i * 0.8)!.Value.Loud).ToList();
        Assert.Equal([40, 120, 200], loud.Select(l => (int)l));

        // A bar of a slice or more still takes its loudest slice.
        Assert.Equal(200, CloseUpWaveform.BarLevels(waveform, 0, 3)!.Value.Loud);
        Assert.Null(CloseUpWaveform.BarLevels(waveform, 4, 5));
    }

    [AvaloniaFact]
    public void Four_decks_show_the_whole_song_and_zoom_in_on_demand()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        vm.Settings.FourDecks = true;
        vm.LoadToDeckAsync(vm.Deck3!, app.ToneTrack("Tune", seconds: 60)).GetAwaiter().GetResult();
        var (view, closeUp, whole) = Deck(app, "Deck3View");
        double height = view.Bounds.Height;

        Assert.False(closeUp.IsEffectivelyVisible);
        Assert.False(whole.ShowCloseUp);

        app.Window.MouseWheel(Centre(app, whole), new Vector(0, 1));   // the wheel away from you: zoom in
        AppHarness.Settle(100);
        Assert.True(view.IsZoomed);
        Assert.True(closeUp.IsEffectivelyVisible);
        Assert.False(whole.IsEffectivelyVisible);
        Assert.Equal(height, view.Bounds.Height, 1);

        closeUp.WindowSeconds = CloseUpWaveform.MaxWindowSeconds;
        app.Window.MouseWheel(Centre(app, closeUp), new Vector(0, -1));   // out past the widest: the whole song again
        AppHarness.Settle(100);
        Assert.False(view.IsZoomed);
        Assert.True(whole.IsEffectivelyVisible);
        Assert.Equal(height, view.Bounds.Height, 1);
    }
}
