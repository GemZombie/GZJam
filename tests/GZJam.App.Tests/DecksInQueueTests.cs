using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GZJam.App.Controls;
using GZJam.App.ViewModels;
using GZJam.App.Views;

namespace GZJam.App.Tests;

/// <summary>
/// Hidden decks atop the queue (#177): the deck the room hears, then the next, then the queue, as
/// one running order. The line under the top bar stands in for them only while the queue is out of view.
/// </summary>
public sealed class DecksInQueueTests
{
    private static T Find<T>(AppHarness app, string name) where T : Control => app.Window.FindControl<T>(name)!;

    private static List<Border> Rows(AppHarness app) =>
        Find<ItemsControl>(app, "QueueDecks").GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("deck-row")).ToList();

    private static Point Center(AppHarness app, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), app.Window)!.Value;

    [AvaloniaFact]
    public void The_decks_sit_atop_the_queue_in_the_order_the_room_will_hear_them()
    {
        using var app = new AppHarness();
        app.Settings.OnAirPause = OnAirPause.TapTwice;
        var vm = app.ViewModel;
        vm.LoadToDeckAsync(vm.Deck1!, app.ToneTrack("Jump Steady", 60)).GetAwaiter().GetResult();
        vm.Mixer!.Crossfader = 0;
        vm.AddToQueue([app.Library.Results[0], app.Library.Results[1]]);
        vm.DecksHidden = true;
        app.Play(0.05);

        Assert.True(Find<Control>(app, "QueueDecks").IsVisible);
        Assert.False(Find<Control>(app, "AirLine").IsVisible);   // not twice
        Assert.Equal([vm.Deck1!, vm.Deck2!], vm.AirLine!.Order);
        Assert.Equal(2, Rows(app).Count);

        // Deck 1 on air: its row first, with its bar; Deck 2 next, empty for now.
        Rows(app)[0].GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("play")).Command!.Execute(null);
        app.Play(0.2);
        Assert.True(vm.Deck1!.IsOnAir);
        Assert.True(Rows(app)[0].GetVisualDescendants().OfType<AirBar>().Single().IsVisible);
        Assert.False(Rows(app)[1].GetVisualDescendants().OfType<AirBar>().Single().IsVisible);

        // A song dropped on Deck 2's row loads it; on Deck 1's, on air, it's refused.
        var song = app.ToneTrack("Blue Lou", 60);
        app.DragTracks(new MainWindow.TrackDrag([song], null), Center(app, Rows(app)[1]));
        Assert.Equal(song, vm.Deck2!.Track);
        app.DragTracks(new MainWindow.TrackDrag([app.ToneTrack("Not Now")], null), Center(app, Rows(app)[0]));
        Assert.Equal("Jump Steady", vm.Deck1.Title);
        app.Screenshot("decks-in-queue");

        // The crossfader over to Deck 2, playing: it's first now.
        vm.Deck2.TogglePlayCommand.Execute(null);
        vm.Mixer.Crossfader = 1;
        vm.Deck1.TogglePlayCommand.Execute(null);   // two taps: on air, so the first only arms it
        vm.Deck1.TogglePlayCommand.Execute(null);
        app.Play(0.2);
        Assert.Equal([vm.Deck2!, vm.Deck1!], vm.AirLine.Order);
        Assert.Equal("Blue Lou", ((DeckViewModel)Rows(app)[0].DataContext!).Title);

        // History, or the queue folded: the line at the top takes over.
        vm.ShowHistory = true;
        AppHarness.Settle(100);
        Assert.True(Find<Control>(app, "AirLine").IsVisible);
        vm.ShowHistory = false;
        AppHarness.Settle(100);
        Assert.False(Find<Control>(app, "AirLine").IsVisible);
        Find<Button>(app, "FoldQueueButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        AppHarness.Settle(100);
        Assert.True(Find<Control>(app, "AirLine").IsVisible);
        Assert.False(vm.DecksInQueue);
    }

    [AvaloniaFact]
    public void With_the_decks_showing_the_queue_is_as_before()
    {
        using var app = new AppHarness();
        Assert.False(Find<Control>(app, "QueueDecks").IsVisible);
        Assert.False(Find<Control>(app, "AirLine").IsVisible);
    }
}
