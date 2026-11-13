using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using GZJam.Audio;

namespace GZJam.App.Controls;

/// <summary>
/// What the room gets from its EQ (#272), from 20 Hz to 20 kHz: the same curve in either view,
/// three bands or ten, worked out the way the speakers' filters work (<see cref="RoomEq.ResponseDb"/>).
/// </summary>
public sealed class RoomEqCurve : Control
{
    public static readonly StyledProperty<RoomEq?> EqProperty =
        AvaloniaProperty.Register<RoomEqCurve, RoomEq?>(nameof(Eq));

    private const double LowestHz = 20, HighestHz = 20000;
    private const double TopDb = RoomEq.MaxDb + 1, BottomDb = RoomEq.MinDb - 1;

    private static readonly IBrush GridBrush = new SolidColorBrush(Color.Parse("#262B34"));
    private static readonly IBrush ZeroBrush = new SolidColorBrush(Color.Parse("#3A4150"));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.Parse("#5D6574"));
    private static readonly IBrush FlatBrush = new SolidColorBrush(Color.Parse("#5D6574"));

    static RoomEqCurve() => AffectsRender<RoomEqCurve>(EqProperty);

    public RoomEq? Eq
    {
        get => GetValue(EqProperty);
        set => SetValue(EqProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(240, 120);

    private double X(double hz) => Math.Log(hz / LowestHz) / Math.Log(HighestHz / LowestHz) * Bounds.Width;

    private double Y(double db) => (TopDb - Math.Clamp(db, BottomDb, TopDb)) / (TopDb - BottomDb) * Bounds.Height;

    public override void Render(DrawingContext context)
    {
        double width = Bounds.Width, height = Bounds.Height;
        if (width <= 0 || height <= 0)
            return;
        var typeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
        FormattedText Label(string text) =>
            new(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 9.5, LabelBrush);

        foreach (double hz in new[] { 100.0, 1000, 10000 })
        {
            double x = X(hz);
            context.DrawLine(new Pen(GridBrush, 1), new Point(x, 0), new Point(x, height));
            var label = Label(hz >= 1000 ? $"{hz / 1000:0}k" : $"{hz:0}");
            context.DrawText(label, new Point(x + 3, height - label.Height - 1));
        }
        foreach (double db in new[] { RoomEq.MaxDb, -6.0, RoomEq.MinDb })
        {
            context.DrawLine(new Pen(GridBrush, 1), new Point(0, Y(db)), new Point(width, Y(db)));
            var label = Label(db > 0 ? $"+{db:0}" : $"−{-db:0}");
            context.DrawText(label, new Point(2, Y(db) - label.Height - 1));
        }
        context.DrawLine(new Pen(ZeroBrush, 1), new Point(0, Y(0)), new Point(width, Y(0)));

        RoomEq eq = Eq ?? RoomEq.Flat;
        if (eq.IsFlat)
        {
            context.DrawLine(new Pen(FlatBrush, 2), new Point(0, Y(0)), new Point(width, Y(0)));
            return;
        }
        IBrush accent = this.TryFindResource("AccentBrush", out object? found) && found is IBrush brush ? brush : Brushes.DeepSkyBlue;
        var line = new StreamGeometry();
        var fill = new StreamGeometry();
        using (StreamGeometryContext l = line.Open(), f = fill.Open())
        {
            f.BeginFigure(new Point(0, Y(0)), isFilled: true);
            const int steps = 160;
            for (int i = 0; i <= steps; i++)
            {
                double hz = LowestHz * Math.Pow(HighestHz / LowestHz, (double)i / steps);
                var point = new Point(X(hz), Y(eq.ResponseDb(hz)));
                if (i == 0)
                    l.BeginFigure(point, isFilled: false);
                else
                    l.LineTo(point);
                f.LineTo(point);
            }
            f.LineTo(new Point(width, Y(0)));
            f.EndFigure(true);
            l.EndFigure(false);
        }
        context.DrawGeometry(new SolidColorBrush(Colors.White, 0.06), null, fill);
        context.DrawGeometry(null, new Pen(accent, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), line);
    }
}
