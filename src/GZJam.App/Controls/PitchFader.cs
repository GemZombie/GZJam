using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace GZJam.App.Controls;

/// <summary>
/// A deck's tempo, drawn like a turntable's pitch fader: a tick every 1%, longer ones at 0 and
/// ±4%, labels underneath if there's room, and a centre notch that lights when the tempo is
/// exactly 0. Dragging is relative: pressing beside the cap never jumps the tempo (the room may
/// be dancing to it). Double-tap for 0; arrow keys and the mouse wheel move it 0.1%.
/// </summary>
public sealed class PitchFader : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<PitchFader, double>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>The fader's reach either side of 0, in percent.</summary>
    public static readonly StyledProperty<double> RangeProperty =
        AvaloniaProperty.Register<PitchFader, double>(nameof(Range), 8);

    private const double Step = 0.1;
    private const double CapWidth = 18;
    private const double SlotHeight = 6;
    private const double LabelSpace = 13;

    private static readonly IBrush TickBrush = new SolidColorBrush(Color.Parse("#2A303B"));
    private static readonly IBrush MajorTickBrush = new SolidColorBrush(Color.Parse("#4A5160"));
    private static readonly IBrush CentreTickBrush = new SolidColorBrush(Color.Parse("#7A8292"));
    private static readonly IBrush SlotBrush = new SolidColorBrush(Color.Parse("#050608"));
    private static readonly IBrush SlotEdgeBrush = new SolidColorBrush(Color.Parse("#262B34"));
    private static readonly IBrush CentreLitBrush = new SolidColorBrush(Color.Parse("#34D399"));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.Parse("#5D6574"));
    private static readonly IBrush CentreLabelBrush = new SolidColorBrush(Color.Parse("#8B94A5"));
    private static readonly IBrush ShadowBrush = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0));
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

    private double _dragFrom;
    private double _valueFrom;

    static PitchFader()
    {
        AffectsRender<PitchFader>(ValueProperty, RangeProperty, IsEnabledProperty);
        FocusableProperty.OverrideDefaultValue<PitchFader>(true);
        CursorProperty.OverrideDefaultValue<PitchFader>(new Cursor(StandardCursorType.SizeWestEast));
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Range
    {
        get => GetValue(RangeProperty);
        set => SetValue(RangeProperty, value);
    }

    private bool ShowLabels => Bounds.Height >= 40;
    private double TravelStart => CapWidth / 2 + 2;
    private double TravelLength => Math.Max(1, Bounds.Width - CapWidth - 4);
    private double SlotY => (ShowLabels ? Bounds.Height - LabelSpace : Bounds.Height) / 2;

    /// <summary>Where a tempo sits along the fader.</summary>
    internal double XOf(double percent) => TravelStart + (Math.Clamp(percent, -Range, Range) + Range) / (2 * Range) * TravelLength;

    protected override Size MeasureOverride(Size availableSize) => new(120, 44);

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width < CapWidth * 3 || Bounds.Height < 16)
            return;
        double slotY = SlotY, reach = Math.Min(slotY - 2, 16);
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));

        // A tick every 1%; longer at 0 and every 4%.
        int range = (int)Math.Round(Range);
        for (int p = -range; p <= range; p++)
        {
            double x = Math.Round(XOf(p)) + 0.5;
            bool centre = p == 0, major = p % 4 == 0;
            double half = centre || major ? reach : reach * 0.6;
            IBrush brush = centre ? CentreTickBrush : major ? MajorTickBrush : TickBrush;
            context.DrawRectangle(brush, null, new Rect(x - 0.5, slotY - half, 1, half * 2));
        }
        if (ShowLabels)
        {
            var typeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
            for (int p = -range; p <= range; p += 4)
            {
                string text = p > 0 ? $"+{p}" : p < 0 ? $"−{-p}" : "0";
                var label = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    typeface, 9.5, p == 0 ? CentreLabelBrush : LabelBrush);
                context.DrawText(label, new Point(XOf(p) - label.Width / 2, Bounds.Height - label.Height));
            }
        }

        // The slot, with the centre notch lit while the tempo is exactly 0.
        var slot = new Rect(2, slotY - SlotHeight / 2, Bounds.Width - 4, SlotHeight);
        context.DrawRectangle(SlotEdgeBrush, null, slot.Translate(new Vector(0, 1)), SlotHeight / 2, SlotHeight / 2);
        context.DrawRectangle(SlotBrush, null, slot, SlotHeight / 2, SlotHeight / 2);
        if (Math.Abs(Value) < Step / 2)
            context.DrawRectangle(CentreLitBrush, null, new Rect(XOf(0) - 1, slotY - SlotHeight / 2, 2, SlotHeight));

        // The cap.
        double capX = XOf(Value), capHeight = Math.Min(reach * 2 + 4, 30);
        var cap = new Rect(capX - CapWidth / 2, slotY - capHeight / 2, CapWidth, capHeight);
        using (context.PushOpacity(IsEnabled ? 1 : 0.4))
        {
            context.DrawRectangle(ShadowBrush, null, cap.Translate(new Vector(0, 2)), 3, 3);
            context.DrawRectangle(CapBrush, CapPen, cap, 3, 3);
            context.DrawRectangle(IndexBrush, null, new Rect(capX - 1, cap.Y + 3, 2, capHeight - 6));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        if (e.ClickCount == 2)
            Value = 0;   // double-tap: back to the record's own tempo
        _dragFrom = e.GetPosition(this).X;
        _valueFrom = Value;
        e.Pointer.Capture(this);
        Focus();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Pointer.Captured != this)
            return;
        // Relative: the cap moves as far as the pointer does, wherever the press began.
        double moved = (e.GetPosition(this).X - _dragFrom) / TravelLength * 2 * Range;
        Value = Snap(_valueFrom + moved);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Pointer.Captured == this)
            e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!IsEnabled || e.Delta.Y == 0)
            return;
        Value = Snap(Value + Math.Sign(e.Delta.Y) * Step);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        double? to = e.Key switch
        {
            Key.Left => Value - Step,
            Key.Right => Value + Step,
            Key.PageDown => Value - 1,
            Key.PageUp => Value + 1,
            Key.Home => 0,
            _ => null,
        };
        if (to is not { } value)
            return;
        Value = Snap(value);
        e.Handled = true;
    }

    private double Snap(double value) => Math.Clamp(Math.Round(value / Step) * Step, -Range, Range);
}
