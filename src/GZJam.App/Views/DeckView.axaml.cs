using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using GZJam.App.ViewModels;

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
