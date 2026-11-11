using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering.Composition;
using GZJam.Audio;

namespace GZJam.App.Controls;

/// <summary>
/// A close-up of the song around the playhead: a few seconds (<see cref="WindowSeconds"/>), the
/// playhead fixed in the middle and the music scrolling past it, for seeing the end of a phrase
/// coming and setting points precisely. Drawn like <see cref="SeekBar"/> (bars colored by
/// frequency, what's played dimmed, trimmed silence shaded, jumps and cues marked), from a close-up
/// waveform (10 ms a slice) or, until that's ready, the overview.
/// <para>
/// While the deck plays, it's redrawn at every frame the screen shows, the playhead worked out
/// from the clock and the tempo and eased toward where the deck says it is (<see cref="CloseUpClock"/>),
/// so the music glides past instead of stuttering. It's drawn on the render thread
/// (<see cref="CloseUpHandler"/>, <see cref="CloseUpPicture"/>, #364), so a busy moment on the
/// window's thread never shows as the waveform stuttering; the window's thread only says what
/// changed. Only while it's in view and the window isn't minimized. Each bar covers a fixed
/// stretch of the song, so bars keep their shape as they move; zoomed in so far that a bar is
/// shorter than a slice, bars blend the slices either side.
/// </para>
/// <para>
/// Drag it to move through the song like a record (a mouse click goes to that spot); locked, a
/// press says why. The mouse wheel or a pinch shows more or less of the song. A press on a marker
/// (the start or end, a jump, a cue) grabs it instead, to drag it, on air too.
/// </para>
/// </summary>
public sealed class CloseUpWaveform : Control
{
    public static readonly StyledProperty<Waveform?> WaveformProperty =
        AvaloniaProperty.Register<CloseUpWaveform, Waveform?>(nameof(Waveform));

    /// <summary>The song's length in seconds (the waveform covers it all).</summary>
    public static readonly StyledProperty<double> LengthProperty =
        AvaloniaProperty.Register<CloseUpWaveform, double>(nameof(Length));

    /// <summary>The playhead, 0..1 through the song.</summary>
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<CloseUpWaveform, double>(nameof(Progress));

    /// <summary>How much of the song shows across the close-up, in seconds.</summary>
    public static readonly StyledProperty<double> WindowSecondsProperty =
        AvaloniaProperty.Register<CloseUpWaveform, double>(nameof(WindowSeconds), 8, defaultBindingMode: BindingMode.TwoWay,
            coerce: (_, v) => double.IsNaN(v) ? 8 : Math.Clamp(v, MinWindowSeconds, MaxWindowSeconds));

    public const double MinWindowSeconds = 2, MaxWindowSeconds = 30;

    public static readonly StyledProperty<double> TrimStartProperty =
        AvaloniaProperty.Register<CloseUpWaveform, double>(nameof(TrimStart));

    public static readonly StyledProperty<double> TrimEndProperty =
        AvaloniaProperty.Register<CloseUpWaveform, double>(nameof(TrimEnd), 1);

    public static readonly StyledProperty<bool> IsStartManualProperty =
        AvaloniaProperty.Register<CloseUpWaveform, bool>(nameof(IsStartManual));

    public static readonly StyledProperty<bool> IsEndManualProperty =
        AvaloniaProperty.Register<CloseUpWaveform, bool>(nameof(IsEndManual));

    /// <summary>The song fades out from its end over this much of the file (#267); 0: it stops there.</summary>
    public static readonly StyledProperty<double> EndFadeProperty =
        AvaloniaProperty.Register<CloseUpWaveform, double>(nameof(EndFade));

    public static readonly StyledProperty<IReadOnlyList<JumpMarker>?> JumpsProperty =
        AvaloniaProperty.Register<CloseUpWaveform, IReadOnlyList<JumpMarker>?>(nameof(Jumps));

    public static readonly StyledProperty<double?> PendingJumpFromProperty =
        AvaloniaProperty.Register<CloseUpWaveform, double?>(nameof(PendingJumpFrom));

    public static readonly StyledProperty<IReadOnlyList<CueMarker>?> CuesProperty =
        AvaloniaProperty.Register<CloseUpWaveform, IReadOnlyList<CueMarker>?>(nameof(Cues));

    /// <summary>Seeking refused (the room is hearing this deck): a press raises <see cref="LockedPressed"/>.</summary>
    public static readonly StyledProperty<bool> IsLockedProperty =
        AvaloniaProperty.Register<CloseUpWaveform, bool>(nameof(IsLocked));

    /// <summary>The deck's color, for the playhead while locked (as on the whole-song view).</summary>
    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<CloseUpWaveform, IBrush?>(nameof(Fill), Brushes.White);

    /// <summary>The deck is playing: the close-up moves on by itself between position reports.</summary>
    public static readonly StyledProperty<bool> IsPlayingProperty =
        AvaloniaProperty.Register<CloseUpWaveform, bool>(nameof(IsPlaying));

    /// <summary>The deck's tempo change in percent: how fast the song goes by.</summary>
    public static readonly StyledProperty<double> TempoPercentProperty =
        AvaloniaProperty.Register<CloseUpWaveform, double>(nameof(TempoPercent));

    public CloseUpWaveform()
    {
        GestureRecognizers.Add(new PinchGestureRecognizer());
        AddHandler(PinchEvent, (_, e) =>
        {
            _pinchFrom ??= WindowSeconds;
            if (e.Scale > 0)
            {
                if (e.Scale < 0.85 && _pinchFrom >= MaxWindowSeconds)
                    ZoomedOutPastWidest?.Invoke(this, EventArgs.Empty);
                else
                    WindowSeconds = _pinchFrom.Value / e.Scale;   // fingers apart: closer in
            }
            e.Handled = true;
        });
        AddHandler(PinchEndedEvent, (_, _) => _pinchFrom = null);
    }

    public Waveform? Waveform { get => GetValue(WaveformProperty); set => SetValue(WaveformProperty, value); }
    public double Length { get => GetValue(LengthProperty); set => SetValue(LengthProperty, value); }
    public double Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public double WindowSeconds { get => GetValue(WindowSecondsProperty); set => SetValue(WindowSecondsProperty, value); }
    public double TrimStart { get => GetValue(TrimStartProperty); set => SetValue(TrimStartProperty, value); }
    public double TrimEnd { get => GetValue(TrimEndProperty); set => SetValue(TrimEndProperty, value); }
    public bool IsStartManual { get => GetValue(IsStartManualProperty); set => SetValue(IsStartManualProperty, value); }
    public bool IsEndManual { get => GetValue(IsEndManualProperty); set => SetValue(IsEndManualProperty, value); }
    public double EndFade { get => GetValue(EndFadeProperty); set => SetValue(EndFadeProperty, value); }
    public IReadOnlyList<JumpMarker>? Jumps { get => GetValue(JumpsProperty); set => SetValue(JumpsProperty, value); }
    public double? PendingJumpFrom { get => GetValue(PendingJumpFromProperty); set => SetValue(PendingJumpFromProperty, value); }
    public IReadOnlyList<CueMarker>? Cues { get => GetValue(CuesProperty); set => SetValue(CuesProperty, value); }
    public bool IsLocked { get => GetValue(IsLockedProperty); set => SetValue(IsLockedProperty, value); }
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public bool IsPlaying { get => GetValue(IsPlayingProperty); set => SetValue(IsPlayingProperty, value); }
    public double TempoPercent { get => GetValue(TempoPercentProperty); set => SetValue(TempoPercentProperty, value); }

    // ───────────── The playhead, between reports ─────────────

    private readonly CloseUpClock _clock = new();

    /// <summary>Where the playhead is drawn, in seconds into the song.</summary>
    internal double Playhead => _clock.Playhead;

    /// <summary>The properties the picture shows, apart from where the playhead is.</summary>
    private static readonly HashSet<AvaloniaProperty> SceneProperties =
    [
        WaveformProperty, LengthProperty, WindowSecondsProperty, TrimStartProperty, TrimEndProperty, IsStartManualProperty,
        IsEndManualProperty, EndFadeProperty, JumpsProperty, PendingJumpFromProperty, CuesProperty, IsLockedProperty, FillProperty,
        IsEnabledProperty,
    ];

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ProgressProperty || change.Property == LengthProperty)
            _clock.Report(Progress, Length);
        else if (change.Property == TempoPercentProperty)
            _clock.SetTempo(TempoPercent);
        else if (change.Property == IsPlayingProperty)
            _clock.SetPlaying(IsPlaying);
        else if (change.Property == IsVisibleProperty)
            _clock.Restart();
        if (SceneProperties.Contains(change.Property))
            SceneChanged();
        else if (change.Property == ProgressProperty || change.Property == IsPlayingProperty || change.Property == IsVisibleProperty
                 || change.Property == TempoPercentProperty)
            Moved();
    }

    // ───────────── Drawn on the render thread (#364) ─────────────

    private CompositionCustomVisual? _visual;
    private CloseUpScene? _scene;
    private bool _animating;

    /// <summary>The render thread is drawing it at every frame (tests).</summary>
    internal bool IsMoving => _animating;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (ElementComposition.GetElementVisual(this)?.Compositor is not { } compositor)
            return;
        _visual = compositor.CreateCustomVisual(new CloseUpHandler(_clock));
        ElementComposition.SetElementChildVisual(this, _visual);
        _visual.Size = new Vector(Bounds.Width, Bounds.Height);
        _animating = false;
        SceneChanged();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ElementComposition.SetElementChildVisual(this, null);
        _visual = null;
        _animating = false;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_visual is not null)
            _visual.Size = new Vector(e.NewSize.Width, e.NewSize.Height);
    }

    /// <summary>What the picture shows, made now from the properties (and a marker being dragged).</summary>
    private CloseUpScene Scene()
    {
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var cues = (_grab.Cues(Cues) ?? []).Select(c => (c.At, CloseUpPicture.CueFlag(c.Number, scaling))).ToList();
        return new CloseUpScene(Waveform, Length, WindowSeconds, _grab.TrimStart(TrimStart), _grab.TrimEnd(TrimEnd),
            _grab.StartManual(IsStartManual), _grab.EndManual(IsEndManual), EndFade, _grab.Jumps(Jumps) ?? [], PendingJumpFrom, cues,
            CanSeek, Fill?.ToImmutable() ?? (IImmutableBrush)Brushes.White);
    }

    /// <summary>Something the picture shows changed: the render thread gets it, or (on its own, tests) it's drawn here.</summary>
    private void SceneChanged()
    {
        _scene = null;
        if (_visual is null)
        {
            InvalidateVisual();
            return;
        }
        _visual.SendHandlerMessage(Scene());
        Moved();
    }

    /// <summary>
    /// The playhead moved, or playing started or stopped: drawn again, at every frame while the deck
    /// plays and the close-up is in view. Not while it's hidden (the decks, or a deck zoomed out of
    /// it) or the window is minimized: then the render thread does nothing for it.
    /// </summary>
    private void Moved()
    {
        if (_visual is null)
        {
            InvalidateVisual();
            return;
        }
        bool animate = IsPlaying && IsEffectivelyVisible && TopLevel.GetTopLevel(this) is not Window { WindowState: WindowState.Minimized };
        if (animate != _animating)
        {
            _animating = animate;
            _visual.SendHandlerMessage(animate ? CloseUpHandler.Play : CloseUpHandler.Stop);
        }
        else if (!animate)
            _visual.SendHandlerMessage(CloseUpHandler.Redraw);   // paused: a seek, a nudge
    }

    /// <summary>While a marker is held and the music moves on, it stays under the finger: moved at every frame.</summary>
    private void FollowHeldMarker()
    {
        if (!IsPlaying || !_grab.IsHolding || TopLevel.GetTopLevel(this) is not { } top)
            return;
        top.RequestAnimationFrame(_ =>
        {
            if (!IsPlaying || !_grab.IsHolding)
                return;
            if (_grab.IsDragging && _grab.Drag(_dragX, FractionAt(new Point(_dragX, 0))))
                SceneChanged();
            FollowHeldMarker();
        });
    }

    /// <summary>Asks to move the playhead to a fraction (0..1) of the song.</summary>
    public event EventHandler<double>? SeekRequested;

    /// <summary>Pressed while <see cref="IsLocked"/>.</summary>
    public event EventHandler? LockedPressed;

    /// <summary>The wheel or a pinch asked for more of the song while it already shows the most it can.</summary>
    public event EventHandler? ZoomedOutPastWidest;

    /// <summary>A marker was dragged and let go.</summary>
    public event EventHandler<MarkerMove>? MarkerMoved;

    /// <summary>Shows more (<paramref name="factor"/> above 1) or less of the song.</summary>
    private void Zoom(double factor)
    {
        if (factor > 1 && WindowSeconds >= MaxWindowSeconds)
            ZoomedOutPastWidest?.Invoke(this, EventArgs.Empty);
        else
            WindowSeconds *= factor;
    }

    private bool CanSeek => IsEnabled && !IsLocked;

    /// <summary>Seconds per pixel at the current width and window.</summary>
    private double SecondsPerPixel => Bounds.Width > 0 ? WindowSeconds / Bounds.Width : 0;

    /// <summary>Where the close-up starts, in seconds (the playhead is in the middle).</summary>
    private double WindowStart => Playhead - WindowSeconds / 2;

    /// <summary>The position (0..1 through the song) at a point on the close-up.</summary>
    public double FractionAt(Point point) =>
        Length > 0 ? Math.Clamp((WindowStart + point.X * SecondsPerPixel) / Length, 0, 1) : 0;

    /// <summary>Where a fraction of the song is on the close-up, in pixels (may be off either side).</summary>
    private double X(double fraction) => (fraction * Length - WindowStart) / SecondsPerPixel;

    /// <summary>The marker a press at <paramref name="x"/> would grab, if any.</summary>
    private (MarkerKind Kind, int Index, double At)? MarkerAt(double x, double reach) =>
        Length > 0 && SecondsPerPixel > 0
            ? MarkerGrab.Find(MarkerGrab.Draggable(TrimStart, TrimEnd, TrimStart > 0 || IsStartManual, TrimEnd < 1 || IsEndManual, Jumps, Cues),
                fraction => X(Math.Clamp(fraction, 0, 1)), x, reach)
            : null;

    // ───────────── Drawing ─────────────

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (_visual is not null)
        {
            // Drawn on the render thread; this only lets presses land on it.
            context.DrawRectangle(Brushes.Transparent, null, bounds);
            return;
        }
        // On its own (a picture of it, in tests): drawn here, the same way.
        CloseUpPicture.Draw(new ControlCanvas(context), _scene ??= Scene(), Playhead, bounds.Size);
    }

    /// <summary>
    /// A bar's levels, for the stretch of the song from slice <paramref name="from"/> to
    /// <paramref name="to"/> (fractions of slices): the loudest slice in it; or, for a bar shorter
    /// than a slice (zoomed in tight), a blend of the two slices either side of its middle, so
    /// neighboring bars don't repeat one slice and the shape doesn't move in steps. Null: off the song.
    /// </summary>
    internal static (byte Low, byte Mid, byte High, byte Loud)? BarLevels(Waveform waveform, double from, double to)
    {
        if (to <= 0 || from >= waveform.Count)
            return null;
        if (to - from < 1)
        {
            double center = Math.Clamp((from + to) / 2 - 0.5, 0, waveform.Count - 1);   // between slice middles
            int i = (int)Math.Floor(center), j = Math.Min(i + 1, waveform.Count - 1);
            double t = center - i;
            static byte Blend(byte[] band, int i, int j, double t) => (byte)Math.Round(band[i] + (band[j] - band[i]) * t);
            return (Blend(waveform.Low, i, j, t), Blend(waveform.Mid, i, j, t), Blend(waveform.High, i, j, t), Blend(waveform.Loudness, i, j, t));
        }
        int a = (int)Math.Floor(from), b = Math.Max(a + 1, (int)Math.Floor(to));
        byte low = 0, mid = 0, high = 0, loud = 0;
        for (int s = Math.Max(0, a); s < b && s < waveform.Count; s++)
        {
            low = Math.Max(low, waveform.Low[s]);
            mid = Math.Max(mid, waveform.Mid[s]);
            high = Math.Max(high, waveform.High[s]);
            loud = Math.Max(loud, waveform.Loudness[s]);
        }
        return (low, mid, high, loud);
    }

    // ───────────── Input ─────────────

    private double? _pinchFrom;
    private Point _pressedAt;
    private double _progressAtPress;
    private bool _dragged;
    private readonly MarkerGrab _grab = new();
    private double _dragX;   // where a held marker's pointer is

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        Zoom(Math.Pow(1.25, -e.Delta.Y));   // away from you: closer in
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        Point point = e.GetPosition(this);
        if (MarkerAt(point.X, MarkerGrab.Reach(e.Pointer)) is { } marker)
        {
            // On a marker: hold it, to drag it (on air too), rather than the song.
            e.Pointer.Capture(this);
            _grab.Hold(marker, point.X);
            (_pressedAt, _dragX) = (point, point.X);
            FollowHeldMarker();
            e.Handled = true;
            return;
        }
        if (IsLocked)
        {
            LockedPressed?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }
        e.Pointer.Capture(this);
        (_pressedAt, _progressAtPress, _dragged) = (point, Progress, false);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point point = e.GetPosition(this);
        if (e.Pointer.Captured != this)
        {
            if (e.Pointer.Type == PointerType.Mouse)
                Cursor = IsEnabled && MarkerAt(point.X, MarkerGrab.Reach(e.Pointer)) is not null ? MarkerGrab.DragCursor : null;
            return;
        }
        if (_grab.IsHolding)
        {
            _dragX = point.X;
            if (_grab.Drag(point.X, FractionAt(point)))
                SceneChanged();
            return;
        }
        if (!CanSeek || Length <= 0)
            return;
        double dx = point.X - _pressedAt.X;
        if (!_dragged && Math.Abs(dx) < 4)
            return;
        _dragged = true;
        // Like a record: the song moves with the pointer, so the playhead goes the other way.
        SeekRequested?.Invoke(this, Math.Clamp(_progressAtPress - dx * SecondsPerPixel / Length, 0, 1));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Pointer.Captured != this)
            return;
        bool click = e.Pointer.Type == PointerType.Mouse && e.InitialPressMouseButton == MouseButton.Left;
        if (_grab.IsHolding)
        {
            Point point = e.GetPosition(this);
            _grab.Drag(point.X, FractionAt(point));
            MarkerMove? move = _grab.Release();
            e.Pointer.Capture(null);
            SceneChanged();
            if (move is { } moved)
                MarkerMoved?.Invoke(this, moved);
            else if (IsLocked)
                LockedPressed?.Invoke(this, EventArgs.Empty);
            else if (CanSeek && click)
                SeekRequested?.Invoke(this, FractionAt(point));   // a click on a marker: as anywhere else
            return;
        }
        e.Pointer.Capture(null);
        // A mouse click goes to that spot. A finger has to drag: a tap on the close-up mustn't move the song.
        if (!_dragged && CanSeek && click)
            SeekRequested?.Invoke(this, FractionAt(e.GetPosition(this)));
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_grab.IsHolding)
        {
            _grab.Cancel();   // taken away (a menu opening, say): the marker stays where it was
            SceneChanged();
        }
    }
}

/// <summary>
/// Draws <see cref="CloseUpWaveform"/> on the render thread (#364): at every frame while the deck
/// plays, from the scene the window's thread last sent and the shared clock. The window's thread
/// only says when something changes, so a busy moment there never shows as the waveform stuttering.
/// </summary>
internal sealed class CloseUpHandler(CloseUpClock clock) : CompositionCustomVisualHandler
{
    /// <summary>Messages: draw at every frame (playing), stop doing that, or draw once (a seek while paused).</summary>
    public static readonly object Play = new(), Stop = new(), Redraw = new();

    private readonly ImmediateCanvas _canvas = new();
    private CloseUpScene? _scene;
    private bool _playing;

    public override void OnMessage(object message)
    {
        if (message is CloseUpScene scene)
            _scene = scene;
        else if (message == Play)
            _playing = true;
        else if (message == Stop)
            _playing = false;
        Invalidate();
        if (_playing)
            RegisterForNextAnimationFrameUpdate();
    }

    public override void OnAnimationFrameUpdate()
    {
        if (!_playing)
            return;
        Invalidate();
        RegisterForNextAnimationFrameUpdate();
    }

    public override void OnRender(ImmediateDrawingContext context)
    {
        if (_scene is { } scene)
            CloseUpPicture.Draw(_canvas.On(context), scene, clock.Playhead, new Size(EffectiveSize.X, EffectiveSize.Y));
    }
}
