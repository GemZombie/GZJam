using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace GZJam.App.Controls;

/// <summary>
/// A small icon drawn from a shape (a geometry), as <c>Path</c> and <c>PathIcon</c> draw one, but
/// without holding on to it (#407). A <c>Path</c> subscribes to its shape's changes, and an icon's
/// shape is shared by every row that shows it (a resource, or a style's value), so the shape kept
/// every row ever made, with its menu: rows of the folder tree, made again as folders open, close
/// and scroll by, piled up to 300 MB over a night. Icon shapes never change, so this one just
/// draws it, fitted inside itself (as <c>Stretch="Uniform"</c>), centered, with the outline as thick
/// as asked whatever the size.
/// </summary>
public sealed class ShapeIcon : Control
{
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<ShapeIcon, Geometry?>(nameof(Data));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<ShapeIcon, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<ShapeIcon, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<ShapeIcon, double>(nameof(StrokeThickness));

    public static readonly StyledProperty<PenLineJoin> StrokeJoinProperty =
        AvaloniaProperty.Register<ShapeIcon, PenLineJoin>(nameof(StrokeJoin));

    public static readonly StyledProperty<PenLineCap> StrokeLineCapProperty =
        AvaloniaProperty.Register<ShapeIcon, PenLineCap>(nameof(StrokeLineCap));

    /// <summary>The shape, in any units: it's fitted to the icon's size.</summary>
    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>The outline's thickness on the screen, whatever the shape's own size.</summary>
    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public PenLineJoin StrokeJoin
    {
        get => GetValue(StrokeJoinProperty);
        set => SetValue(StrokeJoinProperty, value);
    }

    public PenLineCap StrokeLineCap
    {
        get => GetValue(StrokeLineCapProperty);
        set => SetValue(StrokeLineCapProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // Not AffectsRender: that would listen to the shape and the brushes, holding on to this
        // icon from them, which is what this control is for not doing.
        if (change.Property == DataProperty || change.Property == FillProperty || change.Property == StrokeProperty
            || change.Property == StrokeThicknessProperty || change.Property == StrokeJoinProperty
            || change.Property == StrokeLineCapProperty)
            InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (Data is not { } shape || (Fill is null && Stroke is null))
            return;
        Rect bounds = shape.Bounds;
        double stroke = Stroke is null ? 0 : StrokeThickness;
        // The outline goes half outside the shape: room is left for it.
        double width = Bounds.Width - stroke, height = Bounds.Height - stroke;
        if (width <= 0 || height <= 0)
            return;
        double scale = Math.Min(
            bounds.Width > 0 ? width / bounds.Width : double.PositiveInfinity,
            bounds.Height > 0 ? height / bounds.Height : double.PositiveInfinity);
        if (double.IsInfinity(scale))
            return;
        double x = (Bounds.Width - bounds.Width * scale) / 2 - bounds.X * scale;
        double y = (Bounds.Height - bounds.Height * scale) / 2 - bounds.Y * scale;
        IPen? pen = Stroke is { } brush && stroke > 0
            ? new Pen(brush, stroke / scale, lineCap: StrokeLineCap, lineJoin: StrokeJoin)
            : null;
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(x, y)))
            context.DrawGeometry(Fill, pen, shape);
    }
}
