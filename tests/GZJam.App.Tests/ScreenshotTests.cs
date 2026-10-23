using Avalonia;
using Avalonia.VisualTree;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace GZJam.App.Tests;

/// <summary>
/// Renders the window over a real music folder (read-only) and saves PNGs to
/// %TEMP%\gzjam-screenshots for eyeballing the layout. Skipped when the folder isn't there.
/// Point GZJAM_SCREENSHOT_MUSIC at your library to use a different folder.
/// </summary>
public sealed class ScreenshotTests
{
    private static readonly string MusicFolder = Environment.GetEnvironmentVariable("GZJAM_SCREENSHOT_MUSIC") ?? @"C:\music";

    [AvaloniaFact]
    public void Real_library_screenshots()
    {
        Assert.SkipUnless(Directory.Exists(MusicFolder), $"{MusicFolder} not found");
        using var app = new AppHarness(MusicFolder, new AppSettings());   // default settings: loudness matching on
        var library = app.Library;

        app.Screenshot("01-whole-library");

        // Drill into the biggest style folder, as a DJ would.
        var root = library.Roots[0];
        var style = root.SelfAndDescendants().Where(n => n.Node.RelativePath.Count(c => c == '/') == 1)
            .OrderByDescending(n => n.Node.TrackCount).First();
        library.Reveal(style.Node.RootId, style.Node.RelativePath);
        style.Parent!.IsExpanded = true;
        style.IsExpanded = true;
        AppHarness.Settle();
        app.Screenshot("02-folder-selected");

        library.SearchText = "jump";
        AppHarness.Settle();
        var tracks = library.Results;
        // Some marks, as a DJ would have made them (in the throwaway database, not the files).
        app.ViewModel.SetRating([tracks[0], tracks[1], tracks[4]], GZJam.Library.Rating.Loved);
        app.ViewModel.SetRating([tracks[5]], GZJam.Library.Rating.Rejected);
        app.ViewModel.SetEnergy([tracks[0], tracks[2]], GZJam.Library.Energy.High);
        app.ViewModel.SetEnergy([tracks[1]], GZJam.Library.Energy.Medium);
        app.ViewModel.SetEnergy([tracks[4]], GZJam.Library.Energy.Low);
        app.ViewModel.SetLabel([tracks[0], tracks[2], tracks[4]], "Lindy", on: true);
        app.ViewModel.SetLabel([tracks[0]], "Balboa", on: true);
        app.ViewModel.SetLabel([tracks[5]], "Female vocals", on: true);
        app.ViewModel.Queue.Add(tracks.Take(5));
        var grid = app.Window.FindControl<DataGrid>("TrackGrid")!;
        grid.SelectedIndex = 3;
        AppHarness.Settle();
        app.Screenshot("03-search-and-queue");

        // Right-click a row to show the track menu.
        grid.ScrollIntoView(grid.SelectedItem, null);
        AppHarness.Settle(200);
        var row = grid.GetVisualDescendants().OfType<DataGridRow>().First(r => r.DataContext == grid.SelectedItem);
        var point = row.TranslatePoint(new Avalonia.Point(200, 12), app.Window)!.Value;
        app.Window.MouseDown(point, MouseButton.Right, RawInputModifiers.None);
        app.Window.MouseUp(point, MouseButton.Right, RawInputModifiers.None);
        AppHarness.Settle(300);
        app.Screenshot("04-context-menu");
        app.Window.FindControl<ContextMenu>("TrackMenu")!.Close();

        // A set in progress: A on air, B cued up, a third track previewing in the headphones.
        var vm = app.ViewModel;
        vm.LoadToDeckAsync(vm.Deck1!, tracks[1]).GetAwaiter().GetResult();
        vm.LoadToDeckAsync(vm.Deck2!, tracks[2]).GetAwaiter().GetResult();
        AppHarness.WaitFor(() => vm.Deck1!.Waveform is not null && vm.Deck2!.Waveform is not null, TimeSpan.FromSeconds(30));
        vm.Deck1!.TogglePlayCommand.Execute(null);
        vm.Deck2!.TempoPercent = -2.5;
        vm.Deck2.IsCueOn = true;
        vm.Deck2.TogglePlayCommand.Execute(null);
        app.Play(25);
        vm.Deck2.SeekTo(0.6);
        vm.Deck2.TogglePlayCommand.Execute(null);
        vm.Preview!.Toggle(tracks[4]);
        app.Play(8);
        app.Screenshot("05-decks-playing");

        // The live deck refusing a drop.
        vm.LoadToDeckAsync(vm.Deck1, tracks[6]).GetAwaiter().GetResult();
        vm.Deck1.TogglePlayCommand.Execute(null);   // a tap on the live deck: asks for a hold
        app.Screenshot("06-protected-deck");

        vm.AutoPlay!.IsOn = true;
        app.Play(0.1);
        app.Screenshot("07-auto-play");

        // Deck 1 has been on air for over 30 s by now, so it's in tonight's history.
        vm.ShowHistory = true;
        app.Play(0.1);
        app.Screenshot("08-history");
        var sessionMenu = app.Window.FindControl<Button>("SessionMenuButton")!;
        sessionMenu.Flyout!.ShowAt(sessionMenu);
        AppHarness.Settle(200);
        app.Screenshot("08b-session-menu");
        sessionMenu.Flyout.Hide();

        app.Window.OpenSettings(GZJam.App.Views.SettingsSection.Sound);
        app.Screenshot("09-settings");
        app.Window.CloseSettings();

        // Right-click Deck 2's BPM: detect it again in a range.
        var bpm = app.Window.FindControl<GZJam.App.Views.DeckView>("Deck2View")!.FindControl<StackPanel>("BpmPanel")!;
        var at = bpm.TranslatePoint(new Avalonia.Point(bpm.Bounds.Width / 2, bpm.Bounds.Height / 2), app.Window)!.Value;
        app.Window.MouseDown(at, MouseButton.Right, RawInputModifiers.None);
        app.Window.MouseUp(at, MouseButton.Right, RawInputModifiers.None);
        AppHarness.Settle(300);
        app.Screenshot("10-bpm-menu");
        app.Window.FindControl<GZJam.App.Views.DeckView>("Deck2View")!.FindControl<ContextMenu>("BpmContextMenu")!.Close();

        // The BPM filter by the search box.
        var filter = app.Window.FindControl<Button>("BpmFilterButton")!;
        var on = filter.TranslatePoint(new Avalonia.Point(filter.Bounds.Width / 2, filter.Bounds.Height / 2), app.Window)!.Value;
        app.Window.MouseDown(on, MouseButton.Left, RawInputModifiers.None);
        app.Window.MouseUp(on, MouseButton.Left, RawInputModifiers.None);
        AppHarness.Settle(300);
        Assert.True(filter.Flyout!.IsOpen, "BPM filter menu didn't open");
        app.Screenshot("11-bpm-filter");
        filter.Flyout.Hide();
        library.SearchText = "~bpm:180 -rating:no";
        AppHarness.Settle();
        app.Screenshot("12-bpm-filtered");

        // Deck 2: the song set to start a few seconds in (say, after applause); its waveform menu.
        vm.Deck2.SetStart(TimeSpan.FromSeconds(12));
        // ...and a jump that skips a middle chorus.
        vm.Deck2.StartJump(vm.Deck2.Deck.Length * 0.68);
        vm.Deck2.LandJump(vm.Deck2.Deck.Length * 0.86);
        var seek2 = app.Window.FindControl<GZJam.App.Views.DeckView>("Deck2View")!.FindControl<GZJam.App.Controls.SeekBar>("Seek")!;
        var at2 = seek2.TranslatePoint(new Avalonia.Point(seek2.Bounds.Width * 0.9, seek2.Bounds.Height / 2), app.Window)!.Value;
        app.Window.MouseDown(at2, MouseButton.Right, RawInputModifiers.None);
        app.Window.MouseUp(at2, MouseButton.Right, RawInputModifiers.None);
        AppHarness.Settle(300);
        app.Screenshot("13-trim-menu");
        seek2.ContextMenu!.Close();

        // Deck 1, on air: two cues (after the solo, the outro), a key for each.
        vm.Deck1.AddCue(vm.Deck1.Deck.Length * 0.35);
        vm.Deck1.AddCue(vm.Deck1.Deck.Length * 0.79);
        app.Play(0.2);
        app.Screenshot("13b-cue-keys");

        // Four decks in the space of two: Deck 3 ready under Deck 1, Deck 4 empty.
        vm.SetDeckCount(4);
        vm.LoadToDeckAsync(vm.Deck3!, tracks[7]).GetAwaiter().GetResult();
        AppHarness.WaitFor(() => vm.Deck3!.Waveform is not null, TimeSpan.FromSeconds(30));
        vm.Deck3!.TempoPercent = 1.5;
        library.SearchText = "jump";
        app.Play(0.2);
        app.Screenshot("14-four-decks");

        // Deck 3's EQ: the treble down for a hissy transfer.
        vm.Deck3.LessHissCommand.Execute(null);
        vm.Deck3.EqLow = 1.5;
        var eq = app.Window.FindControl<GZJam.App.Views.DeckView>("Deck3View")!.FindControl<Button>("CompactEqButton")!;
        eq.Flyout!.ShowAt(eq);
        app.Screenshot("15-eq");
        eq.Flyout.Hide();
        vm.Deck3.FlatEqCommand.Execute(null);

        // Touch mode: four decks, then two, then the track menu with finger-sized items.
        vm.Settings.TouchMode = true;
        app.Play(0.2);
        app.Screenshot("16-touch-four-decks");
        vm.SetDeckCount(2);
        app.Play(0.2);
        app.Screenshot("17-touch-two-decks");
        grid.SelectedIndex = 2;
        grid.ScrollIntoView(grid.SelectedItem, null);
        AppHarness.Settle(200);
        var touchRow = grid.GetVisualDescendants().OfType<DataGridRow>().First(r => r.DataContext == grid.SelectedItem);
        var touchAt = touchRow.TranslatePoint(new Avalonia.Point(200, 20), app.Window)!.Value;
        app.Window.MouseDown(touchAt, MouseButton.Right, RawInputModifiers.None);
        app.Window.MouseUp(touchAt, MouseButton.Right, RawInputModifiers.None);
        AppHarness.Settle(300);
        app.Screenshot("18-touch-menu");
        app.Window.FindControl<ContextMenu>("TrackMenu")!.Close();

        // Dragging a song by finger: the tag above the finger, the queue showing where it goes.
        vm.ShowHistory = false;
        AppHarness.Settle(100);
        var queueArea = app.Window.FindControl<Panel>("QueueArea")!;
        var over = queueArea.TranslatePoint(new Avalonia.Point(queueArea.Bounds.Width / 2, 70), app.Window)!.Value;
        var finger = app.Window.TouchBegin(touchAt, RawInputModifiers.None);
        for (int i = 1; i <= 10; i++)
        {
            app.Window.TouchMove(finger, touchAt + (over - touchAt) * (i / 10.0), RawInputModifiers.None);
            AppHarness.Settle(10);
        }
        app.Screenshot("19-touch-drag");
        app.Window.TouchEnd(finger, over, RawInputModifiers.None);
        AppHarness.Settle(100);
        vm.Settings.TouchMode = false;
    }
}
