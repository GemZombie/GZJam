using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GZJam.App.Controls;
using GZJam.App.Views;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>Rating and energy marks in the library, queue and decks.</summary>
public sealed class MarksAppTests
{
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void Tapping_a_tracks_heart_cycles_its_rating_everywhere_it_shows()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var grid = app.Window.FindControl<DataGrid>("TrackGrid")!;
        Track track = app.Library.Results[0];
        vm.Queue.Add([track]);
        vm.LoadToDeckAsync(vm.Deck1!, track).GetAwaiter().GetResult();
        AppHarness.Settle(200);

        Button HeartButton() => grid.GetVisualDescendants().OfType<RatingIcon>()
            .First(i => i.DataContext == track).FindAncestorOfType<Button>()!;

        Click(HeartButton());
        Assert.Equal(Rating.Loved, track.Rating);
        Assert.Equal(Rating.Loved, vm.Queue.Items[0].Track.Rating);
        Assert.Equal(Rating.Loved, vm.Deck1!.Track!.Rating);
        Assert.Equal(Rating.Loved, app.Database.GetMarks(track.Path).Rating);

        Click(HeartButton());
        Assert.Equal(Rating.Rejected, app.Database.GetMarks(track.Path).Rating);
        Click(HeartButton());
        Assert.Equal(Rating.Unrated, app.Database.GetMarks(track.Path).Rating);

        // The deck's own buttons do the same.
        var deck = app.Window.FindControl<DeckView>("Deck1View")!;
        Click(deck.FindControl<Button>("EnergyButton")!);
        Click(deck.FindControl<Button>("EnergyButton")!);
        Assert.Equal(Energy.Medium, track.Energy);
        Assert.Equal(Energy.Medium, app.Database.GetMarks(track.Path).Energy);
    }

    [AvaloniaFact]
    public void Several_tracks_can_be_marked_at_once_from_the_menu()
    {
        using var app = new AppHarness();
        var grid = app.Window.FindControl<DataGrid>("TrackGrid")!;
        grid.SelectedItems.Add(app.Library.Results[0]);
        grid.SelectedItems.Add(app.Library.Results[1]);
        AppHarness.Settle(100);

        var row = grid.GetVisualDescendants().OfType<DataGridRow>().First(r => r.DataContext == app.Library.Results[0]);
        Point point = row.TranslatePoint(new Point(300, 12), app.Window)!.Value;
        app.Window.MouseDown(point, MouseButton.Right, RawInputModifiers.None);
        app.Window.MouseUp(point, MouseButton.Right, RawInputModifiers.None);
        AppHarness.Settle(200);
        var energy = app.Window.FindControl<MenuItem>("TrackEnergyMenu")!;
        var high = energy.Items.OfType<MenuItem>().Single(i => (string)i.Header! == "High energy");
        high.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        app.Window.FindControl<ContextMenu>("TrackMenu")!.Close();

        Assert.All(app.Library.Results.Take(2), t => Assert.Equal(Energy.High, app.Database.GetMarks(t.Path).Energy));
        Assert.Equal(Energy.Unset, app.Library.Results[2].Energy);

        // Searching again reads them back from the library.
        app.Library.SearchText = "x";
        app.Library.SearchText = "";
        AppHarness.Settle();
        Assert.Equal(2, app.Library.Results.Count(t => t.Energy == Energy.High));
    }
}
