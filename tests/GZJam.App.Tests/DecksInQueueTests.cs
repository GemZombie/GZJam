using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GZJam.App.Controls;
using GZJam.App.ViewModels;
using GZJam.App.Views;
using GZJam.Library;

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
    public void The_decks_atop_the_queue_show_what_the_queue_shows()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var tone = app.ToneTrack("Jump Steady", 60);
        var song = new Track { Path = tone.Path, Folder = "", FileName = tone.FileName, Title = tone.Title, Artist = "Count Basie",
            Album = "The Complete Decca Recordings", Year = 1938, TagBpm = 150 };
        vm.LoadToDeckAsync(vm.Deck1!, song).GetAwaiter().GetResult();
        vm.Deck1!.TempoPercent = 4;
        vm.AddToQueue([app.Library.Results[0]]);
        app.Window.SetQueueColumnShown("Album", true);
        vm.DecksHidden = true;
        var panes = app.Window.FindControl<Grid>("PanesGrid")!;
        panes.ColumnDefinitions[4].Width = new GridLength(560);   // the widest the queue goes, room for the album
        AppHarness.Settle(200);

        FitRow Deck(int row) => Rows(app)[row].GetVisualDescendants().OfType<FitRow>().First();
        Control Part(FitRow row, string part) => row.Children.Single(c => c.Classes.Contains(part));
        bool Shows(FitRow row, string part) => Part(row, part).IsEffectivelyVisible && !row.IsDropped(Part(row, part));

        // Deck 1: the album, as the queue shows it now, and the BPM as the room hears it, pitch and all.
        Assert.True(Shows(Deck(0), "q-album"));
        Assert.True(Shows(Deck(0), "q-bpm"));
        Assert.Equal(vm.Deck1.BpmText, ((TextBlock)Part(Deck(0), "q-bpm")).Text);
        Assert.Contains("156", vm.Deck1.BpmText);
        Assert.False(Shows(Deck(0), "q-genre"));   // not ticked in the queue
        app.Screenshot("decks-in-queue-show");

        // The BPM lines up with the queue's below it.
        var queued = app.Window.FindControl<ListBox>("QueueList")!.GetVisualDescendants().OfType<FitRow>().First();
        double Right(FitRow row) => Part(row, "q-bpm").TranslatePoint(new Point(Part(row, "q-bpm").Bounds.Width, 0), app.Window)!.Value.X;
        Assert.Equal(Right(queued), Right(Deck(0)), 1);

        // Deck 2, empty: only its name and "Drag a song here".
        Assert.DoesNotContain(Deck(1).Children, c => c.Classes.Contains("song") && c.IsEffectivelyVisible);

        // Unticked in the queue's menu, gone from the decks too.
        app.Window.SetQueueColumnShown("BPM", false);
        AppHarness.Settle(100);
        Assert.False(Shows(Deck(0), "q-bpm"));

        // However narrow, nothing overlaps; the album gives way first, the time left never.
        foreach (int width in new[] { 120, 180, 260, 320, 560 })
        {
            panes.ColumnDefinitions[4].Width = new GridLength(width);
            AppHarness.Settle(150);
            QueueColumnsTests.AssertFits(Deck(0), $"{width} px");
            QueueColumnsTests.AssertFits(Deck(1), $"{width} px");
        }
        Assert.True(Deck(0).Children.Last().IsEffectivelyVisible && !Deck(0).IsDropped(Deck(0).Children.Last()));
        panes.ColumnDefinitions[4].Width = new GridLength(320);
        AppHarness.Settle(150);
        Assert.False(Shows(Deck(0), "q-album"));
    }

    [AvaloniaFact]
    public void With_the_decks_showing_the_queue_is_as_before()
    {
        using var app = new AppHarness();
        Assert.False(Find<Control>(app, "QueueDecks").IsVisible);
        Assert.False(Find<Control>(app, "AirLine").IsVisible);
    }
}
