using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace GZJam.App.Controls;

/// <summary>
/// A crossfader drawn like the one on a DJ mixer: tick marks, a dark slot, and a grey cap.
/// Drag the cap, or press anywhere on the track to jump there and keep dragging. Arrow keys
/// nudge it when focused. Works the same with mouse, trackpad, touch and pen.
/// </summary>
public sealed class CrossfaderControl : Control
{
    /// <summary>0 = all the way to Deck 1, 1 = all the way to Deck 2.</summary>
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<CrossfaderControl, double>(nameof(Value), defaultBindingMode: BindingMode.TwoWay,
            coerce: (_, v) => double.IsNaN(v) ? 0 : Math.Clamp(v, 0, 1));

    public static readonly StyledProperty<int> TickCountProperty =
        AvaloniaProperty.Register<CrossfaderControl, int>(nameof(TickCount), 7);

    private const double CapWidth = 24;
    private const double SlotHeight = 6;
    private const double KeyStep = 0.05;

    private static readonly IBrush TickBrush = new SolidColorBrush(Color.Parse("#343A45"));
    private static readonly IBrush CentreTickBrush = new SolidColorBrush(Color.Parse("#4A5160"));
    private static readonly IBrush SlotBrush = new SolidColorBrush(Color.Parse("#050608"));
    private static readonly IBrush SlotEdgeBrush = new SolidColorBrush(Color.Parse("#262B34"));
    private static readonly IBrush ShadowBrush = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0));
    private static readonly IPen CapPen = new Pen(new SolidColorBrush(Color.Parse("#4E535C")), 1);
    private static readonly IBrush IndexBrush = new SolidColorBrush(Color.Parse("#F2F4F8"));
    private static readonly IBrush CapBrush = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse("#7C8089"), 0),
            new GradientStop(Color.Parse("#B4B8C0"), 0.45),
            new GradientStop(Color.Parse("#9EA2AA"), 0.6),
            new GradientStop(Color.Parse("#6C7078"), 1),
        },
    };

    /// <summary>Where the pointer grabbed the cap, relative to its centre, while dragging.</summary>
    private double _grabOffset;

    static CrossfaderControl()
    {
        AffectsRender<CrossfaderControl>(ValueProperty, TickCountProperty, IsEnabledProperty);
        FocusableProperty.OverrideDefaultValue<CrossfaderControl>(true);
        CursorProperty.OverrideDefaultValue<CrossfaderControl>(new Cursor(StandardCursorType.SizeWestEast));
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public int TickCount
    {
        get => GetValue(TickCountProperty);
        set => SetValue(TickCountProperty, value);
    }

    // The cap's centre travels between half a cap-width from each edge.
    private double TravelStart => CapWidth / 2 + 2;
    private double TravelLength => Math.Max(1, Bounds.Width - CapWidth - 4);
    private double SlotY => Bounds.Height * 0.55;

    /// <summary>The cap's centre in this control's coordinates (also used by tests to grab it).</summary>
    internal Point CapCentre => new(TravelStart + Value * TravelLength, SlotY);

    private double CapHeight => Math.Min(Bounds.Height - 6, 60);

    protected override Size MeasureOverride(Size availableSize) => new(160, 64);

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width < CapWidth * 2 || Bounds.Height < 20)
            return;

        double slotY = SlotY;

        // Invisible, but makes the whole area respond to touch and clicks, not just the drawn lines.
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));

        // Tick marks across the travel, the centre one slightly brighter.
        int ticks = Math.Max(2, TickCount);
        double tickTop = 3, tickBottom = Bounds.Height - 3;
        for (int i = 0; i < ticks; i++)
        {
            double x = Math.Round(TravelStart + TravelLength * i / (ticks - 1)) + 0.5;
            IBrush brush = i == ticks / 2 && ticks % 2 == 1 ? CentreTickBrush : TickBrush;
            context.DrawRectangle(brush, null, new Rect(x - 0.5, tickTop, 1, tickBottom - tickTop));
        }

        // The slot the cap rides in, with a faint lower edge for depth.
        var slot = new Rect(4, slotY - SlotHeight / 2, Bounds.Width - 8, SlotHeight);
        context.DrawRectangle(SlotEdgeBrush, null, slot.Translate(new Vector(0, 1)), SlotHeight / 2, SlotHeight / 2);
        context.DrawRectangle(SlotBrush, null, slot, SlotHeight / 2, SlotHeight / 2);

        // The cap: soft shadow, metal-look body, and a grip line across the middle.
        Point centre = CapCentre;
        double capHeight = CapHeight;
        var cap = new Rect(centre.X - CapWidth / 2, centre.Y - capHeight / 2 - 3, CapWidth, capHeight);
        using (context.PushOpacity(IsEnabled ? 1 : 0.4))
        {
            context.DrawRectangle(ShadowBrush, null, cap.Translate(new Vector(0, 2)), 3, 3);
            context.DrawRectangle(CapBrush, CapPen, cap, 3, 3);
            context.DrawRectangle(IndexBrush, null, new Rect(cap.Center.X - 1, cap.Y + 4, 2, cap.Height - 8));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        Point p = e.GetPosition(this);
        Point centre = CapCentre;
        bool onCap = Math.Abs(p.X - centre.X) <= CapWidth / 2 + 6;
        // Grabbing the cap keeps it under the finger; pressing the track jumps the cap there.
        _grabOffset = onCap ? p.X - centre.X : 0;
        if (!onCap)
            Value = ValueAt(p.X);

        e.Pointer.Capture(this);
        Focus();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Pointer.Captured == this)
            Value = ValueAt(e.GetPosition(this).X - _grabOffset);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Pointer.Captured == this)
            e.Pointer.Capture(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Left:
                Value -= KeyStep;
                break;
            case Key.Right:
                Value += KeyStep;
                break;
            case Key.Home:
                Value = 0;
                break;
            case Key.End:
                Value = 1;
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private double ValueAt(double x) => Math.Clamp((x - TravelStart) / TravelLength, 0, 1);
}
