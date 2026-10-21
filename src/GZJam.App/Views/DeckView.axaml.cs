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

    /// <summary>How long the play button must be held to pause an on-air deck (when two-tap pausing is on).</summary>
    public static readonly TimeSpan HoldToPause = TimeSpan.FromMilliseconds(600);

    private CancellationTokenSource? _hold;
    private bool _holdCompleted;

    public DeckView()
    {
        InitializeComponent();
        Seek.SeekRequested += (_, fraction) => Vm?.SeekTo(fraction);
        Seek.LockedPressed += (_, _) => Vm?.ExplainSeekLock();
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
        TrimMenu.Opening += (_, e) => FillTrimMenu(e);
        TrimMenu.Opened += (_, _) => FillTrimMenu(null);
    }

    private double? _trimMenuAt;

    /// <summary>
    /// The waveform's menu: start or end the song where it was clicked, or at the playhead (after
    /// listening for the spot in the headphones), or go back to the automatic trim.
    /// </summary>
    private void FillTrimMenu(System.ComponentModel.CancelEventArgs? opening)
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
        var items = TrimMenu.Items;
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
        BpmMenu.Fill(menu, vm.Owner, [track], withTitle: true);
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
        var hold = _hold = new CancellationTokenSource();
        Task.Delay(HoldToPause, hold.Token).ContinueWith(_ => Dispatcher.UIThread.Post(() =>
        {
            if (!hold.IsCancellationRequested)
                CompleteHold();
        }), CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    private void OnPlayReleased(object? sender, PointerReleasedEventArgs e) => CancelHold();

    private void CompleteHold()
    {
        CancelHold();
        _holdCompleted = true;
        Vm?.HoldPause();
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
