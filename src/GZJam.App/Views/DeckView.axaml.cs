using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using GZJam.App.ViewModels;
using GZJam.Audio;

namespace GZJam.App.Views;

public partial class DeckView : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<DeckView, string>(nameof(Label), "1");

    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<DeckView, IBrush?>(nameof(Accent));

    /// <summary>Half height, for four decks: the track on one line, a shorter waveform, smaller buttons.</summary>
    public static readonly StyledProperty<bool> IsCompactProperty =
        AvaloniaProperty.Register<DeckView, bool>(nameof(IsCompact));

    /// <summary>Touch mode: the compact deck's keys are finger-sized too (the full deck's come from the app's resources).</summary>
    public static readonly StyledProperty<bool> IsTouchProperty =
        AvaloniaProperty.Register<DeckView, bool>(nameof(IsTouch));

    /// <summary>A full deck's height; two compact decks and the gap between them fit in it.</summary>
    public const double FullHeight = 268;
    public const double CompactGap = 8;
    public const double CompactHeight = (FullHeight - CompactGap) / 2;

    static DeckView()
    {
        IsCompactProperty.Changed.AddClassHandler<DeckView>((view, _) => view.ApplyCompact());
        IsTouchProperty.Changed.AddClassHandler<DeckView>((view, _) => view.ApplyCompact());
    }

    /// <summary>How long the play button must be held to pause an on-air deck (when two-tap pausing is on).</summary>
    /// <remarks>
    /// Timed by the clock, so a test machine that stalls between a click's press and release turns
    /// the click into a hold: tests of clicks make it long.
    /// </remarks>
    internal TimeSpan HoldToPause { get; set; } = TimeSpan.FromMilliseconds(600);

    private CancellationTokenSource? _hold;
    private bool _holdCompleted;

    /// <summary>The press being timed, by the input's own times (see <see cref="PlayHold"/>).</summary>
    private PlayHold? _gesture;

    public DeckView()
    {
        InitializeComponent();
        Seek.SeekRequested += (_, fraction) => Vm?.SeekTo(fraction);
        Seek.LockedPressed += (_, _) => Vm?.ExplainSeekLock();
        CloseUp.SeekRequested += (_, fraction) => Vm?.SeekTo(fraction);
        CloseUp.LockedPressed += (_, _) => Vm?.ExplainSeekLock();
        Seek.MarkerMoved += (_, move) => Vm?.MoveMarker(move.Kind, move.Index, move.To);
        CloseUp.MarkerMoved += (_, move) => Vm?.MoveMarker(move.Kind, move.Index, move.To);
        // Four decks: the mouse wheel or a pinch on the whole song zooms in; out past the widest, back.
        Seek.AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            if (IsCompact && e.Delta.Y > 0)
            {
                ZoomIn();
                e.Handled = true;
            }
        });
        Seek.GestureRecognizers.Add(new Avalonia.Input.PinchGestureRecognizer());
        Seek.AddHandler(PinchEvent, (_, e) =>
        {
            if (IsCompact && e.Scale > 1.15)
                ZoomIn();
        });
        CloseUp.ZoomedOutPastWidest += (_, _) =>
        {
            if (IsCompact)
                SetZoomed(false);
        };
        PlayButton.AddHandler(PointerPressedEvent, OnPlayPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        PlayButton.AddHandler(PointerReleasedEvent, OnPlayReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        PlayButton.AddHandler(PointerCaptureLostEvent, (_, _) => CancelHold(), RoutingStrategies.Tunnel, handledEventsToo: true);
        PlayButton.Click += OnPlayClick;
        foreach (ContextMenu menu in new[] { BpmContextMenu, ReadoutBpmMenu })
        {
            // Opening covers right-click and long-press; Opened also covers menus opened from code.
            menu.Opening += (_, e) => FillBpmMenu(menu, e);
            menu.Opened += (_, _) => FillBpmMenu(menu, null);
        }
        // Remember where on the waveform the menu was asked for, so "here" means that spot.
        Seek.AddHandler(ContextRequestedEvent, (_, e) =>
            _trimMenuAt = e.TryGetPosition(Seek, out Point point) ? Seek.FractionAt(point) : null,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        CloseUp.AddHandler(ContextRequestedEvent, (_, e) =>
            _trimMenuAt = e.TryGetPosition(CloseUp, out Point point) ? CloseUp.FractionAt(point) : null,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        foreach (ContextMenu menu in new[] { TrimMenu, CloseUpMenu })
        {
            menu.Opening += (_, e) => FillTrimMenu(menu, e);
            menu.Opened += (_, _) => FillTrimMenu(menu, null);
        }
        FlyoutMenus.FillBeforeOpening(MoreButton, MoreMenu, FillMoreMenu);
        // Tap tempo: the pad takes the keyboard (Space taps it), and Escape puts it away.
        TapPanel.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && TapPanel.IsVisible)
                Dispatcher.UIThread.Post(() => TapPad.Focus());
        };
        TapPanel.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Vm?.CancelTapCommand.Execute(null);
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        ApplyCompact();
    }

    public bool IsCompact
    {
        get => GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    public bool IsTouch
    {
        get => GetValue(IsTouchProperty);
        set => SetValue(IsTouchProperty, value);
    }

    // ───────────── Close-up ─────────────

    /// <summary>Four decks: this deck shows its close-up instead of the whole song (zoomed in on demand).</summary>
    public bool IsZoomed { get; private set; }

    private void ZoomIn() => SetZoomed(true);

    private void SetZoomed(bool zoomed)
    {
        IsZoomed = zoomed && IsCompact;
        ApplyWaveformLayout();
    }

    /// <summary>
    /// Two decks: the close-up over the whole song (2 : 1). Four: the whole song, or (zoomed) the
    /// close-up in its place. The rows are set here, so the deck's height never changes.
    /// </summary>
    private void ApplyWaveformLayout()
    {
        bool closeUp = !IsCompact || IsZoomed, whole = !IsCompact || !IsZoomed;
        CloseUp.IsVisible = closeUp;
        Seek.IsVisible = whole;
        Seek.ShowCloseUp = !IsCompact;
        WaveRows.RowDefinitions[0].Height = closeUp ? new GridLength(whole ? 2 : 1, GridUnitType.Star) : new GridLength(0);
        WaveRows.RowDefinitions[1].Height = new GridLength(closeUp && whole ? 4 : 0);
        WaveRows.RowDefinitions[2].Height = whole ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
    }

    private void ApplyCompact()
    {
        bool compact = IsCompact;
        Classes.Set("compact", compact);
        if (!compact)
            IsZoomed = false;
        ApplyWaveformLayout();
        MinHeight = compact ? CompactHeight : FullHeight;
        // Smaller transport buttons; the full sizes come from the app's resources.
        (string, double)[] sizes = IsTouch
            ? [("DeckButtonSize", 40.0), ("DeckControlHeight", 34.0), ("DeckPlayIconSize", 14.0), ("DeckPauseIconSize", 13.0)]
            : [("DeckButtonSize", 34.0), ("DeckControlHeight", 28.0), ("DeckPlayIconSize", 12.0), ("DeckPauseIconSize", 11.0)];
        foreach (var (key, size) in sizes)
        {
            if (compact)
                Resources[key] = size;
            else
                Resources.Remove(key);
        }
    }

    internal MenuFlyout MoreMenu => (MenuFlyout)MoreButton.Flyout!;

    /// <summary>The ⋯ menu on a compact deck: what the full deck has room to show.</summary>
    private void FillMoreMenu()
    {
        var items = MoreMenu.Items;
        items.Clear();
        if (Vm is not { Track: { } track } vm)
        {
            items.Add(new MenuItem { Header = "No track loaded", IsEnabled = false });
            return;
        }
        var rating = new MenuItem { Header = "Rating" };
        MarksMenu.FillRating(rating, vm.Owner, [track]);
        var energy = new MenuItem { Header = "Energy" };
        MarksMenu.FillEnergy(energy, vm.Owner, [track]);
        var bpm = new MenuItem { Header = BpmMenu.Title };
        BpmMenu.Fill(bpm, vm.Owner, [track], deck: vm);
        var eject = new MenuItem { Header = "Eject", IsEnabled = !vm.IsPlaying, Command = vm.EjectCommand };
        // The cue keys don't fit over a four-deck waveform: they're here instead.
        foreach (GZJam.Library.SongCue cue in vm.Cues)
            items.Add(new MenuItem { Header = $"Go to cue {cue.Number} ({Time(cue.At)})", Command = vm.GoToCueCommand, CommandParameter = cue.Number });
        if (vm.HasCues)
            items.Add(new Separator());
        items.Add(rating);
        items.Add(energy);
        items.Add(bpm);
        items.Add(new Separator());
        items.Add(eject);
    }

    private double? _trimMenuAt;

    /// <summary>
    /// The waveform's menu: start or end the song where it was clicked, or at the playhead (after
    /// listening for the spot in the headphones), or go back to the automatic trim.
    /// </summary>
    private void FillTrimMenu(ContextMenu menu, System.ComponentModel.CancelEventArgs? opening)
    {
        if (Vm is not { HasTrack: true } vm)
        {
            if (opening is not null)
                opening.Cancel = true;
            return;
        }
        Deck deck = vm.Deck;
        TimeSpan playhead = deck.Position;
        TimeSpan? here = _trimMenuAt is { } fraction ? deck.Length * fraction : null;
        var items = menu.Items;
        items.Clear();
        items.Add(new MenuItem { Header = "Song starts", IsEnabled = false });
        if (here is { } h)
            items.Add(Item($"Here ({Time(h)})", () => vm.SetStart(h)));
        items.Add(Item($"At the playhead ({Time(playhead)})", () => vm.SetStart(playhead)));
        items.Add(Item($"Automatic ({Time(vm.AutomaticStart)})", () => vm.SetStart(null), radio: true, isChecked: !vm.IsStartManual));
        items.Add(new Separator());
        items.Add(new MenuItem { Header = "Song ends", IsEnabled = false });
        if (here is { } h2)
            items.Add(Item($"Here ({Time(h2)})", () => vm.SetEnd(h2)));
        items.Add(Item($"At the playhead ({Time(playhead)})", () => vm.SetEnd(playhead)));
        items.Add(Item($"Automatic ({Time(vm.AutomaticEnd)})", () => vm.SetEnd(null), radio: true, isChecked: !vm.IsEndManual));

        // Jumps: leave at the end of a phrase, land at the start of another.
        items.Add(new Separator());
        items.Add(new MenuItem { Header = "Jump", IsEnabled = false });
        if (vm.IsSettingJump)
        {
            if (here is { } h3)
                items.Add(Item($"Land the jump here ({Time(h3)})", () => vm.LandJump(h3)));
            items.Add(Item($"Land the jump at the playhead ({Time(playhead)})", () => vm.LandJump(playhead)));
            items.Add(Item("Cancel this jump", () => { vm.RemoveJump(-1); return true; }));
        }
        else if (vm.CanAddJump)
        {
            if (here is { } h4)
                items.Add(Item($"Jump from here ({Time(h4)})", () => vm.StartJump(h4)));
            items.Add(Item($"Jump from the playhead ({Time(playhead)})", () => vm.StartJump(playhead)));
        }
        for (int i = 0; i < vm.SongJumps.Count; i++)
        {
            int index = i;
            SongJumpText(vm.SongJumps[i], out string text);
            items.Add(Item($"Remove jump {i + 1} ({text})", () => { vm.RemoveJump(index); return true; }));
        }

        // Cues: points to go to at a tap (the keys over the waveform), numbered 1 to 4.
        items.Add(new Separator());
        items.Add(new MenuItem { Header = "Cue", IsEnabled = false });
        if (vm.CanAddCue)
        {
            int number = vm.NextCueNumber;
            if (here is { } h5)
                items.Add(Item($"Cue {number} here ({Time(h5)})", () => vm.AddCue(h5)));
            items.Add(Item($"Cue {number} at the playhead ({Time(playhead)})", () => vm.AddCue(playhead)));
        }
        foreach (GZJam.Library.SongCue cue in vm.Cues)
        {
            int number = cue.Number;
            items.Add(Item($"Remove cue {number} ({Time(cue.At)})", () => { vm.RemoveCue(number); return true; }));
        }

        static void SongJumpText(GZJam.Library.SongJump jump, out string text) => text = $"{Time(jump.From)} → {Time(jump.To)}";

        static MenuItem Item(string header, Func<bool> action, bool radio = false, bool isChecked = false)
        {
            var item = new MenuItem
            {
                Header = header,
                ToggleType = radio ? MenuItemToggleType.CheckBox : MenuItemToggleType.None,
                IsChecked = isChecked,
            };
            item.Click += (_, _) => action();
            return item;
        }
    }

    /// <summary>"1:04.2": tenths, since start and end points are about precision.</summary>
    private static string Time(TimeSpan t) =>
        string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{(int)t.TotalMinutes}:{t.Seconds:00}.{t.Milliseconds / 100}");

    private void FillBpmMenu(ContextMenu menu, System.ComponentModel.CancelEventArgs? opening)
    {
        if (Vm is not { Track: { } track } vm)
        {
            if (opening is not null)
                opening.Cancel = true;   // nothing loaded: no menu
            return;
        }
        BpmMenu.Fill(menu, vm.Owner, [track], withTitle: true, deck: vm);
    }

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public IBrush? Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    private DeckViewModel? Vm => DataContext as DeckViewModel;

    /// <summary>Outline shown while a track is dragged over the deck: accepted, refused, or none.</summary>
    public void SetDropState(bool? accepted)
    {
        Frame.Classes.Set("drop-ok", accepted == true);
        Frame.Classes.Set("drop-blocked", accepted == false);
    }

    // Play/pause: a tap plays or pauses. If Settings asks for two taps on a deck the room is
    // hearing, a press-and-hold also pauses it (one deliberate gesture instead of two).

    private void OnPlayPressed(object? sender, PointerPressedEventArgs e)
    {
        _holdCompleted = false;
        if (Vm is not { PauseNeedsConfirm: true })
            return;
        PlayButton.Classes.Add("holding");
        _gesture = new PlayHold(HoldToPause);
        _gesture.Press(e.Timestamp);
        var hold = _hold = new CancellationTokenSource();
        // Below input: after a freeze, a release already waiting is handled first, and cancels this.
        Task.Delay(HoldToPause, hold.Token).ContinueWith(_ => Dispatcher.UIThread.Post(() =>
        {
            if (!hold.IsCancellationRequested)
                CompleteHold();
        }, DispatcherPriority.Background), CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    private void OnPlayReleased(object? sender, PointerReleasedEventArgs e)
    {
        CancelHold();
        if (_gesture is not { } gesture)
            return;
        _gesture = null;
        if (gesture.Released(e.Timestamp) == PlayHold.Release.TapAfterFreeze)
        {
            // The release's own time says it was a quick tap: the screen froze, and the timer
            // paused first. Play on; the click then does what a tap does (asks for a second).
            _holdCompleted = false;
            Vm?.UndoHoldPause();
        }
    }

    private void CompleteHold()
    {
        CancelHold();
        _holdCompleted = true;
        if (Vm?.HoldPause() == true)
            _gesture?.Paused();
    }

    private void CancelHold()
    {
        _hold?.Cancel();
        _hold = null;
        PlayButton.Classes.Remove("holding");
    }

    private void OnRatingClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { Track: { } track } vm)
            vm.Owner.CycleRating(track);
    }

    private void OnEnergyClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { Track: { } track } vm)
            vm.Owner.CycleEnergy(track);
    }

    private void OnPlayClick(object? sender, RoutedEventArgs e)
    {
        if (_holdCompleted)
        {
            _holdCompleted = false;   // the hold already paused; this click is its release
            return;
        }
        Vm?.TogglePlayCommand.Execute(null);
    }

    /// <summary>For keyboard and tests: the same as completing a press-and-hold.</summary>
    internal void SimulateHold() => CompleteHold();
}
