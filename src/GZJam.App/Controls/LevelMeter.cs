using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace GZJam.App.Controls;

/// <summary>
/// A level meter: two horizontal rows of segments (left and right), or one taller row for a mono
/// output. Green, then amber near the top, then red at the last segment. Levels are 0..1 along
/// the meter (see <see cref="FromPeak"/>).
/// </summary>
public sealed class LevelMeter : Control
{
    /// <summary>The quietest level shown, in dB below full scale.</summary>
    public const double Range = 48;

    private const double Segment = 4, Gap = 1.5, RowGap = 2;

    public static readonly StyledProperty<double> LeftProperty =
        AvaloniaProperty.Register<LevelMeter, double>(nameof(Left));

    public static readonly StyledProperty<double> RightProperty =
        AvaloniaProperty.Register<LevelMeter, double>(nameof(Right));

    public static readonly StyledProperty<bool> IsMonoProperty =
        AvaloniaProperty.Register<LevelMeter, bool>(nameof(IsMono));

    private static readonly IBrush Off = new ImmutableSolidColorBrush(Color.Parse("#232833"));
    private static readonly IBrush Green = new ImmutableSolidColorBrush(Color.Parse("#34D399"));
    private static readonly IBrush Amber = new ImmutableSolidColorBrush(Color.Parse("#F5C024"));
    private static readonly IBrush Red = new ImmutableSolidColorBrush(Color.Parse("#F04444"));

    static LevelMeter() => AffectsRender<LevelMeter>(LeftProperty, RightProperty, IsMonoProperty);

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

    /// <summary>One row: the output is mono, so left and right are the same.</summary>
    public bool IsMono
    {
        get => GetValue(IsMonoProperty);
        set => SetValue(IsMonoProperty, value);
    }

    /// <summary>Converts a peak sample (0..1) to a position along the meter (0..1), in decibels.</summary>
    public static double FromPeak(double peak) =>
        peak <= 0 ? 0 : Math.Clamp((20 * Math.Log10(peak) + Range) / Range, 0, 1);

    protected override Size MeasureOverride(Size availableSize) => new(0, 12);

    public override void Render(DrawingContext context)
    {
        double row = IsMono ? Bounds.Height : (Bounds.Height - RowGap) / 2;
        int count = (int)((Bounds.Width + Gap) / (Segment + Gap));
        if (count <= 0 || row <= 0)
            return;
        double width = (Bounds.Width + Gap) / count - Gap;   // fill the width exactly
        if (IsMono)
        {
            DrawRow(context, Math.Max(Left, Right), 0, row, count, width);
            return;
        }
        DrawRow(context, Left, 0, row, count, width);
        DrawRow(context, Right, row + RowGap, row, count, width);
    }

    private static void DrawRow(DrawingContext context, double level, double y, double height, int count, double width)
    {
        int lit = (int)Math.Round(level * count);
        for (int i = 0; i < count; i++)
        {
            // The last segment is the limiter's ceiling (-1 dB); amber covers the 6 dB below it.
            double top = (i + 1.0) / count;
            IBrush brush = i >= lit ? Off
                : i == count - 1 ? Red
                : top > 1 - 7 / Range ? Amber
                : Green;
            context.FillRectangle(brush, new Rect(i * (width + Gap), y, width, height), 1);
        }
    }
}
