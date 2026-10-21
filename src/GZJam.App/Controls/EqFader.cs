using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace GZJam.App.Controls;

/// <summary>
/// One band of a graphic EQ, drawn like the hardware: a ladder of lines every 3 dB, a slot, and
/// a fader cap. The lines between 0 dB and the cap light up, so a cut reads as a cut and a boost
/// as a boost. Drag the cap, or press anywhere to jump there; double-tap for 0 dB. Arrow keys
/// and the mouse wheel move it half a dB.
/// </summary>
public sealed class EqFader : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<EqFader, double>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<EqFader, double>(nameof(Minimum), -24);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<EqFader, double>(nameof(Maximum), 6);

    /// <summary>The colour of the lit lines between 0 dB and the cap.</summary>
    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<EqFader, IBrush?>(nameof(Accent));

    /// <summary>dB between ladder lines.</summary>
    public const double LineEvery = 3;

    private const double Step = 0.5;
    private const double CapWidth = 34;
    private const double CapHeight = 16;
    private const double SlotWidth = 4;

    private static readonly IBrush LineBrush = new SolidColorBrush(Color.Parse("#2A303B"));
    private static readonly IBrush ZeroBrush = new SolidColorBrush(Color.Parse("#7A8292"));
    private static readonly IBrush SlotBrush = new SolidColorBrush(Color.Parse("#050608"));
    private static readonly IBrush SlotEdgeBrush = new SolidColorBrush(Color.Parse("#262B34"));
    private static readonly IBrush ShadowBrush = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0));
    private static readonly IPen CapPen = new Pen(new SolidColorBrush(Color.Parse("#4E535C")), 1);
    private static readonly IBrush IndexBrush = new SolidColorBrush(Color.Parse("#F2F4F8"));
    private static readonly IBrush CapBrush = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse("#B4B8C0"), 0),
            new GradientStop(Color.Parse("#8E929A"), 0.5),
            new GradientStop(Color.Parse("#62666E"), 1),
        },
    };

    /// <summary>Where the pointer grabbed the cap, relative to its centre, while dragging.</summary>
    private double _grabOffset;

    static EqFader()
    {
        AffectsRender<EqFader>(ValueProperty, MinimumProperty, MaximumProperty, AccentProperty, IsEnabledProperty);
        FocusableProperty.OverrideDefaultValue<EqFader>(true);
        CursorProperty.OverrideDefaultValue<EqFader>(new Cursor(StandardCursorType.SizeNorthSouth));
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public IBrush? Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    // The cap's centre travels between half a cap-height from the top and bottom.
    private const double TravelTop = CapHeight / 2 + 2;
    private double TravelLength => Math.Max(1, Bounds.Height - CapHeight - 4);
    private double Range => Math.Max(Step, Maximum - Minimum);

    /// <summary>The height of a level: the top is the maximum boost.</summary>
    internal double YOf(double db) => LevelY(db, Bounds.Height, Minimum, Maximum);

    /// <summary>Where a level sits on a fader of this height (shared with <see cref="EqScale"/> so the labels line up).</summary>
    internal static double LevelY(double db, double height, double minimum, double maximum)
    {
        double range = Math.Max(Step, maximum - minimum), travel = Math.Max(1, height - CapHeight - 4);
        return TravelTop + (maximum - Math.Clamp(db, minimum, maximum)) / range * travel;
    }

    private double ValueAt(double y) =>
        Math.Round(Math.Clamp(Maximum - (y - TravelTop) / TravelLength * Range, Minimum, Maximum) / Step) * Step;

    /// <summary>The cap's centre in this control's coordinates (also used by tests to grab it).</summary>
    internal Point CapCentre => new(Bounds.Width / 2, YOf(Value));

    protected override Size MeasureOverride(Size availableSize) => new(48, 150);

    public override void Render(DrawingContext context)
    {
        if (Bounds.Height < CapHeight * 2)
            return;
        double width = Bounds.Width, mid = width / 2;

        // Invisible, but makes the whole column respond to touch and clicks.
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));

        // The ladder: a line every 3 dB, 0 dB longer and brighter; the lines between 0 dB and the
        // cap lit in the accent colour.
        double zeroY = YOf(0), capY = YOf(Value);
        double low = Math.Min(zeroY, capY), high = Math.Max(zeroY, capY);
        IBrush lit = Accent ?? Brushes.MediumPurple;
        for (double db = Math.Ceiling(Minimum / LineEvery) * LineEvery; db <= Maximum + 0.001; db += LineEvery)
        {
            double y = Math.Round(YOf(db)) + 0.5;
            bool zero = Math.Abs(db) < 0.001;
            bool isLit = Value != 0 && y >= low - 0.5 && y <= high + 0.5 && !zero;
            double inset = zero ? 2 : 7;
            IBrush brush = zero ? ZeroBrush : isLit ? lit : LineBrush;
            double thickness = zero || isLit ? 2 : 1;
            context.DrawRectangle(brush, null, new Rect(inset, y - thickness / 2, width - inset * 2, thickness));
        }

        // The slot the cap rides in.
        var slot = new Rect(mid - SlotWidth / 2, TravelTop - 2, SlotWidth, TravelLength + 4);
        context.DrawRectangle(SlotEdgeBrush, null, slot.Translate(new Vector(1, 0)), 2, 2);
        context.DrawRectangle(SlotBrush, null, slot, 2, 2);
        if (high - low >= 1)
            context.DrawRectangle(lit, null, new Rect(mid - SlotWidth / 2, low, SlotWidth, high - low));   // the slot lit from 0 dB to the cap

        // The cap: shadow, metal-look body, and an index line marking the level.
        var cap = new Rect(mid - CapWidth / 2, capY - CapHeight / 2, CapWidth, CapHeight);
        using (context.PushOpacity(IsEnabled ? 1 : 0.4))
        {
            context.DrawRectangle(ShadowBrush, null, cap.Translate(new Vector(0, 2)), 3, 3);
            context.DrawRectangle(CapBrush, CapPen, cap, 3, 3);
            context.DrawRectangle(IndexBrush, null, new Rect(cap.X + 3, capY - 1, CapWidth - 6, 2));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        Point p = e.GetPosition(this);
        double capY = CapCentre.Y;
        bool onCap = Math.Abs(p.Y - capY) <= CapHeight / 2 + 6;
        // Grabbing the cap keeps it under the finger; pressing elsewhere jumps it there.
        _grabOffset = onCap ? p.Y - capY : 0;
        if (e.ClickCount == 2)
            Value = 0;   // double-tap: flat
        else if (!onCap)
            Value = ValueAt(p.Y);
        e.Pointer.Capture(this);
        Focus();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Pointer.Captured == this)
            Value = ValueAt(e.GetPosition(this).Y - _grabOffset);
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
        Nudge(Math.Sign(e.Delta.Y) * Step);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Up:
                Nudge(Step);
                break;
            case Key.Down:
                Nudge(-Step);
                break;
            case Key.PageUp:
                Nudge(LineEvery);
                break;
            case Key.PageDown:
                Nudge(-LineEvery);
                break;
            case Key.Home:
                Value = 0;
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void Nudge(double db) => Value = Math.Clamp(Value + db, Minimum, Maximum);
}

/// <summary>The dB labels beside a row of <see cref="EqFader"/>s of the same height: every 6 dB.</summary>
public sealed class EqScale : Control
{
    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<EqScale, double>(nameof(Minimum), -24);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<EqScale, double>(nameof(Maximum), 6);

    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.Parse("#5D6574"));
    private static readonly IBrush ZeroLabelBrush = new SolidColorBrush(Color.Parse("#8B94A5"));

    static EqScale() => AffectsRender<EqScale>(MinimumProperty, MaximumProperty);

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(26, 150);

    public override void Render(DrawingContext context)
    {
        var typeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
        for (double db = Math.Ceiling(Minimum / 6) * 6; db <= Maximum + 0.001; db += 6)
        {
            string text = db > 0 ? $"+{db:0}" : db < 0 ? $"−{-db:0}" : "0";
            var label = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, 10, db == 0 ? ZeroLabelBrush : LabelBrush);
            double y = EqFader.LevelY(db, Bounds.Height, Minimum, Maximum);
            context.DrawText(label, new Point(Bounds.Width - label.Width - 2, y - label.Height / 2));
        }
    }
}
