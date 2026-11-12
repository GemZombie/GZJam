using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GZJam.App.Controls;
using GZJam.App.Views;

namespace GZJam.App.Tests;

/// <summary>
/// A deck's transport row is never wider than the deck (#378). In a narrow window Eject, then the
/// headphones, then the BPM give way, and ⋯ comes in with them in its menu; nothing is drawn over
/// the tempo fader or cut off at the deck's edge. Four decks always have ⋯.
/// </summary>
public sealed class DeckFitTests
{
    [AvaloniaTheory]
    [InlineData(2, 1440)]
    [InlineData(2, 1280)]
    [InlineData(2, 1024)]
    [InlineData(4, 1440)]
    [InlineData(4, 1024)]
    public async Task The_transport_row_fits_and_what_gives_way_is_in_the_more_menu(int decks, int width)
    {
        using var app = new AppHarness();
        if (decks == 4)
            Assert.True(app.ViewModel.SetDeckCount(4));
        app.Window.Width = width;
        await app.ViewModel.LoadToDeckAsync(app.ViewModel.Deck1!, app.ToneTrack("Song", 60));
        AppHarness.Settle(300);
        app.Play(0.1);
        AppHarness.Settle(100);

        var view = app.Window.FindControl<DeckView>("Deck1View")!;
        var row = view.FindControl<FitRow>("TransportRow")!;
        var more = view.FindControl<Button>("MoreButton")!;
        var shown = row.Children.Where(c => c.IsVisible && !row.IsDropped(c)).ToList();
        foreach (var part in shown)
        {
            Assert.True(part.Bounds.Right <= row.Bounds.Width + 0.5,
                $"{part.Name ?? part.GetType().Name} ends at {part.Bounds.Right:0}, past the row's {row.Bounds.Width:0}");
        }
        for (int i = 1; i < shown.Count; i++)
            Assert.True(shown[i].Bounds.Left >= shown[i - 1].Bounds.Right, $"{shown[i].Name ?? shown[i].GetType().Name} over its neighbor");

        bool gaveWay = row.HasDropped;
        Assert.Equal(gaveWay || decks == 4, !row.IsDropped(more));
        if (decks == 2 && width == 1440)
            Assert.False(gaveWay);   // a full window: everything on the deck, as before
        if (decks == 2 && width == 1024)
            Assert.True(gaveWay);
        if (!gaveWay && decks == 2)
            return;

        more.Flyout!.ShowAt(more);
        AppHarness.Settle(200);
        var items = view.MoreMenu.Items.OfType<MenuItem>().Select(i => (string)i.Header!).ToList();
        more.Flyout.Hide();
        Assert.Contains("Eject", items);
        Assert.Contains("Preview in headphones", items);
        var bpm = view.FindControl<StackPanel>("BpmPanel")!;
        Assert.Contains(row.IsDropped(bpm) ? "BPM 150" : "BPM", items);
    }
}
