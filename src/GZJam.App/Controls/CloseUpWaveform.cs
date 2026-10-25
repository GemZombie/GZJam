using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
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
/// from the clock and the tempo and eased toward where the deck says it is (which arrives about
/// 30 times a second, in uneven 20 and 40 ms steps), so the music glides past instead of
/// stuttering. Each bar covers a fixed stretch of the song, so bars keep their shape as they
/// move; zoomed in so far that a bar is shorter than a slice, bars blend the slices either side.
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

    static CloseUpWaveform()
    {
        AffectsRender<CloseUpWaveform>(WaveformProperty, LengthProperty, ProgressProperty, WindowSecondsProperty,
            TrimStartProperty, TrimEndProperty, IsStartManualProperty, IsEndManualProperty, JumpsProperty,
            PendingJumpFromProperty, CuesProperty, IsLockedProperty, FillProperty, IsEnabledProperty, IsPlayingProperty);
    }

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
    public IReadOnlyList<JumpMarker>? Jumps { get => GetValue(JumpsProperty); set => SetValue(JumpsProperty, value); }
    public double? PendingJumpFrom { get => GetValue(PendingJumpFromProperty); set => SetValue(PendingJumpFromProperty, value); }
    public IReadOnlyList<CueMarker>? Cues { get => GetValue(CuesProperty); set => SetValue(CuesProperty, value); }
    public bool IsLocked { get => GetValue(IsLockedProperty); set => SetValue(IsLockedProperty, value); }
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public bool IsPlaying { get => GetValue(IsPlayingProperty); set => SetValue(IsPlayingProperty, value); }
    public double TempoPercent { get => GetValue(TempoPercentProperty); set => SetValue(TempoPercentProperty, value); }

    // ───────────── The playhead, between reports ─────────────

    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private double _clockFrom;    // the song's position (s) at _clockAt
    private double _clockAt;      // on _clock, in seconds
    private double _correction;   // where the corrections from reports have got to (s)
    private double _shown;        // how much of it is shown so far: it follows over CorrectionSpread
    private double _shownAt;      // on _clock, in seconds
    private bool _animating;

    /// <summary>A report's error counts for this much, so one report moves the playhead little.</summary>
    private const double CorrectionGain = 0.1;

    /// <summary>
    /// The most a report's error counts for (s): reports come in 20 and 40 ms steps, now and then
    /// 80, and one far out mustn't jolt the picture.
    /// </summary>
    private const double CorrectionCap = 0.03;

    /// <summary>
    /// Each correction is spread over about this long (s), not made in one frame: zoomed in tight,
    /// a few milliseconds at once is a visible jerk. Measured on real reports (GZJam.DevTools
    /// clock): the worst frame went from about 2 px off steady to 0.4 px, as close to the music.
    /// </summary>
    private const double CorrectionSpread = 0.15;

    private double Now => _clock.Elapsed.TotalSeconds;

    private static double Rate(double tempoPercent) => 1 + tempoPercent / 100;

    private double Clock(double now) => _clockFrom + (now - _clockAt) * Rate(TempoPercent);

    private double Predicted => Clock(Now) + Shown(Now);

    /// <summary>The part of the correction shown by <paramref name="now"/>, catching up smoothly.</summary>
    private double Shown(double now)
    {
        if (now > _shownAt)
        {
            _shown = _correction - (_correction - _shown) * Math.Exp(-(now - _shownAt) / CorrectionSpread);
            _shownAt = now;
        }
        return _shown;
    }

    /// <summary>Where the playhead is drawn, in seconds into the song.</summary>
    internal double Playhead => IsPlaying && Length > 0 ? Math.Clamp(Predicted, 0, Length) : Progress * Length;

    /// <summary>Starts the clock again from where the deck says it is.</summary>
    private void Restart() => (_clockFrom, _clockAt, _correction, _shown, _shownAt) = (Progress * Length, Now, 0, 0, Now);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ProgressProperty || change.Property == LengthProperty)
        {
            double now = Now, reported = Progress * Length;
            if (!IsPlaying || Math.Abs(reported - Predicted) > 0.25)
                Restart();               // paused, a seek, a jump, a new song
            else
            {
                // Reports come in uneven steps: ease toward them, a little at a time.
                Shown(now);
                double error = reported - (Clock(now) + _correction);
                _correction += Math.Clamp(error, -CorrectionCap, CorrectionCap) * CorrectionGain;
            }
            Animate();
        }
        else if (change.Property == TempoPercentProperty)
        {
            // Carry on from where the old tempo had got to.
            double now = Now;
            _clockFrom += (now - _clockAt) * Rate(change.GetOldValue<double>());
            _clockAt = now;
        }
        else if (change.Property == IsPlayingProperty || change.Property == IsVisibleProperty)
        {
            Restart();
            Animate();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Animate();
    }

    /// <summary>While playing and shown, redraws at every frame the screen shows.</summary>
    private void Animate()
    {
        if (_animating || !IsPlaying || !IsVisible || TopLevel.GetTopLevel(this) is not { } top)
            return;
        _animating = true;
        top.RequestAnimationFrame(_ =>
        {
            _animating = false;
            if (!IsPlaying || !IsVisible)
                return;
            // A marker held still while the music moves on stays under the finger.
            if (_grab.IsDragging)
                _grab.Drag(_dragX, FractionAt(new Point(_dragX, 0)));
            InvalidateVisual();
            Animate();
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

    private const double BarStep = 2;
    private static readonly IBrush Background = new ImmutableSolidColorBrush(Color.Parse("#0E1116"));
    private static readonly IBrush CenterLine = new ImmutableSolidColorBrush(Colors.White, 0.08);
    private static readonly IBrush TrimShade = new ImmutableSolidColorBrush(Color.FromArgb(150, 12, 14, 18));
    private static readonly IBrush TrimMarker = new ImmutableSolidColorBrush(Color.FromRgb(0x8B, 0x94, 0xA5));
    private readonly Dictionary<Color, (IBrush Bright, IBrush Dim, IBrush Outline)> _brushes = [];

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width < 4 || bounds.Height < 1)
            return;
        context.DrawRectangle(Background, null, bounds, 3, 3);
        double middle = Math.Round(bounds.Width / 2);
        if (Length > 0 && SecondsPerPixel > 0)
        {
            using (context.PushClip(bounds))
            {
                if (Waveform is { Count: > 0, Peak: > 0, LoudnessPeak: > 0 } waveform)
                    DrawBars(context, waveform, bounds, middle);
                else
                    context.DrawRectangle(CenterLine, null, new Rect(0, bounds.Height / 2 - 0.5, bounds.Width, 1));
                DrawTrim(context, bounds);
                DrawJumps(context, bounds);
                DrawCues(context, bounds);
            }
        }
        context.DrawRectangle(CanSeek ? Brushes.White : Fill, null, new Rect(middle - 1, 0, 2, bounds.Height));
    }

    private void DrawBars(DrawingContext context, Waveform waveform, Rect bounds, double playhead)
    {
        double center = bounds.Height / 2, reach = center - 3;
        double maxPeak = Waveform.Decode(waveform.Peak), maxLoudness = Waveform.Decode(waveform.LoudnessPeak);
        double slicesPerSecond = waveform.Count / Length, start = WindowStart, spp = SecondsPerPixel;
        // Each bar is a fixed stretch of the song (not of the screen), so it keeps its shape as it
        // slides past; the first one may start just off the left edge.
        double barSeconds = BarStep * spp;
        for (long bar = (long)Math.Floor(start / barSeconds); ; bar++)
        {
            double from = bar * barSeconds, x = (from - start) / spp;
            if (x >= bounds.Width)
                break;
            if (BarLevels(waveform, from * slicesPerSecond, (from + barSeconds) * slicesPerSecond) is not var (low, mid, high, loud))
                continue;
            double body = Math.Max(0.5, Waveform.Decode(loud) / maxLoudness * reach);
            double peak = Math.Max(body, Waveform.Decode(Math.Max(low, Math.Max(mid, high))) / maxPeak * reach);
            var (bright, dim, outline) = BarBrushes(SeekBar.BandColor(low, mid, high));
            bool played = x + BarStep <= playhead;
            context.DrawRectangle(played ? dim : outline, null, new Rect(x, center - peak, BarStep, peak * 2));
            context.DrawRectangle(played ? dim : bright, null, new Rect(x, center - body, BarStep, body * 2));
        }
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

    private (IBrush, IBrush, IBrush) BarBrushes(Color color)
    {
        if (!_brushes.TryGetValue(color, out var brushes))
        {
            brushes = (new ImmutableSolidColorBrush(color), new ImmutableSolidColorBrush(color, 0.3), new ImmutableSolidColorBrush(color, 0.22));
            if (_brushes.Count < 4096)
                _brushes[color] = brushes;
        }
        return brushes;
    }

    private void DrawTrim(DrawingContext context, Rect bounds)
    {
        double trimStart = _grab.TrimStart(TrimStart), trimEnd = _grab.TrimEnd(TrimEnd);
        bool startManual = _grab.StartManual(IsStartManual), endManual = _grab.EndManual(IsEndManual);
        double start = X(Math.Clamp(trimStart, 0, 1)), end = X(Math.Clamp(trimEnd, 0, 1));
        // Before the song's own start and after its end (the file's edges) is plain background.
        double fileStart = X(0), fileEnd = X(1);
        if (start > 0 && (trimStart > 0 || startManual))
        {
            context.DrawRectangle(TrimShade, null, new Rect(Math.Max(0, fileStart), 0, Math.Max(0, start - Math.Max(0, fileStart)), bounds.Height));
            context.DrawRectangle(startManual ? SeekBar.JumpBrush : TrimMarker, null, new Rect(start - (startManual ? 1 : 0.5), 0, startManual ? 2 : 1, bounds.Height));
        }
        if (end < bounds.Width && (trimEnd < 1 || endManual))
        {
            context.DrawRectangle(TrimShade, null, new Rect(end, 0, Math.Max(0, Math.Min(bounds.Width, fileEnd) - end), bounds.Height));
            context.DrawRectangle(endManual ? SeekBar.JumpBrush : TrimMarker, null, new Rect(end - (endManual ? 1 : 0.5), 0, endManual ? 2 : 1, bounds.Height));
        }
    }

    private void DrawJumps(DrawingContext context, Rect bounds)
    {
        foreach (JumpMarker jump in _grab.Jumps(Jumps) ?? [])
        {
            double from = X(Math.Clamp(jump.From, 0, 1)), to = X(Math.Clamp(jump.To, 0, 1));
            IBrush brush = jump.Armed ? SeekBar.JumpBrush : SeekBar.JumpOffBrush;
            if (jump.Armed)
                context.DrawRectangle(to > from ? SeekBar.SkippedShade : SeekBar.RepeatShade, null, new Rect(Math.Min(from, to), 0, Math.Abs(to - from), bounds.Height));
            context.DrawRectangle(brush, null, new Rect(from - 1, 0, 2, bounds.Height));
            context.DrawRectangle(brush, null, new Rect(to - 1, 0, 2, bounds.Height));
            const double arrow = 6;
            double direction = to > from ? 1 : -1, y = bounds.Height - arrow - 1;
            context.DrawGeometry(brush, null, new PolylineGeometry(
                [new Point(from, y - arrow / 2), new Point(from + direction * arrow, y), new Point(from, y + arrow / 2)], isFilled: true));
        }
        if (PendingJumpFrom is { } pending)
            context.DrawRectangle(SeekBar.JumpOffBrush, null, new Rect(X(Math.Clamp(pending, 0, 1)) - 1, 0, 2, bounds.Height));
    }

    private void DrawCues(DrawingContext context, Rect bounds)
    {
        foreach (CueMarker cue in _grab.Cues(Cues) ?? [])
        {
            double at = X(Math.Clamp(cue.At, 0, 1));
            if (at >= -1 && at <= bounds.Width + 1)   // off the close-up: not pinned to its edge
                SeekBar.DrawCue(context, bounds, at, cue.Number);
        }
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
                InvalidateVisual();
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
            InvalidateVisual();
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
            InvalidateVisual();
        }
    }
}
