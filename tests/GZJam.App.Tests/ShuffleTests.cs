using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>A playlist added to the queue shuffled, and the queue shuffled, with Undo (#254).</summary>
public sealed class ShuffleTests
{
    private static List<string> Queued(AppHarness app) => app.ViewModel.Queue.Items.Select(i => i.Track.Path).ToList();

    private static void Undo(AppHarness app)
    {
        var undo = app.Window.FindControl<Button>("NoticeUndoButton")!;
        Assert.True(undo.IsEffectivelyVisible);
        undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    [AvaloniaFact]
    public void Shuffling_the_queue_changes_its_order_keeps_its_songs_and_Undo_puts_it_back()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var songs = app.Library.Results.Take(4).ToList();
        vm.AddToQueue(songs);
        var before = Queued(app);

        vm.ShuffleQueue();

        Assert.NotEqual(before, Queued(app));   // never the same order
        Assert.Equal(before.Order(), Queued(app).Order());
        Assert.Equal([1, 2, 3, 4], vm.Queue.Items.Select(i => i.Number));
        Assert.Equal("Queue shuffled: 4 songs", vm.Notice);
        AppHarness.Settle();
        Undo(app);
        Assert.Equal(before, Queued(app));
    }

    [AvaloniaFact]
    public void Undo_keeps_songs_added_after_the_shuffle_and_leaves_out_songs_taken_out()
    {
        var queue = new QueueViewModel();
        using var app = new AppHarness();
        var songs = app.Library.Results.Take(5).ToList();
        queue.Add(songs.Take(4));
        var original = queue.Items.ToList();

        var before = queue.Shuffle(new Random(1));
        queue.Remove([original[1]]);
        queue.Add([songs[4]]);
        queue.Reorder(before);

        Assert.Equal(new[] { songs[0], songs[2], songs[3], songs[4] }, queue.Items.Select(i => i.Track));
    }

    [AvaloniaFact]
    public void A_queue_of_one_song_has_nothing_to_shuffle()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        vm.AddToQueue([app.Library.Results[0]]);

        vm.ShuffleQueue();

        Assert.Single(vm.Queue.Items);
        Assert.Null(vm.Notice);
    }

    [AvaloniaFact]
    public void A_playlist_goes_onto_the_end_of_the_queue_shuffled()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var songs = app.Library.Results.Take(5).ToList();
        vm.Queue.Add([songs[4]]);
        var playlist = vm.CreatePlaylist("Class", songs.Take(4).ToList())!;
        AppHarness.Settle(150);

        vm.AddPlaylistToQueue(playlist, shuffled: true);

        var queued = Queued(app);
        Assert.Equal(songs[4].Path, queued[0]);   // what was there stays first
        Assert.Equal(songs.Take(4).Select(s => s.Path).Order(), queued.Skip(1).Order());
        Assert.Equal("4 songs added to the queue, shuffled", vm.Notice);
    }

    [AvaloniaFact]
    public void The_menus_offer_shuffling()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var songs = app.Library.Results.Take(3).ToList();
        var playlist = vm.CreatePlaylist("Class", songs)!;
        AppHarness.Settle(150);
        app.Library.OpenPlaylist(playlist.Id);
        AppHarness.Settle();

        app.Window.FindControl<MenuItem>("PlaylistToQueueShuffledItem")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(songs.Select(s => s.Path).Order(), Queued(app).Order());

        var before = Queued(app);
        app.Window.FindControl<MenuItem>("QueueShuffleItem")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.NotEqual(before, Queued(app));
    }
}
