using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Views;

/// <summary>Decks, headphone preview and settings: loading tracks onto decks (drag or menu), with deck protection.</summary>
public partial class MainWindow
{
    private void InitializeDecks()
    {
        foreach (DeckView deckView in new[] { Deck1View, Deck2View })
        {
            deckView.AddHandler(DragDrop.DragOverEvent, OnDeckDragOver);
            deckView.AddHandler(DragDrop.DragLeaveEvent, (_, _) => deckView.SetDropState(null));
            deckView.AddHandler(DragDrop.DropEvent, OnDeckDrop);
        }

        PreviewSeek.SeekRequested += (_, fraction) => Vm.Preview?.SeekTo(fraction);
        // Opening covers right-click and long-press; Opened also covers menus opened from code.
        TrackMenu.Opening += (_, _) => OnTrackMenuOpening();
        TrackMenu.Opened += (_, _) => OnTrackMenuOpening();
        QueueMenu.Opening += (_, _) => OnQueueMenuOpening();
        QueueMenu.Opened += (_, _) => OnQueueMenuOpening();
        SettingsButton.Flyout!.Opened += (_, _) => Vm.Settings.RefreshDevices();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
                vm.ConfirmAsync = (heading, message) => new ConfirmDialog(heading, message, "Load anyway").ShowDialog<bool>(this);
        };
    }

    private DeckViewModel? DeckAt(int index) => Vm.Decks.ElementAtOrDefault(index);

    // ───────────── Dragging onto a deck ─────────────

    private void OnDeckDragOver(object? sender, DragEventArgs e)
    {
        var view = (DeckView)sender!;
        if (e.DataTransfer.TryGetValue(TrackFormat) is not { Tracks.Count: 1 } drag || view.DataContext is not DeckViewModel deck)
        {
            // A deck takes exactly one track.
            e.DragEffects = DragDropEffects.None;
            view.SetDropState(null);
            return;
        }
        bool allowed = Vm.CanLoad(deck);
        // Match the drag's own effect: tracks from the queue move (they leave it), library tracks copy.
        e.DragEffects = !allowed ? DragDropEffects.None : drag.FromQueue is null ? DragDropEffects.Copy : DragDropEffects.Move;
        view.SetDropState(allowed);
    }

    private async void OnDeckDrop(object? sender, DragEventArgs e)
    {
        var view = (DeckView)sender!;
        view.SetDropState(null);
        if (e.DataTransfer.TryGetValue(TrackFormat) is { Tracks: [var track] } drag && view.DataContext is DeckViewModel deck)
        {
            e.Handled = true;
            if (await Vm.LoadToDeckAsync(deck, track) && drag.FromQueue is { } fromQueue)
                Vm.Queue.Remove(fromQueue);
        }
    }

    // ───────────── Menus ─────────────

    /// <summary>Greys out "Load to Deck X" when protection would refuse it, and says why.</summary>
    private void OnTrackMenuOpening()
    {
        UpdateLoadMenuItems(TrackLoad1, TrackLoad2, SelectedTracks().Count == 1);
        var tracks = SelectedTracks();
        MarksMenu.FillRating(TrackRatingMenu, Vm, tracks);
        MarksMenu.FillEnergy(TrackEnergyMenu, Vm, tracks);
        BpmMenu.Fill(TrackBpmMenu, Vm, tracks);
    }

    private void OnQueueMenuOpening()
    {
        UpdateLoadMenuItems(QueueLoad1, QueueLoad2, SelectedQueueItems().Count == 1);
        var tracks = SelectedQueueItems().Select(i => i.Track).ToList();
        MarksMenu.FillRating(QueueRatingMenu, Vm, tracks);
        MarksMenu.FillEnergy(QueueEnergyMenu, Vm, tracks);
        BpmMenu.Fill(QueueBpmMenu, Vm, tracks);
    }

    private void UpdateLoadMenuItems(MenuItem loadA, MenuItem loadB, bool oneTrackSelected)
    {
        foreach (var (item, index) in new[] { (loadA, 0), (loadB, 1) })
        {
            DeckViewModel? deck = DeckAt(index);
            bool canLoad = deck is not null && Vm.CanLoad(deck);
            item.IsEnabled = oneTrackSelected && canLoad;
            item.Header = deck is { IsPlaying: true } && !canLoad
                ? $"Load to Deck {deck.Label}  (playing)"
                : $"Load to Deck {index + 1}";
        }
    }

    private async void OnLoadSelectedToDeckClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedTracks() is [var track] && DeckAt(int.Parse((string)((MenuItem)sender!).Tag!)) is { } deck)
            await Vm.LoadToDeckAsync(deck, track);
    }

    private async void OnLoadQueueToDeckClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedQueueItems() is [var item] && DeckAt(int.Parse((string)((MenuItem)sender!).Tag!)) is { } deck
            && await Vm.LoadToDeckAsync(deck, item.Track))
            Vm.Queue.Remove([item]);   // loaded: it's no longer waiting in the queue
    }

    // ───────────── Headphone preview ─────────────

    private void OnPreviewTrackClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is Track track)
            Vm.Preview?.Toggle(track);
    }

    private void OnRatingCellClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is Track track)
            Vm.CycleRating(track);
    }

    private void OnEnergyCellClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is Track track)
            Vm.CycleEnergy(track);
    }

    private void OnPreviewQueueItemClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is QueueItem item)
            Vm.Preview?.Toggle(item.Track);
    }

    private void OnPreviewSelectedClick(object? sender, RoutedEventArgs e) => PreviewSelected();

    private void OnPreviewQueueSelectedClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedQueueItems().FirstOrDefault() is { } item)
            Vm.Preview?.Toggle(item.Track);
    }

    private void PreviewSelected()
    {
        if (SelectedTracks().FirstOrDefault() is { } track)
            Vm.Preview?.Toggle(track);
    }

    // ───────────── Settings ─────────────

    private void OnOutputChipClick(object? sender, RoutedEventArgs e) => SettingsButton.Flyout?.ShowAt(SettingsButton);
}
