using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace GZJam.App.Controls;

/// <summary>
/// GZJam's mark: a waveform wrapped into a G (<see cref="LogoShapes"/>), filling the control's
/// square. Where there are too few pixels for its bars, the ring is drawn solid, so it stays a G.
/// </summary>
public sealed class LogoMark : Control
{
    private static readonly ImmutableSolidColorBrush PlayedBrush = new(Color.Parse(LogoShapes.Played));
    private static readonly ImmutableSolidColorBrush ToComeBrush = new(Color.Parse(LogoShapes.ToCome));
    private static readonly IPen PlayedBar = new ImmutablePen(PlayedBrush, LogoShapes.BarWidth);
    private static readonly IPen ToComeBar = new ImmutablePen(ToComeBrush, LogoShapes.BarWidth);
    private static readonly IPen GBar = new ImmutablePen(PlayedBrush, LogoShapes.GBarWidth);
    private static readonly IPen SolidPlayed = new ImmutablePen(PlayedBrush, LogoShapes.SolidRingWidth);
    private static readonly IPen SolidToCome = new ImmutablePen(ToComeBrush, LogoShapes.SolidRingWidth);
    private static readonly IPen SolidGBar = new ImmutablePen(PlayedBrush, LogoShapes.SolidGBarWidth);

    public override void Render(DrawingContext context)
    {
        double side = Math.Min(Bounds.Width, Bounds.Height);
        if (side <= 0)
            return;

        double scale = side / LogoShapes.MarkSize;
        var origin = new Point((Bounds.Width - side) / 2, (Bounds.Height - side) / 2);
        using var _ = context.PushTransform(
            Matrix.CreateTranslation(-LogoShapes.MarkLeft, -LogoShapes.MarkLeft) *
            Matrix.CreateScale(scale, scale) *
            Matrix.CreateTranslation(origin.X, origin.Y));

        double pixels = side * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        if (pixels >= LogoShapes.MarkBarsFromPixels)
        {
            foreach (var bar in LogoShapes.Bars)
                context.DrawLine(bar.Played ? PlayedBar : ToComeBar, new Point(bar.X1, bar.Y1), new Point(bar.X2, bar.Y2));
            context.DrawLine(GBar, new Point(LogoShapes.GBarFrom, LogoShapes.Center), new Point(LogoShapes.GBarTo, LogoShapes.Center));
        }
        else
        {
            double half = LogoShapes.BarStep / 2;
            context.DrawGeometry(null, SolidPlayed, Arc(LogoShapes.RingStart - half, LogoShapes.PlayedTo + half));
            context.DrawGeometry(null, SolidToCome, Arc(LogoShapes.PlayedTo + half, LogoShapes.RingEnd + half));
            context.DrawLine(SolidGBar, new Point(LogoShapes.GBarFrom, LogoShapes.Center), new Point(LogoShapes.GBarTo, LogoShapes.Center));
        }
    }

    /// <summary>Part of the ring, clockwise from one angle to another (0° is 3 o'clock).</summary>
    private static StreamGeometry Arc(double fromDegrees, double toDegrees)
    {
        static Point At(double degrees) => new(
            LogoShapes.Center + LogoShapes.Ring * Math.Cos(degrees * Math.PI / 180),
            LogoShapes.Center + LogoShapes.Ring * Math.Sin(degrees * Math.PI / 180));

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(At(fromDegrees), isFilled: false);
            g.ArcTo(At(toDegrees), new Size(LogoShapes.Ring, LogoShapes.Ring), 0,
                    isLargeArc: toDegrees - fromDegrees > 180, SweepDirection.Clockwise);
            g.EndFigure(isClosed: false);
        }
        return geometry;
    }
}
