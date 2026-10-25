using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using GZJam.Library;

namespace GZJam.App.Controls;

/// <summary>
/// A track's rating: a red heart (I'd play it), a gray broken heart (I wouldn't), or a faint
/// outline when unrated, so there's always something to tap. Drawn rather than an emoji, which
/// not every system's fonts have.
/// </summary>
public sealed class RatingIcon : Control
{
    public static readonly StyledProperty<Rating> RatingProperty =
        AvaloniaProperty.Register<RatingIcon, Rating>(nameof(Rating));

    /// <summary>Whether an unrated track shows the faint outline (false: nothing).</summary>
    public static readonly StyledProperty<bool> ShowUnratedProperty =
        AvaloniaProperty.Register<RatingIcon, bool>(nameof(ShowUnrated), true);

    public static readonly IBrush LovedBrush = new ImmutableSolidColorBrush(Color.Parse("#D9545C"));
    private static readonly IBrush RejectedBrush = new ImmutableSolidColorBrush(Color.Parse("#7A8292"));
    private static readonly IPen UnratedPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#4A5263")), 1.6);

    // In a 24 x 24 box.
    private static readonly Geometry Heart = Geometry.Parse("M12 20.5L4.2 12.7a4.8 4.8 0 0 1 6.8-6.8L12 6.9l1-1a4.8 4.8 0 0 1 6.8 6.8z");
    private static readonly Geometry LeftHalf = Half("M0 0H12L12 6.9L10.3 10.6L13.2 13.6L11 17.2L12 20.5V24H0z");
    private static readonly Geometry RightHalf = Half("M24 0H12L12 6.9L10.3 10.6L13.2 13.6L11 17.2L12 20.5V24H24z");

    private static Geometry Half(string clip) => new CombinedGeometry(GeometryCombineMode.Intersect, Heart, Geometry.Parse(clip));

    /// <summary>Turns a half about the bottom of the crack, then nudges it.</summary>
    private static Matrix Tilt(double radians, double dx, double dy) =>
        Matrix.CreateTranslation(-12, -20) * Matrix.CreateRotation(radians) * Matrix.CreateTranslation(12 + dx, 20 + dy);

    static RatingIcon() => AffectsRender<RatingIcon>(RatingProperty, ShowUnratedProperty);

    public Rating Rating
    {
        get => GetValue(RatingProperty);
        set => SetValue(RatingProperty, value);
    }

    public bool ShowUnrated
    {
        get => GetValue(ShowUnratedProperty);
        set => SetValue(ShowUnratedProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        double size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0)
            return;
        var scale = Matrix.CreateScale(size / 24, size / 24) *
                    Matrix.CreateTranslation((Bounds.Width - size) / 2, (Bounds.Height - size) / 2);
        using var _ = context.PushTransform(scale);
        switch (Rating)
        {
            case Rating.Loved:
                context.DrawGeometry(LovedBrush, null, Heart);
                break;
            case Rating.Rejected:
                // The two halves pulled apart along the crack.
                using (context.PushTransform(Tilt(-0.14, -1.2, 0.6)))
                    context.DrawGeometry(RejectedBrush, null, LeftHalf);
                using (context.PushTransform(Tilt(0.14, 1.2, 0.6)))
                    context.DrawGeometry(RejectedBrush, null, RightHalf);
                break;
            default:
                if (ShowUnrated)
                    context.DrawGeometry(null, UnratedPen, Heart);
                break;
        }
    }
}

/// <summary>
/// A track's energy as one to three rising bars; unset shows the bars unlit (or nothing).
/// </summary>
public sealed class EnergyIcon : Control
{
    public static readonly StyledProperty<Energy> EnergyProperty =
        AvaloniaProperty.Register<EnergyIcon, Energy>(nameof(Energy));

    /// <summary>Whether an unset energy shows unlit bars (false: nothing).</summary>
    public static readonly StyledProperty<bool> ShowUnsetProperty =
        AvaloniaProperty.Register<EnergyIcon, bool>(nameof(ShowUnset), true);

    private static readonly IBrush Lit = new ImmutableSolidColorBrush(Color.Parse("#C3C9D4"));
    private static readonly IBrush Unlit = new ImmutableSolidColorBrush(Color.Parse("#2F3542"));

    static EnergyIcon() => AffectsRender<EnergyIcon>(EnergyProperty, ShowUnsetProperty);

    public Energy Energy
    {
        get => GetValue(EnergyProperty);
        set => SetValue(EnergyProperty, value);
    }

    public bool ShowUnset
    {
        get => GetValue(ShowUnsetProperty);
        set => SetValue(ShowUnsetProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Energy == Energy.Unset && !ShowUnset)
            return;
        const int bars = 3;
        double gap = Math.Max(1.5, Bounds.Width / 8);
        double width = (Bounds.Width - gap * (bars - 1)) / bars;
        for (int i = 0; i < bars; i++)
        {
            double height = Bounds.Height * (i + 1.5) / (bars + 0.5);
            var rect = new Rect(i * (width + gap), Bounds.Height - height, width, height);
            context.FillRectangle(i < (int)Energy ? Lit : Unlit, rect, 1);
        }
    }
}
