using Avalonia;
using Avalonia.Controls;

namespace GZJam.App.Controls;

/// <summary>
/// Two children in a row: the first (say, a title) gets the room it needs, trimming itself only
/// if the row is too narrow; the second (say, a song's labels) goes right after it, with what's
/// left, cut off at the edge, or not shown if there's no useful room.
/// </summary>
public sealed class PriorityRow : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<PriorityRow, double>(nameof(Spacing), 6);

    /// <summary>Less room than this for the second child, and it isn't shown at all (no sliver of a chip).</summary>
    private const double LeastUseful = 24;

    static PriorityRow()
    {
        AffectsMeasure<PriorityRow>(SpacingProperty);
        ClipToBoundsProperty.OverrideDefaultValue<PriorityRow>(true);
    }

    public double Spacing { get => GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    private Control? First => Children.Count > 0 ? Children[0] : null;
    private Control? Second => Children.Count > 1 ? Children[1] : null;

    private bool HasSecond => Second is { IsVisible: true, DesiredSize.Width: > 0 };

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0, height = 0;
        if (First is { } first)
        {
            first.Measure(availableSize);
            (width, height) = (first.DesiredSize.Width, first.DesiredSize.Height);
        }
        if (Second is { } second)
        {
            double left = Math.Max(0, availableSize.Width - width - Spacing);
            second.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            if (HasSecond && left >= LeastUseful)
            {
                width += Spacing + Math.Min(second.DesiredSize.Width, left);
                height = Math.Max(height, second.DesiredSize.Height);
            }
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double firstWidth = First is { } first ? Math.Min(first.DesiredSize.Width, finalSize.Width) : 0;
        First?.Arrange(new Rect(0, 0, firstWidth, finalSize.Height));
        if (Second is { } second)
        {
            double x = firstWidth + Spacing, left = finalSize.Width - x;
            // At its full width (cut off by the clip), or out of sight if there's no useful room.
            second.Arrange(HasSecond && left >= LeastUseful
                ? new Rect(x, 0, second.DesiredSize.Width, finalSize.Height)
                : new Rect(finalSize.Width, 0, 0, 0));
        }
        return finalSize;
    }
}
