using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using GZJam.App.ViewModels;
using GZJam.App.Views;

namespace GZJam.App.Tests;

/// <summary>
/// ↑ from the queue's first song goes on up to the deck that plays next (#404): Space previews its
/// song there, as on a queued one, and ↓ comes back into the queue.
/// </summary>
public sealed class QueueUpToDeckTests
{
    private static ListBox Queue(AppHarness app) => app.Window.FindControl<ListBox>("QueueList")!;

    private static void Press(AppHarness app, Key key, PhysicalKey physical)
    {
        app.Window.KeyPress(key, RawInputModifiers.None, physical, null);
        AppHarness.Settle(100);
    }

    private static void KeysOnQueueRow(AppHarness app, int index)
    {
        var queue = Queue(app);
        queue.SelectedIndex = index;
        ((ListBoxItem)queue.ContainerFromIndex(index)!).Focus(NavigationMethod.Directional);
        AppHarness.Settle(100);
    }

    private static bool Shown(AppHarness app, int deck) =>
        app.Window.FindControl<DeckView>($"Deck{deck}View")!.FindControl<Border>("Frame")!.Classes.Contains("key-selected");

    [AvaloniaFact]
    public async Task Up_from_the_first_song_previews_the_next_deck_and_down_comes_back()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        DeckViewModel live = vm.Deck1!, next = vm.Deck2!;
        await vm.LoadToDeckAsync(live, app.ToneTrack("On air", seconds: 60));
        live.TogglePlayCommand.Execute(null);
        app.Play(0.2);
        Assert.True(live.IsOnAir);
        await vm.LoadToDeckAsync(next, app.ToneTrack("Waiting", seconds: 60));
        var songs = app.Library.Results.Take(3).ToList();
        vm.Queue.Add(songs);
        AppHarness.Settle(100);
        KeysOnQueueRow(app, 1);

        Press(app, Key.Up, PhysicalKey.ArrowUp);   // within the queue, as always
        Assert.Equal(0, Queue(app).SelectedIndex);
        Assert.False(next.IsKeySelected);

        Press(app, Key.Up, PhysicalKey.ArrowUp);   // on up to the deck that plays next
        Assert.True(next.IsKeySelected);
        Assert.True(Shown(app, 2));
        Assert.False(live.IsKeySelected);
        Assert.Empty(Queue(app).SelectedItems!);   // one selection shown: the deck's
        Assert.True(Queue(app).IsKeyboardFocusWithin);
        app.Screenshot("queue-up-to-deck");
        vm.DecksHidden = true;   // the decks as rows atop the queue (#177): that row shows it
        AppHarness.Settle(150);
        app.Screenshot("queue-up-to-deck-row");
        Assert.True(next.IsKeySelected);
        vm.DecksHidden = false;
        AppHarness.Settle(150);

        Press(app, Key.Space, PhysicalKey.Space);
        Assert.Equal(next.Track!.Path, vm.Preview!.Track?.Path);
        Assert.False(next.IsPlaying);   // heard in the headphones only

        // Not a list: nothing to open, take out or move to.
        Press(app, Key.Enter, PhysicalKey.Enter);
        Press(app, Key.Delete, PhysicalKey.Delete);
        Press(app, Key.Up, PhysicalKey.ArrowUp);
        Assert.Equal(songs, vm.Queue.Items.Select(i => i.Track));
        Assert.Equal("Waiting", next.Track!.Title);
        Assert.True(next.IsKeySelected);

        Press(app, Key.Down, PhysicalKey.ArrowDown);   // back into the queue
        Assert.False(next.IsKeySelected);
        Assert.False(Shown(app, 2));
        Assert.Equal(0, Queue(app).SelectedIndex);
        Press(app, Key.Space, PhysicalKey.Space);   // the queue's own Space again
        Assert.Same(songs[0], vm.Preview.Track);

        // The keys going elsewhere leave the deck too.
        Press(app, Key.Up, PhysicalKey.ArrowUp);
        Assert.True(next.IsKeySelected);
        app.Window.FindControl<DataGrid>("TrackGrid")!.Focus();
        AppHarness.Settle(100);
        Assert.False(next.IsKeySelected);
    }

    [AvaloniaFact]
    public void With_no_song_waiting_on_the_next_deck_up_stays_in_the_queue()
    {
        using var app = new AppHarness();
        app.ViewModel.Queue.Add(app.Library.Results.Take(2).ToList());
        AppHarness.Settle(100);
        KeysOnQueueRow(app, 0);

        Press(app, Key.Up, PhysicalKey.ArrowUp);

        Assert.All(app.ViewModel.Decks, d => Assert.False(d.IsKeySelected));
        Assert.Equal(0, Queue(app).SelectedIndex);
    }
}
