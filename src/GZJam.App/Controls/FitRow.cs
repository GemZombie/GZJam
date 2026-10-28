using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace GZJam.App.Controls;

/// <summary>
/// Parts in a row that always fits, however narrow (#187): one part (<see cref="FillsProperty"/>,
/// say a song's title) takes the room left over, and when there isn't enough, the others give way
/// in their <see cref="DropOrderProperty"/>: 1 first, then 2, and so on (parts with the same number
/// go together, parts with none stay). Nothing is drawn over its neighbor; a part given way is
/// moved out of sight past the row's end, as <see cref="PriorityRow"/> does with a song's labels.
/// <para>
/// Parts with a <see cref="KeyProperty"/> can be put in another <see cref="OrderProperty"/> and
/// given other <see cref="WidthsProperty"/> (#188): both inherited, so set once on a list they
/// reach each of its rows, and its column names, alike.
/// </para>
/// </summary>
public sealed class FitRow : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<FitRow, double>(nameof(Spacing), 6);

    /// <summary>
    /// The least the filling part keeps before the others give way to it; unset, the room it needs.
    /// A song row sets it, so a short title doesn't keep parts that every other row gives up.
    /// </summary>
    public static readonly StyledProperty<double> FillMinWidthProperty =
        AvaloniaProperty.Register<FitRow, double>(nameof(FillMinWidth), double.NaN);

    public static readonly AttachedProperty<bool> FillsProperty =
        AvaloniaProperty.RegisterAttached<FitRow, Control, bool>("Fills");

    public static readonly AttachedProperty<int> DropOrderProperty =
        AvaloniaProperty.RegisterAttached<FitRow, Control, int>("DropOrder");

    /// <summary>A part's name, for <see cref="OrderProperty"/> and <see cref="WidthsProperty"/>.</summary>
    public static readonly AttachedProperty<string?> KeyProperty =
        AvaloniaProperty.RegisterAttached<FitRow, Control, string?>("Key");

    /// <summary>The named parts in the order they're shown; ones it doesn't name keep their places.</summary>
    public static readonly AttachedProperty<IReadOnlyList<string>?> OrderProperty =
        AvaloniaProperty.RegisterAttached<FitRow, Control, IReadOnlyList<string>?>("Order", inherits: true);

    /// <summary>Widths for named parts, margins included, in place of the room they'd take.</summary>
    public static readonly AttachedProperty<IReadOnlyDictionary<string, double>?> WidthsProperty =
        AvaloniaProperty.RegisterAttached<FitRow, Control, IReadOnlyDictionary<string, double>?>("Widths", inherits: true);

    private readonly HashSet<Control> _dropped = [];

    static FitRow()
    {
        AffectsMeasure<FitRow>(SpacingProperty, FillMinWidthProperty, OrderProperty, WidthsProperty);
        AffectsParentMeasure<FitRow>(FillsProperty, DropOrderProperty, KeyProperty);
    }

    public double Spacing { get => GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }
    public double FillMinWidth { get => GetValue(FillMinWidthProperty); set => SetValue(FillMinWidthProperty, value); }

    public static bool GetFills(Control control) => control.GetValue(FillsProperty);
    public static void SetFills(Control control, bool value) => control.SetValue(FillsProperty, value);
    public static int GetDropOrder(Control control) => control.GetValue(DropOrderProperty);
    public static void SetDropOrder(Control control, int value) => control.SetValue(DropOrderProperty, value);
    public static string? GetKey(Control control) => control.GetValue(KeyProperty);
    public static void SetKey(Control control, string? value) => control.SetValue(KeyProperty, value);
    public static IReadOnlyList<string>? GetOrder(Control control) => control.GetValue(OrderProperty);
    public static void SetOrder(Control control, IReadOnlyList<string>? value) => control.SetValue(OrderProperty, value);
    public static IReadOnlyDictionary<string, double>? GetWidths(Control control) => control.GetValue(WidthsProperty);
    public static void SetWidths(Control control, IReadOnlyDictionary<string, double>? value) => control.SetValue(WidthsProperty, value);

    /// <summary>Whether a part has given way for lack of room (for the tests).</summary>
    internal bool IsDropped(Control part) => _dropped.Contains(part);

    /// <summary>
    /// The parts in the order they're laid out: the named ones <see cref="OrderProperty"/> names
    /// take, in its order, the places named parts had; every other part stays where it is.
    /// </summary>
    internal IReadOnlyList<Control> Ordered()
    {
        var order = GetOrder(this);
        if (order is not { Count: > 0 })
            return Children;
        var slots = new List<int>();
        var moving = new List<Control>();
        for (int i = 0; i < Children.Count; i++)
        {
            if (GetKey(Children[i]) is { } key && order.Contains(key))
            {
                slots.Add(i);
                moving.Add(Children[i]);
            }
        }
        int At(Control part)
        {
            for (int i = 0; i < order.Count; i++)
            {
                if (order[i] == GetKey(part))
                    return i;
            }
            return -1;
        }
        moving.Sort((a, b) => At(a).CompareTo(At(b)));
        var laidOut = Children.ToArray();
        for (int i = 0; i < slots.Count; i++)
            laidOut[slots[i]] = moving[i];
        return laidOut;
    }

    private double? SetWidth(Control part) =>
        GetKey(part) is { } key && GetWidths(this) is { } widths && widths.TryGetValue(key, out double width) ? width : null;

    /// <summary>The room a part takes: its own, or the width set for it.</summary>
    private double PartWidth(Control part) => SetWidth(part) ?? part.DesiredSize.Width;

    private IEnumerable<Control> Shown => Children.Where(c => c.IsVisible && !_dropped.Contains(c));
    private Control? Fill => Children.FirstOrDefault(c => c.IsVisible && GetFills(c));

    /// <summary>The room the parts that don't fill take, with the spaces between all shown parts.</summary>
    private double FixedWidth()
    {
        var shown = Shown.ToList();
        return shown.Where(c => !GetFills(c)).Sum(PartWidth) + Spacing * Math.Max(0, shown.Count - 1);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var parts = new Size(double.PositiveInfinity, availableSize.Height);
        foreach (var child in Children.Where(c => !GetFills(c)))
            child.Measure(SetWidth(child) is { } width ? new Size(width, availableSize.Height) : parts);

        _dropped.Clear();
        double least = Fill is null ? 0 : FillMinWidth;
        if (double.IsNaN(least) && Fill is { } whole)
        {
            whole.Measure(parts);
            least = whole.DesiredSize.Width;
        }
        foreach (int order in Children.Select(GetDropOrder).Where(o => o > 0).Distinct().Order())
        {
            if (FixedWidth() + least <= availableSize.Width)
                break;
            foreach (var child in Children.Where(c => GetDropOrder(c) == order))
                _dropped.Add(child);
        }

        double used = FixedWidth();
        if (Fill is { } fill)
        {
            fill.Measure(new Size(Math.Max(0, availableSize.Width - used), availableSize.Height));
            used += fill.DesiredSize.Width;
        }
        double height = Shown.Select(c => c.DesiredSize.Height).DefaultIfEmpty(0).Max();
        return new Size(Math.Min(used, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double fillWidth = Math.Max(0, finalSize.Width - FixedWidth()), x = 0;
        foreach (var child in Ordered())
        {
            if (!child.IsVisible)
                continue;
            if (_dropped.Contains(child))
            {
                child.Arrange(new Rect(finalSize.Width, 0, PartWidth(child), finalSize.Height));
                continue;
            }
            double width = GetFills(child) ? fillWidth : PartWidth(child);
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width + Spacing;
        }
        // Cut off at the sides only: a chip a pixel taller than the line keeps its edges.
        Clip = new RectangleGeometry(new Rect(0, -8, finalSize.Width, finalSize.Height + 16));
        return finalSize;
    }
}
