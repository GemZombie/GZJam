using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using GZJam.App.Controls;
using GZJam.App.ViewModels;
using GZJam.App.Views;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>Setting where a song starts and ends by hand, e.g. after the applause on a live recording.</summary>
public sealed class TrimAppTests
{
    private static DeckViewModel LoadAndAnalyze(AppHarness app, Track track)
    {
        DeckViewModel deck = app.ViewModel.Deck1!;
        app.ViewModel.LoadToDeckAsync(deck, track).GetAwaiter().GetResult();
        AppHarness.WaitFor(() => deck.Waveform is not null);
        return deck;
    }

    [AvaloniaFact]
    public void A_start_set_by_hand_is_where_the_song_starts_from_now_on()
    {
        using var app = new AppHarness();
        Track live = app.ToneTrack("Live", seconds: 20);
        DeckViewModel deck = LoadAndAnalyze(app, live);
        Assert.Equal(TimeSpan.Zero, deck.Deck.Position);

        Assert.True(deck.SetStart(TimeSpan.FromSeconds(4)));   // after the applause

        Assert.Equal(TimeSpan.FromSeconds(4), deck.Deck.Position);   // cued up there
        Assert.True(deck.IsStartManual);
        Assert.InRange(deck.TrimStart, 0.19, 0.21);
        Assert.Equal(TimeSpan.FromSeconds(4), app.Database.GetTrim(live.Path).Start);

        // Next time it's loaded, it starts there straight away, before analysis is even back.
        deck.EjectCommand.Execute(null);
        app.ViewModel.LoadToDeckAsync(deck, live).GetAwaiter().GetResult();
        Assert.Equal(TimeSpan.FromSeconds(4), deck.Deck.Position);
        AppHarness.WaitFor(() => deck.Waveform is not null);
        Assert.Equal(TimeSpan.FromSeconds(4), deck.Deck.Start);   // the analysis doesn't overrule it

        Assert.True(deck.SetStart(null));   // back to automatic
        Assert.False(deck.IsStartManual);
        Assert.Equal(TimeSpan.Zero, deck.Deck.Start);
        Assert.True(app.Database.GetTrim(live.Path).IsEmpty);
    }

    [AvaloniaFact]
    public void An_end_set_by_hand_finishes_the_song_there()
    {
        using var app = new AppHarness();
        DeckViewModel deck = LoadAndAnalyze(app, app.ToneTrack("Long", seconds: 20));

        Assert.True(deck.SetEnd(TimeSpan.FromSeconds(6)));
        deck.TogglePlayCommand.Execute(null);
        for (int i = 0; i < 80 && !deck.IsFinished; i++)
            app.Play(0.1);

        Assert.True(deck.IsFinished);
        Assert.InRange(deck.Deck.Position.TotalSeconds, 6, 6.5);
    }

    [AvaloniaFact]
    public void Impossible_points_are_refused()
    {
        using var app = new AppHarness();
        DeckViewModel deck = LoadAndAnalyze(app, app.ToneTrack("Tone", seconds: 20));

        Assert.False(deck.SetStart(TimeSpan.FromSeconds(18)));   // too close to the end
        Assert.True(deck.SetEnd(TimeSpan.FromSeconds(15)));
        Assert.False(deck.SetStart(TimeSpan.FromSeconds(12)));   // after the new end, nearly

        // The room is hearing it at 8 s: ending it at 5 s would stop it dead.
        deck.TogglePlayCommand.Execute(null);
        app.Play(0.1);
        deck.Deck.Seek(TimeSpan.FromSeconds(8));
        Assert.True(deck.IsOnAir);
        Assert.False(deck.SetEnd(TimeSpan.FromSeconds(7)));
        Assert.Equal("That part has already played", deck.Hint);
        Assert.Equal(TimeSpan.FromSeconds(15), deck.Deck.End);
    }

    [AvaloniaFact]
    public void Right_clicking_the_waveform_offers_to_start_the_song_there()
    {
        using var app = new AppHarness();
        DeckViewModel deck = LoadAndAnalyze(app, app.ToneTrack("Tone", seconds: 20));
        var view = app.Window.FindControl<DeckView>("Deck1View")!;
        var seek = view.FindControl<SeekBar>("Seek")!;
        var menu = view.FindControl<ContextMenu>("TrimMenu")!;

        Point point = seek.TranslatePoint(new Point(seek.Bounds.Width / 4, seek.Bounds.Height / 2), app.Window)!.Value;
        app.Window.MouseDown(point, MouseButton.Right, RawInputModifiers.None);
        app.Window.MouseUp(point, MouseButton.Right, RawInputModifiers.None);
        AppHarness.Settle(200);

        Assert.True(menu.IsOpen);
        Assert.Equal(TimeSpan.Zero, deck.Deck.Position);   // a right-click doesn't seek
        var items = menu.Items.OfType<MenuItem>().ToList();
        Assert.Equal(["Song starts", "Here (0:05.0)", "At the playhead (0:00.0)", "Automatic (0:00.0)",
                      "Song ends", "Here (0:05.0)", "At the playhead (0:00.0)", "Automatic (0:20.0)",
                      "Jump", "Jump from here (0:05.0)", "Jump from the playhead (0:00.0)",
                      "Cue", "Cue 1 here (0:05.0)", "Cue 1 at the playhead (0:00.0)"],
            items.Select(i => (string)i.Header!));

        items[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        menu.Close();

        Assert.Equal(TimeSpan.FromSeconds(5), deck.Deck.Start);
        Assert.True(view.FindControl<SeekBar>("Seek")!.IsStartManual);
    }
}
