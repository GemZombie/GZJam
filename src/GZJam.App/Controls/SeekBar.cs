using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using GZJam.Audio;

namespace GZJam.App.Controls;

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
            WaveformProperty, TrimStartProperty, TrimEndProperty, IsStartManualProperty, IsEndManualProperty);
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
        context.DrawRectangle(CanSeek ? Brushes.White : Fill, null, new Rect(Math.Clamp(x - 1, 0, bounds.Width - 2), 0, 2, bounds.Height));
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
