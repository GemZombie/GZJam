using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace GZJam.App.Controls;

/// <summary>
/// An upright LED level meter like a mixer's: a column of segments for left and right (one wider
/// column for a mono output), green, then amber near the top, red at the top segment, with a dB
/// scale beside it and a peak marker that holds for a moment before falling. Levels are 0..1 up
/// the meter (see <see cref="FromPeak"/>). Without the scale it's a single slim column, as on
/// each deck.
/// </summary>
public sealed class LevelMeter : Control
{
    /// <summary>The quietest level shown, in dB below full scale.</summary>
    public const double Range = 48;

    /// <summary>How far a meter falls per tick (about 30 a second): roughly 30 dB a second.</summary>
    public const double Fall = 0.02;

    private const double Segment = 3, Gap = 1.5, ColumnGap = 2, ScaleWidth = 20;
    private static readonly TimeSpan PeakHold = TimeSpan.FromSeconds(1.2);

    public static readonly StyledProperty<double> LeftProperty =
        AvaloniaProperty.Register<LevelMeter, double>(nameof(Left));

    public static readonly StyledProperty<double> RightProperty =
        AvaloniaProperty.Register<LevelMeter, double>(nameof(Right));

    public static readonly StyledProperty<bool> IsMonoProperty =
        AvaloniaProperty.Register<LevelMeter, bool>(nameof(IsMono));

    public static readonly StyledProperty<bool> ShowScaleProperty =
        AvaloniaProperty.Register<LevelMeter, bool>(nameof(ShowScale), defaultValue: true);

    private static readonly IBrush Off = new ImmutableSolidColorBrush(Color.Parse("#1C2027"));
    private static readonly IBrush Green = new ImmutableSolidColorBrush(Color.Parse("#34D399"));
    private static readonly IBrush Amber = new ImmutableSolidColorBrush(Color.Parse("#F5C024"));
    private static readonly IBrush Red = new ImmutableSolidColorBrush(Color.Parse("#F04444"));
    private static readonly IBrush LabelBrush = new ImmutableSolidColorBrush(Color.Parse("#5D6574"));
    private static readonly IBrush ZeroLabelBrush = new ImmutableSolidColorBrush(Color.Parse("#8B94A5"));

    private readonly (double Level, long At)[] _peaks = new (double, long)[2];

    static LevelMeter()
    {
        AffectsRender<LevelMeter>(LeftProperty, RightProperty, IsMonoProperty, ShowScaleProperty);
        AffectsMeasure<LevelMeter>(ShowScaleProperty);
    }

    public double Left
    {
        get => GetValue(LeftProperty);
        set => SetValue(LeftProperty, value);
    }

    public double Right
    {
        get => GetValue(RightProperty);
        set => SetValue(RightProperty, value);
    }

    /// <summary>One column: the output is mono, so left and right are the same.</summary>
    public bool IsMono
    {
        get => GetValue(IsMonoProperty);
        set => SetValue(IsMonoProperty, value);
    }

    /// <summary>The dB scale beside the meter.</summary>
    public bool ShowScale
    {
        get => GetValue(ShowScaleProperty);
        set => SetValue(ShowScaleProperty, value);
    }

    /// <summary>Converts a peak sample (0..1) to a position up the meter (0..1), in decibels.</summary>
    public static double FromPeak(double peak) =>
        peak <= 0 ? 0 : Math.Clamp((20 * Math.Log10(peak) + Range) / Range, 0, 1);

    // The slim deck meter takes the deck's height and asks for none of its own.
    protected override Size MeasureOverride(Size availableSize) => ShowScale ? new(42, 100) : new(8, 0);

    public override void Render(DrawingContext context)
    {
        double meterWidth = Bounds.Width - (ShowScale ? ScaleWidth : 0);
        int count = (int)((Bounds.Height + Gap) / (Segment + Gap));
        if (count <= 0 || meterWidth < 2)
            return;
        double height = (Bounds.Height + Gap) / count - Gap;   // fill the height exactly
        if (IsMono)
        {
            DrawColumn(context, 0, Math.Max(Left, Right), 0, meterWidth, count, height);
        }
        else
        {
            double column = (meterWidth - ColumnGap) / 2;
            DrawColumn(context, 0, Left, 0, column, count, height);
            DrawColumn(context, 1, Right, column + ColumnGap, column, count, height);
        }
        if (ShowScale)
            DrawScale(context, meterWidth);
    }

    private void DrawColumn(DrawingContext context, int channel, double level, double x, double width, int count, double height)
    {
        int lit = (int)Math.Round(level * count);
        int peak = PeakSegment(channel, lit);
        for (int i = 0; i < count; i++)
        {
            // The top segment is the limiter's ceiling (-1 dB); amber covers the 6 dB below it.
            double top = (i + 1.0) / count;
            bool on = i < lit || (i == peak - 1 && peak > 0);
            IBrush brush = !on ? Off
                : i == count - 1 ? Red
                : top > 1 - 7 / Range ? Amber
                : Green;
            double y = Bounds.Height - (i + 1) * (height + Gap) + Gap;
            context.FillRectangle(brush, new Rect(x, y, width, height), 0.5f);
        }
    }

    /// <summary>The highest segment lit lately: holds, then falls to the level.</summary>
    private int PeakSegment(int channel, int lit)
    {
        long now = TimeProvider.System.GetTimestamp();
        var (level, at) = _peaks[channel];
        if (lit >= level || TimeProvider.System.GetElapsedTime(at, now) > PeakHold)
            _peaks[channel] = (lit, now);
        return (int)_peaks[channel].Level;
    }

    private void DrawScale(DrawingContext context, double meterWidth)
    {
        var typeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
        double below = double.NegativeInfinity;   // where the last label drawn ends
        foreach (int db in new[] { 0, -6, -12, -24, -48 })
        {
            string text = db == 0 ? "0" : $"−{-db}";
            var label = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, 9, db == 0 ? ZeroLabelBrush : LabelBrush);
            double y = Bounds.Height * (-db / Range);
            y = Math.Clamp(y - label.Height / 2, 0, Bounds.Height - label.Height);
            if (y < below)
                continue;   // a short meter (compact decks): no room for this one between its neighbors
            context.DrawText(label, new Point(meterWidth + 4, y));
            below = y + label.Height;
        }
    }
}
