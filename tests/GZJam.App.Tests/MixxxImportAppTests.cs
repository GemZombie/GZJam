using Avalonia.Controls;
using ToggleButton = Avalonia.Controls.Primitives.ToggleButton;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using GZJam.App.ViewModels;
using GZJam.App.Views;
using GZJam.Library;
using GZJam.Testing;

namespace GZJam.App.Tests;

/// <summary>Settings › Import from Mixxx, and typing a BPM.</summary>
public sealed class MixxxImportAppTests
{
    [AvaloniaFact]
    public void Importing_from_mixxx_shows_what_will_come_over_then_brings_it_into_the_library_and_history()
    {
        using var app = new AppHarness();
        Track first = app.Library.Results[0], second = app.Library.Results[1];
        string mixxxDb = Path.Combine(app.WorkDirectory, "mixxx", "mixxxdb.sqlite");
        Directory.CreateDirectory(Path.GetDirectoryName(mixxxDb)!);
        var mixxx = new FakeMixxx(mixxxDb);
        long a = mixxx.Song(first.Path, first.DisplayTitle, stars: 5, bpm: 236, locked: true);
        long b = mixxx.Song(second.Path, second.DisplayTitle, stars: 1);
        mixxx.Crate("Lindy", a, b);
        mixxx.Night("2024-03-14", (a, new DateTime(2024, 3, 14, 20, 0, 0, DateTimeKind.Utc)), (b, new DateTime(2024, 3, 14, 20, 3, 0, DateTimeKind.Utc)));
        mixxx.Close();

        MixxxImportViewModel import = app.ViewModel.CreateMixxxImport();
        var dialog = new MixxxImportDialog(import);
        AppHarness.Await(import.LoadAsync(mixxxDb));
        AppHarness.Settle(100);

        Assert.True(import.HasPlan);
        Assert.StartsWith("2 songs in Mixxx; 2 of them", import.Status);
        Assert.StartsWith("2 songs: 4 and 5 stars become ♥", import.RatingsText);
        Assert.StartsWith("1 crate, 2 songs", import.CratesText);
        Assert.StartsWith("1 night, 2 songs played", import.HistoryText);
        Assert.StartsWith("1 song with a BPM you locked", import.BpmsText);
        Assert.Equal("", import.MissingText);
        Assert.True(import.CanImport);

        dialog.Show();
        AppHarness.Settle(100);
        var shot = dialog.CaptureRenderedFrame();   // the dialog's own window
        Directory.CreateDirectory(AppHarness.ScreenshotDirectory);
        shot?.Save(Path.Combine(AppHarness.ScreenshotDirectory, "mixxx-import-dialog.png"));

        dialog.FindControl<ToggleButton>("HistorySwitch")!.IsChecked = false;   // everything but History
        MixxxImportResult? result = AppHarness.Await(import.ImportAsync());
        dialog.Close();
        AppHarness.Settle();

        Assert.Equal(new MixxxImportResult(2, 1, 2, 0, 0, 1), result);
        Assert.Equal("Imported from Mixxx: 2 ratings, 1 crate as labels, 1 BPM.", MixxxImportViewModel.Summary(result!));
        Track nowFirst = app.Library.ResultFor(first.Path)!;
        Assert.Equal(Rating.Loved, nowFirst.Rating);
        Assert.Equal(236, nowFirst.Bpm);
        Assert.Equal(["Lindy"], nowFirst.Labels);
        Assert.Equal(Rating.Rejected, app.Library.ResultFor(second.Path)!.Rating);
        Assert.DoesNotContain(app.ViewModel.History.Sessions, s => s.Session is not null);
        Assert.Contains("Lindy", app.Library.LabelFilterLabel + string.Join(",", app.Database.GetLabels().Select(l => l.Name)));
    }

    [AvaloniaFact]
    public void Without_mixxx_the_dialog_says_where_to_look()
    {
        using var app = new AppHarness();
        MixxxImportViewModel import = app.ViewModel.CreateMixxxImport();
        AppHarness.Await(import.LoadAsync(Path.Combine(app.WorkDirectory, "not-mixxx.sqlite")));
        Assert.False(import.HasPlan);
        Assert.False(import.CanImport);
        Assert.StartsWith("That doesn't look like a Mixxx library", import.Status);
    }

    [AvaloniaFact]
    public void A_typed_bpm_wins_until_automatic_is_chosen_again()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        Track song = app.Library.Results[0];
        vm.LoadToDeckAsync(vm.Deck1!, song).GetAwaiter().GetResult();
        vm.PromptAsync = (_, _, _, _) => Task.FromResult<string?>("156.5");

        // The library's track menu › BPM › Type the BPM…
        var grid = app.Window.FindControl<DataGrid>("TrackGrid")!;
        grid.SelectedItem = song;
        var menu = app.Window.FindControl<ContextMenu>("TrackMenu")!;
        menu.Open(grid);
        AppHarness.Settle(150);
        var bpmMenu = app.Window.FindControl<MenuItem>("TrackBpmMenu")!;
        var type = bpmMenu.Items.OfType<MenuItem>().Single(i => ((string)i.Header!).StartsWith("Type the BPM", StringComparison.Ordinal));
        type.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        AppHarness.Settle(150);
        menu.Close();

        Assert.Equal(156.5, song.Bpm);
        Assert.Equal(156.5, app.Database.GetManualBpm(song.Path));
        Assert.Equal("156.5", vm.Deck1!.BpmText);
        app.Library.SearchText = "bpm:156.5";
        AppHarness.Settle();
        Assert.Contains(app.Library.Results, t => t.Path == song.Path);

        AppHarness.Await(vm.DetectBpmAsync([song], null));   // Automatic
        Assert.Null(song.ManualBpm);
        Assert.Null(app.Database.GetManualBpm(song.Path));
    }
}
