using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using GZJam.App.ViewModels;
using GZJam.App.Views;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>Track analysis in the app: waveforms on the decks, silence trimming, detected BPMs.</summary>
public sealed class AnalysisAppTests
{
    [AvaloniaFact]
    public void Loading_shows_the_waveform_and_skips_the_silence_at_the_top()
    {
        using var app = new AppHarness();
        DeckViewModel deck = app.ViewModel.Deck1!;
        Track track = app.ToneTrack("Padded", seconds: 5, silenceBefore: 2, silenceAfter: 3);

        app.ViewModel.LoadToDeckAsync(deck, track).GetAwaiter().GetResult();
        AppHarness.WaitFor(() => deck.Waveform is not null);

        Assert.False(deck.IsAnalyzing);
        Assert.InRange(deck.Deck.Position.TotalSeconds, 1.9, 2.0);   // ready at the first note
        Assert.InRange(deck.TrimStart, 0.19, 0.2);
        Assert.InRange(deck.TrimEnd, 0.7, 0.735);
        Assert.Equal("-0:05", deck.ReadoutText);   // time left is music, not trailing silence
        Assert.NotNull(app.Database.GetAnalysis(track.Path, FileVersion.Of(track.Path)!.Value));   // saved for next time
    }

    [AvaloniaFact]
    public void The_song_ends_where_the_music_ends()
    {
        using var app = new AppHarness();
        DeckViewModel deck = app.ViewModel.Deck1!;
        Track track = app.ToneTrack("Padded", seconds: 2, silenceAfter: 4);
        app.ViewModel.LoadToDeckAsync(deck, track).GetAwaiter().GetResult();
        AppHarness.WaitFor(() => deck.Waveform is not null);

        deck.TogglePlayCommand.Execute(null);
        for (int i = 0; i < 40 && !deck.IsFinished; i++)
            app.Play(0.1);

        Assert.True(deck.IsFinished);   // so auto-play's gap starts now, not after 4 s of silence
        Assert.InRange(deck.Deck.Position.TotalSeconds, 2, 2.6);
    }

    private static double Bpm(QueueItem item) => double.Parse(item.BpmText, CultureInfo.CurrentCulture);

    [AvaloniaFact]
    public void A_detected_bpm_shows_in_the_queue_and_can_be_redetected_in_a_range()
    {
        using var app = new AppHarness();
        Track drums = app.BeatTrack("Drums", bpm: 180);
        app.ViewModel.Queue.Add([drums]);
        QueueItem item = app.ViewModel.Queue.Items[0];

        AppHarness.WaitFor(() => item.BpmText != "");
        Assert.InRange(Bpm(item), 177, 183);

        // "This one's slow": detected again between 60 and 110, it's the half-time pulse.
        var slow = MainWindowViewModel.BpmRanges[0].Range;
        Task detecting = app.ViewModel.DetectBpmAsync([drums], slow);
        AppHarness.WaitFor(() => detecting.IsCompleted && Bpm(item) < 110);
        Assert.InRange(Bpm(item), 87, 93);
        Assert.Equal(slow, app.Database.GetBpmRange(drums.Path));   // remembered

        detecting = app.ViewModel.DetectBpmAsync([drums], null);   // back to automatic
        AppHarness.WaitFor(() => detecting.IsCompleted && Bpm(item) > 110);
        Assert.InRange(Bpm(item), 177, 183);
        Assert.Null(app.Database.GetBpmRange(drums.Path));
    }

    [AvaloniaFact]
    public void Right_clicking_a_decks_bpm_offers_the_ranges()
    {
        using var app = new AppHarness();
        DeckViewModel deck = app.ViewModel.Deck1!;
        Track drums = app.BeatTrack("Drums", bpm: 180);
        app.ViewModel.LoadToDeckAsync(deck, drums).GetAwaiter().GetResult();
        AppHarness.WaitFor(() => deck.Waveform is not null);

        var view = app.Window.FindControl<DeckView>("Deck1View")!;
        var panel = view.FindControl<StackPanel>("BpmPanel")!;
        var menu = view.FindControl<ContextMenu>("BpmContextMenu")!;
        Point point = panel.TranslatePoint(new Point(panel.Bounds.Width / 2, panel.Bounds.Height / 2), app.Window)!.Value;
        app.Window.MouseDown(point, MouseButton.Right, RawInputModifiers.None);
        app.Window.MouseUp(point, MouseButton.Right, RawInputModifiers.None);
        AppHarness.Settle(200);
        Assert.True(menu.IsOpen);
        var items = menu.Items.OfType<MenuItem>().ToList();
        Assert.Equal(["BPM", "Automatic", "Detect in a range", "Slow (60–110)", "Medium (100–160)", "Up-tempo (140–220)", "Fast (200–320)", "Type the BPM…"],
            items.Select(i => (string)i.Header!));
        Assert.True(items[1].IsChecked);   // automatic so far

        items.Single(i => (string)i.Header! == "Slow (60–110)").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        menu.Close();
        AppHarness.WaitFor(() => deck.BpmText is var text && double.TryParse(text, out double bpm) && bpm < 110);

        Assert.Equal(new BpmRange(60, 110), drums.BpmRange);
    }
}
