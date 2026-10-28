using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using GZJam.App.Controls;

namespace GZJam.App.Views;

/// <summary>
/// Choosing what the queue shows of each song (#131): the queue's menu › Show, for the decks atop
/// it too (#189). The title and artist always show; the rest is ticked on or off, and remembered.
/// Album, genre, year and when it was last played start off: they're for a queue given the room in
/// the middle (#178). The column names over it (#188) put the parts in another order (drag a name)
/// and resize the text ones (drag the line beside one, on the title's side), remembered too; the
/// decks' rows follow.
/// </summary>
public partial class MainWindow
{
    /// <summary>The queue's optional parts, by name (as kept in the settings) and as the menu says them.</summary>
    internal static readonly (string Name, string Header)[] QueueColumns =
    [
        ("Art", "Album art"), ("Rating", "♥ Rating"), ("Energy", "Energy"), ("Labels", "Labels"),
        ("Album", "Album"), ("Genre", "Genre"), ("Year", "Year"), ("Played", "Played"),
        ("BPM", "BPM"), ("Time", "Time"),
    ];

    /// <summary>The parts that can move, as they come (the drag grip stays first, the preview key last).</summary>
    internal static readonly string[] QueueColumnOrder =
        ["Number", "Art", "Title", "Album", "Genre", "Year", "Played", "Rating", "Energy", "BPM", "Time"];

    /// <summary>The text parts that resize: their widths to start with (margins included), and how far they go.</summary>
    internal static readonly Dictionary<string, (double Start, double Least, double Most)> QueueColumnSizes = new()
    {
        ["Album"] = (144, 50, 320), ["Genre"] = (84, 40, 220), ["Year"] = (34, 30, 60),
        ["Played"] = (46, 34, 90), ["BPM"] = (27, 22, 60), ["Time"] = (36, 30, 70),
    };

    /// <summary>
    /// The decks' time left (#189) is as wide as the queue's time and preview key, less the deck
    /// row's own wider right margin, so the columns line up over the queue's.
    /// </summary>
    private const double TimeLeftBeyondTime = 6 + 24 + 2 - 10;

    private void InitializeQueueColumns()
    {
        DataContextChanged += (_, _) => ApplyQueueColumns();
        QueueHeader.PointerMoved += OnQueueHeaderPointerMoved;
        QueueHeader.PointerPressed += OnQueueHeaderPointerPressed;
        QueueHeader.PointerReleased += OnQueueHeaderPointerReleased;
        QueueHeader.PointerCaptureLost += (_, _) => EndQueueHeaderDrag();
    }

    private void ApplyQueueColumns()
    {
        if (DataContext is null)
            return;
        foreach (var (name, _) in QueueColumns)
            QueueLists.Classes.Set("hide-" + name.ToLowerInvariant(), Vm.HiddenQueueColumns.Contains(name));
        FitRow.SetOrder(QueueLists, QueueOrder());
        FitRow.SetWidths(QueueLists, QueueWidths());
    }

    /// <summary>The parts in the DJ's order: every part once, a part new since then where it comes.</summary>
    internal IReadOnlyList<string> QueueOrder()
    {
        var saved = Vm.PanelSettings.QueueColumnOrder?.Where(QueueColumnOrder.Contains).Distinct().ToList() ?? [];
        foreach (string key in QueueColumnOrder.Where(k => !saved.Contains(k)))
            saved.Insert(Math.Min(saved.Count, Array.IndexOf(QueueColumnOrder, key)), key);
        return saved;
    }

    /// <summary>The text parts' widths, the DJ's or to start with, and the decks' time left to match.</summary>
    internal IReadOnlyDictionary<string, double> QueueWidths()
    {
        var saved = Vm.PanelSettings.QueueColumnWidths;
        var widths = QueueColumnSizes.ToDictionary(s => s.Key,
            s => saved.TryGetValue(s.Key, out double w) ? Math.Clamp(w, s.Value.Least, s.Value.Most) : s.Value.Start);
        widths["TimeLeft"] = widths["Time"] + TimeLeftBeyondTime;
        return widths;
    }

    private void FillQueueColumnsMenu()
    {
        QueueColumnsMenu.Items.Clear();
        foreach (var (name, header) in QueueColumns)
        {
            bool shown = !Vm.HiddenQueueColumns.Contains(name);
            var item = new MenuItem { Header = header, ToggleType = MenuItemToggleType.CheckBox, IsChecked = shown };
            item.Click += (_, _) => SetQueueColumnShown(name, !shown);
            QueueColumnsMenu.Items.Add(item);
        }
        QueueColumnsMenu.Items.Add(new Separator());
        var names = new MenuItem { Header = "Column names", ToggleType = MenuItemToggleType.CheckBox, IsChecked = Vm.ShowsQueueColumnNames };
        names.Click += (_, _) => Vm.ShowsQueueColumnNames = !Vm.ShowsQueueColumnNames;
        QueueColumnsMenu.Items.Add(names);
        var reset = new MenuItem
        {
            Header = "Columns as they came",
            IsEnabled = Vm.PanelSettings.QueueColumnOrder is not null || Vm.PanelSettings.QueueColumnWidths.Count > 0,
        };
        ToolTip.SetTip(reset, "Put the columns back in their first order and widths");
        reset.Click += (_, _) => ResetQueueColumns();
        QueueColumnsMenu.Items.Add(reset);
    }

    internal void SetQueueColumnShown(string name, bool shown)
    {
        Vm.HiddenQueueColumns.Remove(name);
        if (!shown)
            Vm.HiddenQueueColumns.Add(name);
        Log.Info($"Queue column {name}: {(shown ? "shown" : "hidden")}");
        ApplyQueueColumns();
    }

    internal void ResetQueueColumns()
    {
        Vm.PanelSettings.QueueColumnOrder = null;
        Vm.PanelSettings.QueueColumnWidths.Clear();
        Log.Info("Queue columns as they came");
        ApplyQueueColumns();
    }

    /// <summary>Puts <paramref name="key"/> just before <paramref name="before"/> (null: last), and remembers it.</summary>
    internal void MoveQueueColumn(string key, string? before)
    {
        var order = QueueOrder().Where(k => k != key).ToList();
        int at = before is null ? order.Count : order.IndexOf(before);
        order.Insert(at < 0 ? order.Count : at, key);
        Vm.PanelSettings.QueueColumnOrder = order;
        Log.Info($"Queue column {key} moved: {string.Join(", ", order)}");
        ApplyQueueColumns();
    }

    /// <summary>Gives a text part a width (kept within its range), and remembers it.</summary>
    internal void ResizeQueueColumn(string key, double width)
    {
        if (!QueueColumnSizes.TryGetValue(key, out var size))
            return;
        Vm.PanelSettings.QueueColumnWidths[key] = Math.Round(Math.Clamp(width, size.Least, size.Most));
        FitRow.SetWidths(QueueLists, QueueWidths());
    }

    // ───────────── Dragging the column names ─────────────

    /// <summary>How near the line beside a name it's caught, to resize it.</summary>
    private const double ResizeGrab = 5;

    private string? _resizing, _moving;
    private double _dragFrom, _widthFrom, _widthMost;
    private bool _resizingLeft;
    private string? _dropBefore;
    private bool _moved;

    /// <summary>The column names showing, left to right.</summary>
    private List<Control> HeaderParts() => QueueHeader.Ordered()
        .Where(c => c.IsVisible && !QueueHeader.IsDropped(c) && FitRow.GetKey(c) is not null).ToList();

    /// <summary>
    /// The text part whose line <paramref name="x"/> is on, to resize it, and whether that line is
    /// its left one. The title takes the room the others leave, so a part gives it room or takes it
    /// on the title's side (#198): the line there is the one that stays under the pointer. After the
    /// title, a part's line is before it, and the part grows to the left.
    /// </summary>
    private (string Key, bool Left)? ResizableAt(double x)
    {
        var parts = HeaderParts();
        int title = parts.FindIndex(FitRow.GetFills);
        for (int i = 0; i < parts.Count; i++)
        {
            if (FitRow.GetKey(parts[i]) is not { } key || !QueueColumnSizes.ContainsKey(key))
                continue;
            bool left = i > title;
            if (Math.Abs(ResizeLine(parts[i], left) - x) <= ResizeGrab)
                return (key, left);
        }
        return null;
    }

    /// <summary>The line beside a part, midway into the gap (its margin and half the spacing).</summary>
    private double ResizeLine(Control part, bool left) => left
        ? part.Bounds.Left - part.Margin.Left - QueueHeader.Spacing / 2
        : part.Bounds.Right + part.Margin.Right + QueueHeader.Spacing / 2;

    /// <summary>Shows the accent line over the queue at <paramref name="x"/> in the names.</summary>
    private void ShowQueueColumnLine(double x)
    {
        var at = QueueHeader.TranslatePoint(new Point(x, 0), QueueLists) ?? default;
        QueueColumnDropLine.Margin = new Thickness(at.X - 1, 0, 0, 0);
        QueueColumnDropLine.IsVisible = true;
    }

    private void OnQueueHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(QueueHeader).Properties.IsLeftButtonPressed)
            return;
        double x = e.GetPosition(QueueHeader).X;
        _dragFrom = x;
        _moved = false;
        if (ResizableAt(x) is { } line)
        {
            (_resizing, _resizingLeft) = line;
            _widthFrom = QueueWidths()[line.Key];
            // No wider than the title can spare, so nothing gives way under the pointer.
            double spare = HeaderParts().FirstOrDefault(FitRow.GetFills) is { } title
                ? Math.Max(0, title.Bounds.Width - QueueHeader.FillMinWidth) : 0;
            _widthMost = _widthFrom + spare;
            ShowQueueColumnLine(x);
        }
        else if (HeaderParts().FirstOrDefault(c => x >= c.Bounds.Left && x <= c.Bounds.Right) is { } part)
        {
            _moving = FitRow.GetKey(part);
            part.Classes.Add("moving");
        }
        else
        {
            return;
        }
        e.Pointer.Capture(QueueHeader);
        e.Handled = true;
    }

    private void OnQueueHeaderPointerMoved(object? sender, PointerEventArgs e)
    {
        double x = e.GetPosition(QueueHeader).X;
        if (_resizing is { } key)
        {
            double by = _resizingLeft ? _dragFrom - x : x - _dragFrom;
            ResizeQueueColumn(key, Math.Min(_widthFrom + by, _widthMost));
            _moved = true;
            QueueHeader.UpdateLayout();
            if (HeaderParts().FirstOrDefault(c => FitRow.GetKey(c) == key) is { } part)
                ShowQueueColumnLine(ResizeLine(part, _resizingLeft));
            return;
        }
        if (_moving is null)
        {
            QueueHeader.Cursor = ResizableAt(x) is null ? Cursor.Default : new Cursor(StandardCursorType.SizeWestEast);
            return;
        }
        if (!_moved && Math.Abs(x - _dragFrom) < 4)
            return;   // a tap, not a drag
        _moved = true;
        // Before the first name whose middle is past the pointer; past them all, last.
        var parts = HeaderParts();
        Control? next = parts.FirstOrDefault(c => x < c.Bounds.Center.X);
        _dropBefore = next is null ? null : FitRow.GetKey(next);
        ShowQueueColumnLine(next?.Bounds.Left - QueueHeader.Spacing / 2 ?? parts[^1].Bounds.Right + QueueHeader.Spacing / 2);
    }

    private void OnQueueHeaderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_resizing is { } key && _moved)
            Log.Info($"Queue column {key}: {QueueWidths()[key]:0} wide");
        else if (_moving is { } moving && _moved && _dropBefore != moving)
            MoveQueueColumn(moving, _dropBefore);
        e.Pointer.Capture(null);
        EndQueueHeaderDrag();
    }

    private void EndQueueHeaderDrag()
    {
        foreach (var part in QueueHeader.Children)
            part.Classes.Remove("moving");
        _resizing = _moving = _dropBefore = null;
        QueueColumnDropLine.IsVisible = false;
    }
}
