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
/// coming and setting points precisely. Drawn like <see cref="SeekBar"/> (bars coloured by
/// frequency, what's played dimmed, trimmed silence shaded, jumps and cues marked), from a close-up
/// waveform (10 ms a slice) or, until that's ready, the overview.
/// <para>
/// Drag it to move through the song like a record (a mouse click goes to that spot); locked, a
/// press says why. The mouse wheel or a pinch shows more or less of the song.
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

    /// <summary>The deck's colour, for the playhead while locked (as on the whole-song view).</summary>
    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<CloseUpWaveform, IBrush?>(nameof(Fill), Brushes.White);

    static CloseUpWaveform()
    {
        AffectsRender<CloseUpWaveform>(WaveformProperty, LengthProperty, ProgressProperty, WindowSecondsProperty,
            TrimStartProperty, TrimEndProperty, IsStartManualProperty, IsEndManualProperty, JumpsProperty,
            PendingJumpFromProperty, CuesProperty, IsLockedProperty, FillProperty, IsEnabledProperty);
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

    /// <summary>Asks to move the playhead to a fraction (0..1) of the song.</summary>
    public event EventHandler<double>? SeekRequested;

    /// <summary>Pressed while <see cref="IsLocked"/>.</summary>
    public event EventHandler? LockedPressed;

    /// <summary>The wheel or a pinch asked for more of the song while it already shows the most it can.</summary>
    public event EventHandler? ZoomedOutPastWidest;

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
    private double WindowStart => Progress * Length - WindowSeconds / 2;

    /// <summary>The position (0..1 through the song) at a point on the close-up.</summary>
    public double FractionAt(Point point) =>
        Length > 0 ? Math.Clamp((WindowStart + point.X * SecondsPerPixel) / Length, 0, 1) : 0;

    /// <summary>Where a fraction of the song is on the close-up, in pixels (may be off either side).</summary>
    private double X(double fraction) => (fraction * Length - WindowStart) / SecondsPerPixel;

    // ───────────── Drawing ─────────────

    private const double BarStep = 2;
    private static readonly IBrush Background = new ImmutableSolidColorBrush(Color.Parse("#0E1116"));
    private static readonly IBrush CentreLine = new ImmutableSolidColorBrush(Colors.White, 0.08);
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
                    context.DrawRectangle(CentreLine, null, new Rect(0, bounds.Height / 2 - 0.5, bounds.Width, 1));
                DrawTrim(context, bounds);
                DrawJumps(context, bounds);
                DrawCues(context, bounds);
            }
        }
        context.DrawRectangle(CanSeek ? Brushes.White : Fill, null, new Rect(middle - 1, 0, 2, bounds.Height));
    }

    private void DrawBars(DrawingContext context, Waveform waveform, Rect bounds, double playhead)
    {
        double centre = bounds.Height / 2, reach = centre - 3;
        double maxPeak = Waveform.Decode(waveform.Peak), maxLoudness = Waveform.Decode(waveform.LoudnessPeak);
        double slicesPerSecond = waveform.Count / Length, start = WindowStart, spp = SecondsPerPixel;
        for (double x = 0; x < bounds.Width; x += BarStep)
        {
            double from = (start + x * spp) * slicesPerSecond, to = (start + (x + BarStep) * spp) * slicesPerSecond;
            int a = (int)Math.Floor(from), b = Math.Max(a + 1, (int)Math.Ceiling(to));
            if (b <= 0 || a >= waveform.Count)
                continue;
            byte low = 0, mid = 0, high = 0, loud = 0;
            for (int s = Math.Max(0, a); s < b && s < waveform.Count; s++)
            {
                low = Math.Max(low, waveform.Low[s]);
                mid = Math.Max(mid, waveform.Mid[s]);
                high = Math.Max(high, waveform.High[s]);
                loud = Math.Max(loud, waveform.Loudness[s]);
            }
            double body = Math.Max(0.5, Waveform.Decode(loud) / maxLoudness * reach);
            double peak = Math.Max(body, Waveform.Decode(Math.Max(low, Math.Max(mid, high))) / maxPeak * reach);
            var (bright, dim, outline) = BarBrushes(SeekBar.BandColour(low, mid, high));
            bool played = x + BarStep <= playhead;
            context.DrawRectangle(played ? dim : outline, null, new Rect(x, centre - peak, BarStep, peak * 2));
            context.DrawRectangle(played ? dim : bright, null, new Rect(x, centre - body, BarStep, body * 2));
        }
    }

    private (IBrush, IBrush, IBrush) BarBrushes(Color colour)
    {
        if (!_brushes.TryGetValue(colour, out var brushes))
        {
            brushes = (new ImmutableSolidColorBrush(colour), new ImmutableSolidColorBrush(colour, 0.3), new ImmutableSolidColorBrush(colour, 0.22));
            if (_brushes.Count < 4096)
                _brushes[colour] = brushes;
        }
        return brushes;
    }

    private void DrawTrim(DrawingContext context, Rect bounds)
    {
        double start = X(Math.Clamp(TrimStart, 0, 1)), end = X(Math.Clamp(TrimEnd, 0, 1));
        // Before the song's own start and after its end (the file's edges) is plain background.
        double fileStart = X(0), fileEnd = X(1);
        if (start > 0 && (TrimStart > 0 || IsStartManual))
        {
            context.DrawRectangle(TrimShade, null, new Rect(Math.Max(0, fileStart), 0, Math.Max(0, start - Math.Max(0, fileStart)), bounds.Height));
            context.DrawRectangle(IsStartManual ? SeekBar.JumpBrush : TrimMarker, null, new Rect(start - (IsStartManual ? 1 : 0.5), 0, IsStartManual ? 2 : 1, bounds.Height));
        }
        if (end < bounds.Width && (TrimEnd < 1 || IsEndManual))
        {
            context.DrawRectangle(TrimShade, null, new Rect(end, 0, Math.Max(0, Math.Min(bounds.Width, fileEnd) - end), bounds.Height));
            context.DrawRectangle(IsEndManual ? SeekBar.JumpBrush : TrimMarker, null, new Rect(end - (IsEndManual ? 1 : 0.5), 0, IsEndManual ? 2 : 1, bounds.Height));
        }
    }

    private void DrawJumps(DrawingContext context, Rect bounds)
    {
        foreach (JumpMarker jump in Jumps ?? [])
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
        foreach (CueMarker cue in Cues ?? [])
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
        if (IsLocked)
        {
            LockedPressed?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }
        e.Pointer.Capture(this);
        (_pressedAt, _progressAtPress, _dragged) = (e.GetPosition(this), Progress, false);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!CanSeek || e.Pointer.Captured != this || Length <= 0)
            return;
        double dx = e.GetPosition(this).X - _pressedAt.X;
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
        e.Pointer.Capture(null);
        // A mouse click goes to that spot. A finger has to drag: a tap on the close-up mustn't move the song.
        if (!_dragged && CanSeek && e.Pointer.Type == PointerType.Mouse && e.InitialPressMouseButton == MouseButton.Left)
            SeekRequested?.Invoke(this, FractionAt(e.GetPosition(this)));
    }
}
