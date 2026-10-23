using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace GZJam.App.Controls;

/// <summary>
/// GZJam's name in the logo's drawn letters (<see cref="LogoShapes"/>), as tall as the control
/// (the letters have no descenders) and as wide as that needs. With the pixels for it, the letters
/// are cut into bars, GZ in Deck 1's cyan; smaller, they're solid, in the text colour.
/// </summary>
public sealed class Wordmark : Control
{
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<Wordmark>();

    private static readonly IBrush PlayedBrush = new ImmutableSolidColorBrush(Color.Parse(LogoShapes.Played));
    private static readonly IBrush ToComeBrush = new ImmutableSolidColorBrush(Color.Parse(LogoShapes.ToCome));
    private static readonly Geometry[] Shapes = [.. LogoShapes.Letters.Select(l => Geometry.Parse(l.Path))];

    static Wordmark()
    {
        AffectsRender<Wordmark>(ForegroundProperty);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double height = double.IsFinite(availableSize.Height) ? availableSize.Height : 14;
        return new Size(height * LogoShapes.WordWidth / LogoShapes.CapHeight, height);
    }

    public override void Render(DrawingContext context)
    {
        double scale = Math.Min(Bounds.Height / LogoShapes.CapHeight, Bounds.Width / LogoShapes.WordWidth);
        if (scale <= 0)
            return;

        using var _ = context.PushTransform(Matrix.CreateScale(scale, scale));
        double pixels = LogoShapes.CapHeight * scale * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        if (pixels >= LogoShapes.WordBarsFromPixels)
        {
            using (context.PushGeometryClip(BarClip()))
                DrawLetters(context, letter => letter.Played ? PlayedBrush : ToComeBrush);
        }
        else
        {
            DrawLetters(context, _ => Foreground);
        }
    }

    private static void DrawLetters(DrawingContext context, Func<LogoShapes.Letter, IBrush?> brushOf)
    {
        for (int i = 0; i < Shapes.Length; i++)
        {
            var letter = LogoShapes.Letters[i];
            IBrush? brush = brushOf(letter);
            using var at = context.PushTransform(Matrix.CreateTranslation(letter.X, 0));
            if (letter.Filled)
                context.DrawGeometry(brush, null, Shapes[i]);
            else
                context.DrawGeometry(null, new Pen(brush, LogoShapes.LetterStroke), Shapes[i]);
        }
    }

    private static GeometryGroup BarClip()
    {
        var group = new GeometryGroup();
        for (int i = 0; i < LogoShapes.WordBars; i++)
            group.Children.Add(new RectangleGeometry(new Rect(i * LogoShapes.WordBarPitch, -1, LogoShapes.WordBarWidth, LogoShapes.CapHeight + 2)));
        return group;
    }
}
