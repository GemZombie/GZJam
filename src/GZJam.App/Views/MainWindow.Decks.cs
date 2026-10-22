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
        PreviewSeek.SeekRequested += (_, fraction) => Vm.Preview?.SeekTo(fraction);
        // Opening covers right-click and long-press; Opened also covers menus opened from code.
        TrackMenu.Opening += (_, _) => OnTrackMenuOpening();
        TrackMenu.Opened += (_, _) => OnTrackMenuOpening();
        QueueMenu.Opening += (_, _) => OnQueueMenuOpening();
        QueueMenu.Opened += (_, _) => OnQueueMenuOpening();
        SettingsButton.Flyout!.Opened += (_, _) => Vm.Settings.RefreshDevices();
        // Touch mode's sizes are app-wide; take them away with the window (tests open many windows).
        Closed += (_, _) => GZJam.App.Styles.TouchStyles.Apply(false);

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not MainWindowViewModel vm)
                return;
            vm.ConfirmAsync = (heading, message, confirm) => new ConfirmDialog(heading, message, confirm).ShowDialog<bool>(this);
            vm.PromptAsync = (heading, message, ok, initial) => new PromptDialog(heading, message, ok, initial).ShowDialog<string?>(this);
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainWindowViewModel.FourDecks))
                    ApplyDeckLayout();
            };
            vm.Settings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SettingsViewModel.TouchMode))
                    ApplyTouchMode();
            };
            ApplyDeckLayout();
            ApplyTouchMode();
        };
    }

    private DeckView[] DeckViews => [Deck1View, Deck2View, Deck3View, Deck4View];

    /// <summary>Finger-sized controls everywhere, or the usual sizes (Settings › Touch mode).</summary>
    private void ApplyTouchMode()
    {
        bool on = Vm.Settings.TouchMode;
        GZJam.App.Styles.TouchStyles.Apply(on);
        foreach (DeckView view in DeckViews)
            view.IsTouch = on;
    }

    /// <summary>Two full-height decks, or four at half height in the same space.</summary>
    private void ApplyDeckLayout()
    {
        foreach (DeckView view in DeckViews)
            view.IsCompact = Vm.FourDecks;
        Deck3View.IsVisible = Deck4View.IsVisible = Vm.FourDecks;
    }

    private DeckViewModel? DeckAt(int index) => Vm.Decks.ElementAtOrDefault(index);

    // ───────────── Import from Mixxx ─────────────

    private async void OnMixxxImportClick(object? sender, RoutedEventArgs e)
    {
        SettingsButton.Flyout!.Hide();
        var result = await new MixxxImportDialog(Vm.CreateMixxxImport()).ShowDialog<GZJam.Library.MixxxImportResult?>(this);
        if (result is not null)
            Vm.ShowNotice(MixxxImportViewModel.Summary(result));
    }

    // ───────────── Menus ─────────────

    /// <summary>Greys out "Load to Deck X" when protection would refuse it, and says why.</summary>
    private void OnTrackMenuOpening()
    {
        UpdateLoadMenuItems([TrackLoad1, TrackLoad2, TrackLoad3, TrackLoad4], SelectedTracks().Count == 1);
        var tracks = SelectedTracks();
        MarksMenu.FillRating(TrackRatingMenu, Vm, tracks);
        MarksMenu.FillEnergy(TrackEnergyMenu, Vm, tracks);
        LabelsMenu.Fill(TrackLabelsMenu, Vm, tracks);
        BpmMenu.Fill(TrackBpmMenu, Vm, tracks);
    }

    private void OnQueueMenuOpening()
    {
        UpdateLoadMenuItems([QueueLoad1, QueueLoad2, QueueLoad3, QueueLoad4], SelectedQueueItems().Count == 1);
        var tracks = SelectedQueueItems().Select(i => i.Track).ToList();
        MarksMenu.FillRating(QueueRatingMenu, Vm, tracks);
        MarksMenu.FillEnergy(QueueEnergyMenu, Vm, tracks);
        LabelsMenu.Fill(QueueLabelsMenu, Vm, tracks);
        BpmMenu.Fill(QueueBpmMenu, Vm, tracks);
    }

    private void UpdateLoadMenuItems(MenuItem[] items, bool oneTrackSelected)
    {
        for (int index = 0; index < items.Length; index++)
        {
            MenuItem item = items[index];
            DeckViewModel? deck = DeckAt(index);
            item.IsVisible = index < Vm.ActiveDecks.Count;
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
