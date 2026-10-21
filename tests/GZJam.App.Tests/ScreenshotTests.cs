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
        using var app = new AppHarness(MusicFolder);
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

        var settings = app.Window.FindControl<Button>("SettingsButton")!;
        settings.Flyout!.ShowAt(settings);
        app.Screenshot("09-settings");
        settings.Flyout.Hide();

        // Right-click Deck 2's BPM: detect it again in a range.
        var bpm = app.Window.FindControl<GZJam.App.Views.DeckView>("Deck2View")!.FindControl<StackPanel>("BpmPanel")!;
        var at = bpm.TranslatePoint(new Avalonia.Point(bpm.Bounds.Width / 2, bpm.Bounds.Height / 2), app.Window)!.Value;
        app.Window.MouseDown(at, MouseButton.Right, RawInputModifiers.None);
        app.Window.MouseUp(at, MouseButton.Right, RawInputModifiers.None);
        AppHarness.Settle(300);
        app.Screenshot("10-bpm-menu");
    }
}
