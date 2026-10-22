using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using GZJam.App.Controls;
using GZJam.App.Views;

namespace GZJam.App.Tests;

/// <summary>Touch mode (finger-sized controls) and fingers on the faders.</summary>
public sealed class TouchModeTests
{
    private static double RowHeight(AppHarness app) =>
        app.Window.FindControl<DataGrid>("TrackGrid")!.GetVisualDescendants().OfType<DataGridRow>().First().Bounds.Height;

    [AvaloniaFact]
    public void Touch_mode_makes_rows_keys_and_the_crossfader_finger_sized_and_is_remembered()
    {
        using var app = new AppHarness();
        app.ViewModel.Queue.Add(app.Library.Results.Take(2));
        AppHarness.Settle(150);
        var deck = app.Window.FindControl<DeckView>("Deck1View")!;
        var cue = deck.FindControl<Button>("CueButton")!;
        var crossfader = app.Window.FindControl<CrossfaderControl>("Crossfader")!;
        var queueRow = (Control)app.Window.FindControl<ListBox>("QueueList")!.ContainerFromIndex(0)!;
        var autoPlay = app.Window.FindControl<ToggleButton>("AutoPlaySwitch")!;

        double rowBefore = RowHeight(app);
        Assert.InRange(rowBefore, 30, 32);
        Assert.Equal(36, cue.Bounds.Height, 0);
        Assert.Equal(44, crossfader.Bounds.Height, 0);
        double queueRowBefore = queueRow.Bounds.Height;

        app.ViewModel.Settings.TouchMode = true;
        AppHarness.Settle(200);
        Assert.True(app.Settings.TouchMode);
        Assert.InRange(RowHeight(app), 44, 46);
        Assert.True(cue.Bounds.Height >= 42, $"cue key {cue.Bounds.Height}");
        Assert.Equal(56, crossfader.Bounds.Height, 0);
        Assert.True(queueRow.Bounds.Height >= 52 && queueRow.Bounds.Height > queueRowBefore, $"queue row {queueRowBefore} -> {queueRow.Bounds.Height}");
        Assert.True(autoPlay.Bounds.Height >= 40, $"auto-play key {autoPlay.Bounds.Height}");

        // Four decks: the compact decks' keys grow too, within the same space.
        app.ViewModel.Settings.FourDecks = true;
        AppHarness.Settle(200);
        Assert.True(deck.FindControl<Button>("CueButton")!.Bounds.Height >= 34, $"compact cue key {deck.FindControl<Button>("CueButton")!.Bounds.Height}");
        Assert.Equal(DeckView.CompactHeight, deck.Bounds.Height, 0);

        app.ViewModel.Settings.TouchMode = false;
        app.ViewModel.Settings.FourDecks = false;
        AppHarness.Settle(200);
        Assert.Equal(rowBefore, RowHeight(app));
        Assert.Equal(44, crossfader.Bounds.Height, 0);
    }

    [AvaloniaFact]
    public void Touch_mode_menus_have_finger_sized_items()
    {
        using var app = new AppHarness();
        app.ViewModel.Settings.TouchMode = true;
        var grid = app.Window.FindControl<DataGrid>("TrackGrid")!;
        grid.SelectedIndex = 0;
        AppHarness.Settle(150);
        var menu = app.Window.FindControl<ContextMenu>("TrackMenu")!;
        menu.Open(grid);
        AppHarness.Settle(200);
        var first = menu.Items.OfType<MenuItem>().First(i => i.IsVisible);
        Assert.True(first.Bounds.Height >= 44, $"menu item is {first.Bounds.Height} px");
        menu.Close();
    }

    [AvaloniaFact]
    public void A_finger_that_misses_the_crossfader_cap_moves_it_from_where_it_is_rather_than_jumping()
    {
        using var app = new AppHarness();
        var crossfader = app.Window.FindControl<CrossfaderControl>("Crossfader")!;
        app.ViewModel.Mixer!.Crossfader = 0;   // all the way to deck 1
        AppHarness.Settle(50);
        double before = crossfader.Value;

        // Touch the far end of the track, well away from the cap: nothing changes.
        Point farEnd = crossfader.TranslatePoint(new Point(crossfader.Bounds.Width - 8, crossfader.Bounds.Height / 2), app.Window)!.Value;
        var finger = app.Window.TouchBegin(farEnd, RawInputModifiers.None);
        AppHarness.Settle(30);
        Assert.Equal(before, crossfader.Value);

        // Sliding left from there moves the cap by as much as the finger moves: left, to the stop.
        app.Window.TouchMove(finger, farEnd - new Vector(30, 0), RawInputModifiers.None);
        app.Window.TouchEnd(finger, farEnd - new Vector(30, 0), RawInputModifiers.None);
        AppHarness.Settle(30);
        Assert.Equal(before, crossfader.Value);

        // A mouse click on the track still jumps the cap there.
        app.Window.MouseDown(farEnd, MouseButton.Left, RawInputModifiers.None);
        app.Window.MouseUp(farEnd, MouseButton.Left, RawInputModifiers.None);
        AppHarness.Settle(30);
        Assert.True(crossfader.Value > before + 0.5);
    }
}
