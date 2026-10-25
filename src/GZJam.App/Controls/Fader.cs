using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace GZJam.App.Controls;

/// <summary>
/// An upright fader drawn like the hardware: a ladder of lines, a slot, and a metal cap. The slot
/// lights from <see cref="Origin"/> to the cap: on an EQ band from 0 dB, so a cut reads as a cut
/// and a boost as a boost; on a volume fader from the bottom. Drag the cap, or press anywhere to
/// jump there; double-tap for <see cref="ResetValue"/> if there is one. Arrow keys and the mouse
/// wheel move it one <see cref="Step"/>.
/// </summary>
public sealed class Fader : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<Fader, double>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<Fader, double>(nameof(Minimum), -24);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<Fader, double>(nameof(Maximum), 6);

    public static readonly StyledProperty<double> StepProperty =
        AvaloniaProperty.Register<Fader, double>(nameof(Step), 0.5);

    /// <summary>Where the lit part of the slot starts: 0 dB on an EQ, the bottom on a volume fader.</summary>
    public static readonly StyledProperty<double> OriginProperty =
        AvaloniaProperty.Register<Fader, double>(nameof(Origin));

    /// <summary>Values to draw ladder lines at; the line at <see cref="Origin"/> is brighter.</summary>
    public static readonly StyledProperty<AvaloniaList<double>?> LinesProperty =
        AvaloniaProperty.Register<Fader, AvaloniaList<double>?>(nameof(Lines));

    /// <summary>Double-tap goes here; null: double-tap does nothing (a volume fader shouldn't jump to full).</summary>
    public static readonly StyledProperty<double?> ResetValueProperty =
        AvaloniaProperty.Register<Fader, double?>(nameof(ResetValue));

    /// <summary>The color of the lit slot, and of the ladder lines it passes (if <see cref="LightLines"/>).</summary>
    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<Fader, IBrush?>(nameof(Accent));

    public static readonly StyledProperty<bool> LightLinesProperty =
        AvaloniaProperty.Register<Fader, bool>(nameof(LightLines), true);

    private const double CapWidth = 34;
    private const double CapHeight = 16;
    private const double SlotWidth = 4;

    private static readonly IBrush LineBrush = new SolidColorBrush(Color.Parse("#2A303B"));
    private static readonly IBrush OriginBrush = new SolidColorBrush(Color.Parse("#7A8292"));
    private static readonly IBrush SlotBrush = new SolidColorBrush(Color.Parse("#050608"));
    private static readonly IBrush SlotEdgeBrush = new SolidColorBrush(Color.Parse("#262B34"));
    private static readonly IBrush DefaultLitBrush = new SolidColorBrush(Color.Parse("#4A5160"));
    private static readonly IBrush ShadowBrush = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0));
    private static readonly IPen CapPen = new Pen(new SolidColorBrush(Color.Parse("#4E535C")), 1);
    private static readonly IBrush IndexBrush = new SolidColorBrush(Color.Parse("#F2F4F8"));
    internal static readonly IBrush CapBrush = new LinearGradientBrush
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

    /// <summary>Where the pointer grabbed the cap, relative to its center, while dragging.</summary>
    private double _grabOffset;

    static Fader()
    {
        AffectsRender<Fader>(ValueProperty, MinimumProperty, MaximumProperty, OriginProperty, LinesProperty,
            AccentProperty, LightLinesProperty, IsEnabledProperty);
        FocusableProperty.OverrideDefaultValue<Fader>(true);
        CursorProperty.OverrideDefaultValue<Fader>(new Cursor(StandardCursorType.SizeNorthSouth));
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

    public double Step
    {
        get => GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public double Origin
    {
        get => GetValue(OriginProperty);
        set => SetValue(OriginProperty, value);
    }

    public AvaloniaList<double>? Lines
    {
        get => GetValue(LinesProperty);
        set => SetValue(LinesProperty, value);
    }

    public double? ResetValue
    {
        get => GetValue(ResetValueProperty);
        set => SetValue(ResetValueProperty, value);
    }

    public IBrush? Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public bool LightLines
    {
        get => GetValue(LightLinesProperty);
        set => SetValue(LightLinesProperty, value);
    }

    // The cap's center travels between half a cap-height from the top and bottom.
    private const double TravelTop = CapHeight / 2 + 2;
    private double TravelLength => Math.Max(1, Bounds.Height - CapHeight - 4);

    /// <summary>The height of a level: the top is the maximum.</summary>
    internal double YOf(double value) => LevelY(value, Bounds.Height, Minimum, Maximum);

    /// <summary>Where a level sits on a fader of this height (shared with <see cref="EqScale"/> so the labels line up).</summary>
    internal static double LevelY(double value, double height, double minimum, double maximum)
    {
        double range = Math.Max(0.001, maximum - minimum), travel = Math.Max(1, height - CapHeight - 4);
        return TravelTop + (maximum - Math.Clamp(value, minimum, maximum)) / range * travel;
    }

    private double ValueAt(double y)
    {
        double value = Maximum - (y - TravelTop) / TravelLength * (Maximum - Minimum);
        return Snap(value);
    }

    private double Snap(double value)
    {
        double step = Step > 0 ? Step : 1;
        return Math.Clamp(Math.Round(value / step) * step, Minimum, Maximum);
    }

    /// <summary>The cap's center in this control's coordinates (also used by tests to grab it).</summary>
    internal Point CapCenter => new(Bounds.Width / 2, YOf(Value));

    protected override Size MeasureOverride(Size availableSize) => new(44, 120);

    public override void Render(DrawingContext context)
    {
        if (Bounds.Height < CapHeight * 2)
            return;
        double width = Bounds.Width, mid = width / 2;

        // Invisible, but makes the whole column respond to touch and clicks.
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));

        double originY = YOf(Origin), capY = YOf(Value);
        double low = Math.Min(originY, capY), high = Math.Max(originY, capY);
        IBrush lit = Accent ?? DefaultLitBrush;
        IEnumerable<double> lines = Lines ?? DefaultLines();
        foreach (double line in lines)
        {
            double y = Math.Round(YOf(line)) + 0.5;
            bool origin = Math.Abs(line - Origin) < 0.001 && Origin > Minimum;
            bool isLit = LightLines && !origin && Value != Origin && y >= low - 0.5 && y <= high + 0.5;
            double inset = origin ? 2 : 7;
            IBrush brush = origin ? OriginBrush : isLit ? lit : LineBrush;
            double thickness = origin || isLit ? 2 : 1;
            context.DrawRectangle(brush, null, new Rect(inset, y - thickness / 2, width - inset * 2, thickness));
        }

        // The slot the cap rides in, lit from the origin to the cap.
        var slot = new Rect(mid - SlotWidth / 2, TravelTop - 2, SlotWidth, TravelLength + 4);
        context.DrawRectangle(SlotEdgeBrush, null, slot.Translate(new Vector(1, 0)), 2, 2);
        context.DrawRectangle(SlotBrush, null, slot, 2, 2);
        if (high - low >= 1)
            context.DrawRectangle(lit, null, new Rect(mid - SlotWidth / 2, low, SlotWidth, high - low));

        // The cap: shadow, metal-look body, and an index line marking the level.
        var cap = new Rect(mid - CapWidth / 2, capY - CapHeight / 2, CapWidth, CapHeight);
        using (context.PushOpacity(IsEnabled ? 1 : 0.4))
        {
            context.DrawRectangle(ShadowBrush, null, cap.Translate(new Vector(0, 2)), 3, 3);
            context.DrawRectangle(CapBrush, CapPen, cap, 3, 3);
            context.DrawRectangle(IndexBrush, null, new Rect(cap.X + 3, capY - 1, CapWidth - 6, 2));
        }
    }

    /// <summary>Without <see cref="Lines"/>: about ten, at round steps.</summary>
    private IEnumerable<double> DefaultLines()
    {
        double every = (Maximum - Minimum) / 10;
        for (double v = Math.Ceiling(Minimum / every) * every; v <= Maximum + 0.001; v += every)
            yield return v;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        Point p = e.GetPosition(this);
        double capY = CapCenter.Y;
        // Grabbing the cap keeps it under the pointer; a mouse click elsewhere jumps it there. A
        // finger or pen never jumps it (a slightly missed cap mustn't swing the level): it moves
        // the cap from where it is, by as much as the finger moves.
        bool onCap = e.Pointer.Type != PointerType.Mouse || Math.Abs(p.Y - capY) <= CapHeight / 2 + 6;
        _grabOffset = onCap ? p.Y - capY : 0;
        if (e.ClickCount == 2 && ResetValue is { } reset)
            Value = reset;
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
                Nudge(Step * 6);
                break;
            case Key.PageDown:
                Nudge(-Step * 6);
                break;
            case Key.Home when ResetValue is { } reset:
                Value = reset;
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void Nudge(double amount) => Value = Snap(Value + amount);
}

/// <summary>The dB labels beside a row of <see cref="Fader"/>s of the same height: every 6 dB.</summary>
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
            double y = Fader.LevelY(db, Bounds.Height, Minimum, Maximum);
            context.DrawText(label, new Point(Bounds.Width - label.Width - 2, y - label.Height / 2));
        }
    }
}
