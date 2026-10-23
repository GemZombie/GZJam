using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using GZJam.Audio;

namespace GZJam.App.Controls;

/// <summary>A jump drawn on the waveform: where it leaves and lands (fractions of the file), and whether it's armed.</summary>
public readonly record struct JumpMarker(double From, double To, bool Armed);

/// <summary>A cue drawn on the waveform: where it is (a fraction of the file) and its number.</summary>
public readonly record struct CueMarker(double At, int Number);

/// <summary>
/// A track's overview waveform with a playhead; press or drag to seek (mouse, touch or pen).
/// Each bar's solid body is how loud that moment is, with its peaks as a faint outline; bars are
/// coloured by frequency (red bass, green mids, blue highs, blending between). The part already
/// played is dimmed, and the silence trimmed off either end is shaded. Until the waveform is
/// ready it's a plain position bar.
/// </summary>
public sealed class SeekBar : Control
{
    public static readonly StyledProperty<Waveform?> WaveformProperty =
        AvaloniaProperty.Register<SeekBar, Waveform?>(nameof(Waveform));

    /// <summary>Where the music starts, 0..1; before it is skipped silence.</summary>
    public static readonly StyledProperty<double> TrimStartProperty =
        AvaloniaProperty.Register<SeekBar, double>(nameof(TrimStart));

    /// <summary>Where the music ends, 0..1; after it is skipped silence.</summary>
    public static readonly StyledProperty<double> TrimEndProperty =
        AvaloniaProperty.Register<SeekBar, double>(nameof(TrimEnd), 1);

    /// <summary>The start was set by hand: its marker stands out.</summary>
    public static readonly StyledProperty<bool> IsStartManualProperty =
        AvaloniaProperty.Register<SeekBar, bool>(nameof(IsStartManual));

    /// <summary>The end was set by hand: its marker stands out.</summary>
    public static readonly StyledProperty<bool> IsEndManualProperty =
        AvaloniaProperty.Register<SeekBar, bool>(nameof(IsEndManual));

    /// <summary>The song's jumps, as fractions of the file.</summary>
    public static readonly StyledProperty<IReadOnlyList<JumpMarker>?> JumpsProperty =
        AvaloniaProperty.Register<SeekBar, IReadOnlyList<JumpMarker>?>(nameof(Jumps));

    /// <summary>Where a jump being set leaves from, waiting for where it lands.</summary>
    public static readonly StyledProperty<double?> PendingJumpFromProperty =
        AvaloniaProperty.Register<SeekBar, double?>(nameof(PendingJumpFrom));

    /// <summary>The song's cues, as fractions of the file.</summary>
    public static readonly StyledProperty<IReadOnlyList<CueMarker>?> CuesProperty =
        AvaloniaProperty.Register<SeekBar, IReadOnlyList<CueMarker>?>(nameof(Cues));

    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<SeekBar, double>(nameof(Progress));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<SeekBar, IBrush?>(nameof(Fill), Brushes.White);

    public static readonly StyledProperty<IBrush?> TrackBackgroundProperty =
        AvaloniaProperty.Register<SeekBar, IBrush?>(nameof(TrackBackground), new SolidColorBrush(Color.Parse("#0E1116")));

    /// <summary>
    /// Seeking refused (e.g. the room is hearing this deck). Unlike disabling the control, a press
    /// still raises <see cref="LockedPressed"/>, so the app can say why nothing happened.
    /// </summary>
    public static readonly StyledProperty<bool> IsLockedProperty =
        AvaloniaProperty.Register<SeekBar, bool>(nameof(IsLocked));

    public static readonly StyledProperty<double> CornerRadiusProperty =
        AvaloniaProperty.Register<SeekBar, double>(nameof(CornerRadius), 3);

    static SeekBar()
    {
        AffectsRender<SeekBar>(ProgressProperty, FillProperty, TrackBackgroundProperty, IsEnabledProperty, IsLockedProperty,
            WaveformProperty, TrimStartProperty, TrimEndProperty, IsStartManualProperty, IsEndManualProperty,
            JumpsProperty, PendingJumpFromProperty, CuesProperty);
    }

    public Waveform? Waveform
    {
        get => GetValue(WaveformProperty);
        set => SetValue(WaveformProperty, value);
    }

    public double TrimStart
    {
        get => GetValue(TrimStartProperty);
        set => SetValue(TrimStartProperty, value);
    }

    public double TrimEnd
    {
        get => GetValue(TrimEndProperty);
        set => SetValue(TrimEndProperty, value);
    }

    public bool IsStartManual
    {
        get => GetValue(IsStartManualProperty);
        set => SetValue(IsStartManualProperty, value);
    }

    public bool IsEndManual
    {
        get => GetValue(IsEndManualProperty);
        set => SetValue(IsEndManualProperty, value);
    }

    /// <summary>The position (0..1) at a point on the bar.</summary>
    public double FractionAt(Point point) => Bounds.Width > 0 ? Math.Clamp(point.X / Bounds.Width, 0, 1) : 0;

    public double Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IBrush? TrackBackground
    {
        get => GetValue(TrackBackgroundProperty);
        set => SetValue(TrackBackgroundProperty, value);
    }

    public bool IsLocked
    {
        get => GetValue(IsLockedProperty);
        set => SetValue(IsLockedProperty, value);
    }

    public double CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>Raised with the requested position, 0..1, while pressing or dragging.</summary>
    public event EventHandler<double>? SeekRequested;

    /// <summary>Raised when the bar is pressed while <see cref="IsLocked"/>.</summary>
    public event EventHandler? LockedPressed;

    public IReadOnlyList<JumpMarker>? Jumps
    {
        get => GetValue(JumpsProperty);
        set => SetValue(JumpsProperty, value);
    }

    public double? PendingJumpFrom
    {
        get => GetValue(PendingJumpFromProperty);
        set => SetValue(PendingJumpFromProperty, value);
    }

    public IReadOnlyList<CueMarker>? Cues
    {
        get => GetValue(CuesProperty);
        set => SetValue(CuesProperty, value);
    }

    private bool CanSeek => IsEnabled && !IsLocked;

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width < 4 || bounds.Height < 1)
            return;   // collapsed or not laid out yet
        context.DrawRectangle(TrackBackground, null, bounds, CornerRadius, CornerRadius);
        if (Fill is null)
            return;

        double x = Math.Clamp(Progress, 0, 1) * bounds.Width;
        double centre = bounds.Height / 2;

        if (Waveform is { } waveform)
        {
            DrawWaveform(context, waveform, bounds, x);
        }
        else
        {
            // Not analyzed yet. Unplayed: a thin centre line. Played: a softer filled band.
            context.DrawRectangle(new SolidColorBrush(Colors.White, 0.08), null, new Rect(10, centre - 0.5, Math.Max(0, bounds.Width - 20), 1));
            if (x > 0)
            {
                using (context.PushOpacity(CanSeek ? 0.28 : 0.18))
                    context.DrawRectangle(Fill, null, new Rect(0, 0, x, bounds.Height), CornerRadius, CornerRadius);
            }
        }
        DrawJumps(context, bounds);
        DrawCues(context, bounds);
        context.DrawRectangle(CanSeek ? Brushes.White : Fill, null, new Rect(Math.Clamp(x - 1, 0, bounds.Width - 2), 0, 2, bounds.Height));
    }

    // ───────────── Jumps ─────────────

    private static readonly IBrush JumpBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x9D, 0xBE, 0xE6));
    private static readonly IBrush JumpOffBrush = new ImmutableSolidColorBrush(Color.FromArgb(120, 0x9D, 0xBE, 0xE6));
    private static readonly IBrush SkippedShade = new ImmutableSolidColorBrush(Color.FromArgb(140, 12, 14, 18));
    private static readonly IBrush RepeatShade = new ImmutableSolidColorBrush(Color.FromArgb(40, 0x9D, 0xBE, 0xE6));

    /// <summary>
    /// Each jump: a marker where it leaves (with an arrow towards where it lands) and where it lands.
    /// An armed forward jump shades the part it skips; an armed backward one tints the part it repeats.
    /// </summary>
    private void DrawJumps(DrawingContext context, Rect bounds)
    {
        foreach (JumpMarker jump in Jumps ?? [])
        {
            double from = Math.Clamp(jump.From, 0, 1) * bounds.Width, to = Math.Clamp(jump.To, 0, 1) * bounds.Width;
            IBrush brush = jump.Armed ? JumpBrush : JumpOffBrush;
            if (jump.Armed)
            {
                var span = new Rect(Math.Min(from, to), 0, Math.Abs(to - from), bounds.Height);
                context.DrawRectangle(to > from ? SkippedShade : RepeatShade, null, span);
            }
            context.DrawRectangle(brush, null, new Rect(from - 1, 0, 2, bounds.Height));
            context.DrawRectangle(brush, null, new Rect(to - 1, 0, 2, bounds.Height));
            // An arrow at the bottom of the leaving marker, pointing the way the jump goes.
            const double arrow = 6;
            double direction = to > from ? 1 : -1, y = bounds.Height - arrow - 1;
            var geometry = new PolylineGeometry(
                [new Point(from, y - arrow / 2), new Point(from + direction * arrow, y), new Point(from, y + arrow / 2)], isFilled: true);
            context.DrawGeometry(brush, null, geometry);
        }
        if (PendingJumpFrom is { } pending)
        {
            double at = Math.Clamp(pending, 0, 1) * bounds.Width;
            context.DrawRectangle(JumpOffBrush, null, new Rect(at - 1, 0, 2, bounds.Height));
        }
    }

    // ───────────── Cues ─────────────

    private static readonly IBrush CueNumberBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x0E, 0x11, 0x16));

    /// <summary>Each cue: a line, with its number on a flag at the top (the number on its key).</summary>
    private void DrawCues(DrawingContext context, Rect bounds)
    {
        var typeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);
        double flag = Math.Min(13, bounds.Height);
        foreach (CueMarker cue in Cues ?? [])
        {
            double at = Math.Clamp(cue.At, 0, 1) * bounds.Width;
            context.DrawRectangle(JumpBrush, null, new Rect(at - 1, 0, 2, bounds.Height));
            double left = Math.Clamp(at - 1, 0, bounds.Width - flag);
            context.DrawRectangle(JumpBrush, null, new Rect(left, 0, flag, flag));
            var number = new FormattedText(cue.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 10, CueNumberBrush);
            context.DrawText(number, new Point(left + (flag - number.Width) / 2, (flag - number.Height) / 2));
        }
    }

    // ───────────── Waveform ─────────────

    private const double BarStep = 2, BarWidth = 2;   // touching: a solid shape, like a DJ deck's
    private static readonly IBrush TrimShade = new ImmutableSolidColorBrush(Color.FromArgb(150, 12, 14, 18));
    private static readonly IBrush TrimMarker = new ImmutableSolidColorBrush(Color.FromRgb(0x8B, 0x94, 0xA5));
    private static readonly IBrush ManualMarker = new ImmutableSolidColorBrush(Color.FromRgb(0x9D, 0xBE, 0xE6));

    // Bars depend only on the waveform and the size, so they're worked out once, not every frame.
    private (Waveform Waveform, Size Size)? _barsFor;
    private (Rect Body, Rect Peak, IBrush Bright, IBrush Dim, IBrush Outline)[] _bars = [];

    private void DrawWaveform(DrawingContext context, Waveform waveform, Rect bounds, double playhead)
    {
        if (_barsFor != (waveform, bounds.Size))
        {
            _bars = BuildBars(waveform, bounds);
            _barsFor = (waveform, bounds.Size);
        }

        // Played: dimmed, so what's still to come stands out.
        foreach (var (body, peak, bright, dim, outline) in _bars)
        {
            bool played = body.Right <= playhead;
            context.DrawRectangle(played ? dim : outline, null, peak);
            context.DrawRectangle(played ? dim : bright, null, body);
        }

        // Trimmed silence at either end: shaded, with a marker where the music starts and ends.
        double start = Math.Clamp(TrimStart, 0, 1) * bounds.Width, end = Math.Clamp(TrimEnd, 0, 1) * bounds.Width;
        // A point the DJ set by hand gets a brighter, wider marker with a small flag at the top.
        if (start >= 1 || IsStartManual)
        {
            context.DrawRectangle(TrimShade, null, new Rect(0, 0, start, bounds.Height));
            DrawMarker(context, start, bounds.Height, IsStartManual, pointsRight: true);
        }
        if (end <= bounds.Width - 1 || IsEndManual)
        {
            context.DrawRectangle(TrimShade, null, new Rect(end, 0, bounds.Width - end, bounds.Height));
            DrawMarker(context, end - 1, bounds.Height, IsEndManual, pointsRight: false);
        }
    }

    private static void DrawMarker(DrawingContext context, double x, double height, bool manual, bool pointsRight)
    {
        if (!manual)
        {
            context.DrawRectangle(TrimMarker, null, new Rect(x, 0, 1, height));
            return;
        }
        context.DrawRectangle(ManualMarker, null, new Rect(x - 0.5, 0, 2, height));
        const double flag = 7;
        var tip = new Point(pointsRight ? x + flag : x - flag, flag / 2);
        var geometry = new PolylineGeometry([new Point(x, 0), tip, new Point(x, flag)], isFilled: true);
        context.DrawGeometry(ManualMarker, null, geometry);
    }

    private static (Rect, Rect, IBrush, IBrush, IBrush)[] BuildBars(Waveform waveform, Rect bounds)
    {
        int count = (int)(bounds.Width / BarStep);
        if (count == 0 || waveform.Count == 0 || waveform.Peak == 0 || waveform.LoudnessPeak == 0)
            return [];
        double centre = bounds.Height / 2, reach = centre - 3;
        double maxPeak = Waveform.Decode(waveform.Peak), maxLoudness = Waveform.Decode(waveform.LoudnessPeak);
        var bars = new (Rect, Rect, IBrush, IBrush, IBrush)[count];
        for (int i = 0; i < count; i++)
        {
            int from = i * waveform.Count / count, to = Math.Max(from + 1, (i + 1) * waveform.Count / count);
            byte low = 0, mid = 0, high = 0, loud = 0;
            for (int s = from; s < to && s < waveform.Count; s++)
            {
                low = Math.Max(low, waveform.Low[s]);
                mid = Math.Max(mid, waveform.Mid[s]);
                high = Math.Max(high, waveform.High[s]);
                loud = Math.Max(loud, waveform.Loudness[s]);
            }
            // The body: loudness, with the loudest moment filling the height. The outline: peaks.
            double body = Math.Max(0.5, Waveform.Decode(loud) / maxLoudness * reach);
            double peak = Math.Max(body, Waveform.Decode(Math.Max(low, Math.Max(mid, high))) / maxPeak * reach);
            Color colour = BandColour(low, mid, high);
            double x = i * BarStep;
            bars[i] = (new Rect(x, centre - body, BarWidth, body * 2),
                new Rect(x, centre - peak, BarWidth, peak * 2),
                new ImmutableSolidColorBrush(colour),
                new ImmutableSolidColorBrush(colour, 0.3),
                new ImmutableSolidColorBrush(colour, 0.22));
        }
        return bars;
    }

    /// <summary>Red for bass, green for mids, blue for highs, mixed by how strong each band is.</summary>
    private static Color BandColour(byte low, byte mid, byte high)
    {
        // Stored values are square roots of the level, so the 4th power is the level squared,
        // which lets the strongest band dominate the colour.
        static double Weight(byte b) => Math.Pow(b / 255.0, 4);
        double l = Weight(low), m = Weight(mid), h = Weight(high);
        double r = 1.00 * l + 0.25 * m + 0.30 * h;
        double g = 0.25 * l + 0.90 * m + 0.55 * h;
        double b = 0.25 * l + 0.35 * m + 1.00 * h;
        double max = Math.Max(r, Math.Max(g, b));
        if (max <= 0)
            return Color.FromRgb(120, 120, 120);
        (r, g, b) = (r / max, g / max, b / max);
        // Less saturated and a little darker than pure band colours: easier on the eye in a dark room.
        double grey = 0.3 * r + 0.55 * g + 0.15 * b;
        static byte Tone(double c, double grey) => (byte)(255 * 0.9 * (0.72 * c + 0.28 * grey));
        return Color.FromRgb(Tone(r, grey), Tone(g, grey), Tone(b, grey));
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
        RequestSeek(e);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (CanSeek && e.Pointer.Captured == this)
            RequestSeek(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Pointer.Captured == this)
            e.Pointer.Capture(null);
    }

    private void RequestSeek(PointerEventArgs e)
    {
        if (Bounds.Width <= 0)
            return;
        double fraction = Math.Clamp(e.GetPosition(this).X / Bounds.Width, 0, 1);
        SeekRequested?.Invoke(this, fraction);
    }
}
