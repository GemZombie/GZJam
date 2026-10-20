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
    public static readonly StyledProperty<string> LetterProperty =
        AvaloniaProperty.Register<DeckView, string>(nameof(Letter), "A");

    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<DeckView, IBrush?>(nameof(Accent));

    /// <summary>How long the live deck's play button must be held to pause it.</summary>
    public static readonly TimeSpan HoldToPause = TimeSpan.FromMilliseconds(600);

    private readonly DispatcherTimer _holdTimer;
    private bool _holdCompleted;

    public DeckView()
    {
        InitializeComponent();
        _holdTimer = new DispatcherTimer { Interval = HoldToPause };
        _holdTimer.Tick += (_, _) => CompleteHold();

        Seek.SeekRequested += (_, fraction) => Vm?.SeekTo(fraction);
        PlayButton.AddHandler(PointerPressedEvent, OnPlayPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        PlayButton.AddHandler(PointerReleasedEvent, OnPlayReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        PlayButton.AddHandler(PointerCaptureLostEvent, (_, _) => CancelHold(), RoutingStrategies.Tunnel, handledEventsToo: true);
        PlayButton.Click += OnPlayClick;
    }

    public string Letter
    {
        get => GetValue(LetterProperty);
        set => SetValue(LetterProperty, value);
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

    // Play/pause: a tap plays or pauses, but the deck the room is dancing to needs a
    // press-and-hold to pause (unless set to tap in Settings), so a stray touch can't stop the music.

    private void OnPlayPressed(object? sender, PointerPressedEventArgs e)
    {
        _holdCompleted = false;
        if (Vm is not { PauseNeedsHold: true })
            return;
        PlayButton.Classes.Add("holding");
        _holdTimer.Start();
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
        _holdTimer.Stop();
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
