using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace GZJam.App.Controls;

/// <summary>
/// The lines beside a <see cref="FitRow"/>'s divided parts, drawn over it (a panel can't draw its
/// own): the column names' lines to drag (#198), one lit while it's under the pointer or dragged.
/// </summary>
public sealed class FitRowLines : Control
{
    public static readonly StyledProperty<FitRow?> RowProperty =
        AvaloniaProperty.Register<FitRowLines, FitRow?>(nameof(Row));

    public static readonly StyledProperty<IBrush?> BrushProperty =
        AvaloniaProperty.Register<FitRowLines, IBrush?>(nameof(Brush));

    public static readonly StyledProperty<IBrush?> HotBrushProperty =
        AvaloniaProperty.Register<FitRowLines, IBrush?>(nameof(HotBrush));

    /// <summary>The key of the part whose line is lit, if any.</summary>
    public static readonly StyledProperty<string?> HotProperty =
        AvaloniaProperty.Register<FitRowLines, string?>(nameof(Hot));

    static FitRowLines() => AffectsRender<FitRowLines>(BrushProperty, HotBrushProperty, HotProperty);

    public FitRow? Row { get => GetValue(RowProperty); set => SetValue(RowProperty, value); }
    public IBrush? Brush { get => GetValue(BrushProperty); set => SetValue(BrushProperty, value); }
    public IBrush? HotBrush { get => GetValue(HotBrushProperty); set => SetValue(HotBrushProperty, value); }
    public string? Hot { get => GetValue(HotProperty); set => SetValue(HotProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != RowProperty)
            return;
        if (change.OldValue is FitRow old)
            old.Arranged -= OnRowArranged;
        if (change.NewValue is FitRow row)
            row.Arranged += OnRowArranged;
        InvalidateVisual();
    }

    private void OnRowArranged(object? sender, EventArgs e) => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        if (Row is not { } row || Brush is not { } brush || row.TranslatePoint(default, this) is not { } origin)
            return;
        foreach (var part in row.Children.Where(FitRow.GetDivided))
        {
            if (row.DividerOf(part) is not { } line)
                continue;
            bool hot = Hot is { } key && FitRow.GetKey(part) == key;
            context.FillRectangle(hot && HotBrush is { } hotBrush ? hotBrush : brush,
                new Rect(Math.Round(origin.X + line.X), origin.Y, 1, row.Bounds.Height));
        }
    }
}
