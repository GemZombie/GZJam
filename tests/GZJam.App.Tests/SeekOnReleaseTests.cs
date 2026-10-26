using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using GZJam.App.Controls;
using GZJam.App.ViewModels;
using GZJam.App.Views;

namespace GZJam.App.Tests;

/// <summary>
/// A press or drag on a position bar moves the song once, when it lets go: a finger held on the
/// bar, never quite still, made the sound stutter while it moved the song at every tiny move.
/// </summary>
public sealed class SeekOnReleaseTests
{
    private static Point At(AppHarness app, SeekBar bar, double fraction) =>
        bar.TranslatePoint(new Point(fraction * bar.Bounds.Width, bar.Bounds.Height / 2), app.Window)!.Value;

    /// <summary>A finger pressed and held: it wobbles a pixel or two, then moves on, then lets go.</summary>
    private static void PressWobbleAndDrag(AppHarness app, SeekBar bar, double pressAt, double letGoAt, Action whileHeld)
    {
        app.Window.MouseDown(At(app, bar, pressAt), MouseButton.Left, RawInputModifiers.None);
        foreach (double wobble in new[] { 0.002, -0.001, 0.003, 0.0 })
            app.Window.MouseMove(At(app, bar, pressAt + wobble), RawInputModifiers.LeftMouseButton);
        app.Window.MouseMove(At(app, bar, letGoAt), RawInputModifiers.LeftMouseButton);
        whileHeld();
        app.Window.MouseUp(At(app, bar, letGoAt), MouseButton.Left, RawInputModifiers.None);
    }

    [AvaloniaFact]
    public async Task On_a_deck_the_song_moves_when_the_finger_lets_go()
    {
        using var app = new AppHarness();
        DeckViewModel deck = app.ViewModel.Deck2!;   // not the room's: free to move
        await app.ViewModel.LoadToDeckAsync(deck, app.ToneTrack("Tune", 60));
        AppHarness.Settle(100);
        var bar = app.Window.FindControl<DeckView>("Deck2View")!.FindControl<SeekBar>("Seek")!;
        double before = deck.Deck.Position.TotalSeconds;

        PressWobbleAndDrag(app, bar, 0.5, 0.75, () =>
            Assert.Equal(before, deck.Deck.Position.TotalSeconds, 2));   // held: still where it was

        Assert.InRange(deck.Deck.Position.TotalSeconds, 44, 46);
    }

    [AvaloniaFact]
    public void The_preview_plays_on_while_the_finger_is_down()
    {
        using var app = new AppHarness();
        var preview = app.ViewModel.Preview!;
        preview.Toggle(app.ToneTrack("Previewed", 60));
        app.Play(0.5);
        AppHarness.Settle(100);
        var bar = app.Window.FindControl<SeekBar>("PreviewSeek")!;
        double before = preview.Progress;
        Assert.True(before < 0.05);

        PressWobbleAndDrag(app, bar, 0.4, 0.6, () =>
        {
            app.Play(0.2);
            Assert.InRange(preview.Progress, before, 0.05);   // still playing from where it was
        });

        Assert.InRange(preview.Progress, 0.59, 0.61);
    }
}
