using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
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
        SettingsPanel.CloseRequested += (_, _) => CloseSettings();
        DeckGrip.PointerPressed += OnDeckGripPressed;
        DeckGrip.PointerMoved += OnDeckGripMoved;
        DeckGrip.PointerReleased += OnDeckGripReleased;
        SettingsPanel.MixxxImportRequested += (_, _) => ImportFromMixxx();
        SettingsPanel.PlaylistImportRequested += (_, _) => ImportPlaylists();
        SettingsPanel.FileRatingsRequested += (_, _) => ImportFileRatings();
        // Touch mode's sizes are app-wide; take them away with the window (tests open many windows).
        Closed += (_, _) => GZJam.App.Styles.TouchStyles.Apply(false);

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not MainWindowViewModel vm)
                return;
            vm.ConfirmAsync = (heading, message, confirm) => new ConfirmDialog(heading, message, confirm).ShowDialog<bool>(this);
            vm.ConfirmCloseAsync = playing => new ConfirmDialog("Stop the music and close GZJam?", playing, "Close GZJam",
                cancelText: "Keep playing", cancelIsDefault: true).ShowDialog<bool>(this);
            vm.ConfirmImportAsync = (heading, message, confirm) =>
            {
                var dialog = new ConfirmDialog(heading, message, confirm);
                dialog.ConfirmIsHarmless();
                return dialog.ShowDialog<bool>(this);
            };
            vm.PromptAsync = (heading, message, ok, initial) => new PromptDialog(heading, message, ok, initial).ShowDialog<string?>(this);
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(MainWindowViewModel.FourDecks) or nameof(MainWindowViewModel.CompactDecks))
                    ApplyDeckLayout();
            };
            vm.Settings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SettingsViewModel.TouchMode))
                {
                    ApplyTouchMode();
                    ApplyDeckLayout();   // the compact mixer's crossfader is finger-sized too
                }
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

    /// <summary>
    /// Two full-height decks, or four at half height in the same space. Four share it equally,
    /// so the upper and lower decks line up whatever the mixer beside them needs.
    /// Compact decks (#128): two decks at half height, or four without their waveforms, and the
    /// mixer beside them no taller: its faders shorten, PHONES MIX goes (it still applies), and the
    /// side keys move either side of the crossfader.
    /// </summary>
    private void ApplyDeckLayout()
    {
        bool four = Vm.FourDecks, compact = Vm.CompactDecks;
        foreach (DeckView view in DeckViews)
        {
            view.IsCompact = four || compact;
            view.IsMini = four && compact;
        }
        Deck3View.IsVisible = Deck4View.IsVisible = four;
        foreach (Grid side in new[] { LeftDecks, RightDecks })
        {
            side.RowDefinitions[1].Height = four ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
            side.RowSpacing = four ? DeckView.CompactGap : 0;
        }

        MixerPanel.MaxHeight = compact ? DeckView.CompactHeight : double.PositiveInfinity;
        if (compact)
        {
            // No taller than the decks need (four without waveforms, or touch mode's bigger keys,
            // need more than a compact deck's height): known once they've been measured.
            Dispatcher.UIThread.Post(() =>
            {
                if (Vm.CompactDecks)
                    MixerPanel.MaxHeight = Math.Max(LeftDecks.DesiredSize.Height, RightDecks.DesiredSize.Height);
            }, DispatcherPriority.Loaded);
        }
        MixerPanel.Padding = compact ? new Thickness(12, 8) : new Thickness(14, 12);
        PhonesMixRow.IsVisible = !compact;
        LiveLabel.IsVisible = !compact;
        Grid.SetRow(Crossfader, compact ? 1 : 0);
        Grid.SetColumn(Crossfader, compact ? 1 : 0);
        Grid.SetColumnSpan(Crossfader, compact ? 1 : 3);
        Crossfader.Margin = compact ? new Thickness(6, 0) : default;
        CrossfaderRows.RowSpacing = compact ? 0 : 6;
        CrossfaderRows.Classes.Set("compact", compact);
        if (compact)
            MixerPanel.Resources["CrossfaderHeight"] = Vm.Settings.TouchMode ? 40.0 : 30.0;
        else
            MixerPanel.Resources.Remove("CrossfaderHeight");
    }

    // ───────────── The grip under the decks ─────────────

    /// <summary>How far the grip must be dragged to switch; less, and letting go counts as a tap.</summary>
    private const double DeckGripSwitchDistance = 16;

    private double _deckGripFrom;
    private bool _deckGripMoved;

    private void OnDeckGripPressed(object? sender, PointerPressedEventArgs e)
    {
        _deckGripFrom = e.GetPosition(this).Y;
        _deckGripMoved = false;
        e.Pointer.Capture(DeckGrip);
        e.Handled = true;
    }

    /// <summary>Up past the switching distance: smaller decks; down: bigger. Once per drag direction.</summary>
    private void OnDeckGripMoved(object? sender, PointerEventArgs e)
    {
        if (e.Pointer.Captured != DeckGrip)
            return;
        double moved = e.GetPosition(this).Y - _deckGripFrom;
        if (Math.Abs(moved) > 4)
            _deckGripMoved = true;
        if (moved <= -DeckGripSwitchDistance && !Vm.CompactDecks || moved >= DeckGripSwitchDistance && Vm.CompactDecks)
        {
            Vm.CompactDecks = moved < 0;
            _deckGripFrom = e.GetPosition(this).Y;
        }
    }

    private void OnDeckGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Pointer.Captured != DeckGrip)
            return;
        e.Pointer.Capture(null);
        if (!_deckGripMoved)
            Vm.CompactDecks = !Vm.CompactDecks;   // a tap
        e.Handled = true;
    }

    private DeckViewModel? DeckAt(int index) => Vm.Decks.ElementAtOrDefault(index);

    // ───────────── Import from Mixxx ─────────────

    private async void ImportFromMixxx()
    {
        var result = await new MixxxImportDialog(Vm.CreateMixxxImport()).ShowDialog<GZJam.Library.MixxxImportResult?>(this);
        if (result is not null)
            Vm.ShowNotice(MixxxImportViewModel.Summary(result));
    }

    // ───────────── Import from other players ─────────────

    private async void ImportPlaylists()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import playlists (M3U)",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Playlists") { Patterns = PlaylistFile.Patterns }],
        });
        var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        await Vm.ImportPlaylistsAsync(paths);
    }

    private async void ImportFileRatings()
    {
        if (await new FileRatingsDialog(Vm.CreateFileRatingsImport()).ShowDialog<int?>(this) is int rated)
            Vm.ShowNotice(FileRatingsImportViewModel.Summary(rated));
    }

    // ───────────── Menus ─────────────

    /// <summary>Grays out "Load to Deck X" when protection would refuse it, and says why.</summary>
    private void OnTrackMenuOpening()
    {
        UpdateLoadMenuItems([TrackLoad1, TrackLoad2, TrackLoad3, TrackLoad4], SelectedTracks().Count == 1);
        var tracks = SelectedTracks();
        MarksMenu.FillRating(TrackRatingMenu, Vm, tracks);
        MarksMenu.FillEnergy(TrackEnergyMenu, Vm, tracks);
        LabelsMenu.Fill(TrackLabelsMenu, Vm, tracks);
        BpmMenu.Fill(TrackBpmMenu, Vm, tracks);
        FillPlaylistItems(tracks);
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

    /// <summary>Settings is open over the main screen.</summary>
    public bool IsSettingsOpen => SettingsOverlay.IsVisible;

    /// <summary>
    /// Opens Settings over the dimmed main screen, on <paramref name="section"/> or where it was
    /// last. The device list is read again, in case something was plugged in.
    /// </summary>
    public void OpenSettings(SettingsSection? section = null)
    {
        Vm.Settings.RefreshDevices();
        if (section is { } wanted)
            SettingsPanel.Section = wanted;
        SettingsOverlay.IsVisible = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(SettingsPanel.FocusDone, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    public void CloseSettings()
    {
        SettingsOverlay.IsVisible = false;
        SettingsButton.Focus();
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e) => OpenSettings();

    /// <summary>The new version's release page: what's new, and the downloads.</summary>
    private async void OnNewVersionClick(object? sender, RoutedEventArgs e)
    {
        if (Vm.NewVersion is { } release)
            await Launcher.LaunchUriAsync(new Uri(release.Url));
    }

    private void OnNewVersionDismissClick(object? sender, RoutedEventArgs e) => Vm.DismissNewVersion();

    /// <summary>MAIN and CUE at the top open Settings on the outputs.</summary>
    private void OnOutputChipClick(object? sender, RoutedEventArgs e) => OpenSettings(SettingsSection.Sound);

    private void OnSettingsDimmerPressed(object? sender, PointerPressedEventArgs e)
    {
        CloseSettings();
        e.Handled = true;
    }
}
