using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GZJam.App.ViewModels;

namespace GZJam.App.Views;

/// <summary>
/// Organize music (#325): beside the decks, the one place GZJam moves files. Songs are dragged
/// across as they are in the main window, by a tag GZJam moves itself; the buttons do the same with
/// no drag and no key held.
/// </summary>
public partial class OrganizeWindow : Window
{
    /// <summary>How far (px) a press moves before it's a drag.</summary>
    private const double DragStart = 6;

    private readonly OrganizeViewModel? _model;
    private OrganizePane? _pressedPane;
    private OrganizeItem? _pressed;
    private Point _pressedAt;
    private bool _keepSelection;
    private OrganizePane? _dragging;
    private OrganizeItem? _overRow;
    private OrganizePane? _overPane;
    private string? _target;

    public OrganizeWindow() : this(null)
    {
    }

    public OrganizeWindow(OrganizeViewModel? model)
    {
        InitializeComponent();
        _model = model;
        if (model is null)
            return;
        DataContext = model;
        LeftPane.Organize = model;
        RightPane.Organize = model;
        model.ConfirmAsync = (heading, message, yes) => new ConfirmDialog(heading, message, yes).ShowDialog<bool>(this);
        CopyButton.Click += async (_, _) => await model.CopyAsync();
        MoveButton.Click += async (_, _) => await model.MoveAsync();
        UndoButton.Click += async (_, _) => await model.UndoAsync();
        StopButton.Click += (_, _) => model.Stop();
        foreach (OrganizePaneView view in (OrganizePaneView[])[LeftPane, RightPane])
            view.SongList.AddHandler(PointerPressedEvent, (_, e) => OnRowPressed(view, e), RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _dragging is not null)
                EndDrag();
        };
    }

    private void OnRowPressed(OrganizePaneView view, PointerPressedEventArgs e)
    {
        _pressed = null;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || (e.Source as Control)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is not OrganizeItem { IsSong: true } item
            || (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is not null
            || view.DataContext is not OrganizePane pane)
            return;
        _pressed = item;
        _pressedPane = pane;
        _pressedAt = e.GetPosition(this);
        // Several selected, and one of them pressed: it may be the start of dragging them all.
        _keepSelection = e.KeyModifiers == KeyModifiers.None && pane.Selected.Count > 1 && pane.Selected.Contains(item);
        if (_keepSelection)
            e.Handled = true;
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging is not null)
        {
            Follow(e.GetPosition(this), e.KeyModifiers.HasFlag(KeyModifiers.Control));
            e.Handled = true;
            return;
        }
        if (_pressed is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        Point at = e.GetPosition(this);
        Vector moved = at - _pressedAt;
        if (moved.Length < DragStart)
            return;
        if (!_pressedPane!.Selected.Contains(_pressed))
        {
            _pressedPane.Selection.Clear();
            _pressedPane.Selection.Select(_pressedPane.Items.IndexOf(_pressed));
        }
        _dragging = _pressedPane;
        _pressed = null;
        e.Pointer.Capture(this);
        DragTag.IsVisible = true;
        Follow(at, e.KeyModifiers.HasFlag(KeyModifiers.Control));
    }

    private async void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging is { } from)
        {
            string? target = _target;
            bool copy = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            bool can = target is not null && _model!.DragText(from, target, copy) is not null;
            e.Pointer.Capture(null);
            EndDrag();
            e.Handled = true;
            if (can)
                await _model!.DropAsync(from, target!, copy);
            return;
        }
        if (_keepSelection && _pressed is { } pressed && _pressedPane is { } pane)
        {
            pane.Selection.Clear();   // only clicked after all: it alone is selected, as in the song list
            pane.Selection.Select(pane.Items.IndexOf(pressed));
        }
        _pressed = null;
        _keepSelection = false;
    }

    /// <summary>The tag follows the pointer, and says where the songs would go.</summary>
    private void Follow(Point at, bool copy)
    {
        Canvas.SetLeft(DragTag, at.X + 14);
        Canvas.SetTop(DragTag, at.Y + 10);
        ClearOver();

        var (row, pane) = Under(at);
        if (row is { IsFolder: true })
        {
            _overRow = row;
            row.IsDropTarget = true;
            _target = row.Path;
        }
        else if (pane is not null && pane != _dragging && pane.Folder is { } folder)
        {
            _overPane = pane;
            pane.IsDropTarget = true;
            _target = folder;
        }
        else
        {
            _target = null;
        }
        string? text = _target is null ? null : _model!.DragText(_dragging!, _target, copy);
        DragTagText.Text = text ?? (pane is null ? "Drop on the other side" : "Can't go here");
        DragTag.Classes.Set("refused", text is null);
        if (text is null)
        {
            _target = null;
            ClearOver();
        }
    }

    private (OrganizeItem? Row, OrganizePane? Pane) Under(Point at)
    {
        var hit = this.GetVisualsAt(at, v => v.IsEffectivelyVisible && v is InputElement { IsHitTestVisible: true }).FirstOrDefault();
        OrganizeItem? row = hit?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as OrganizeItem;
        OrganizePane? pane = hit?.FindAncestorOfType<OrganizePaneView>(includeSelf: true)?.DataContext as OrganizePane;
        return (row, pane);
    }

    private void ClearOver()
    {
        if (_overRow is not null)
            _overRow.IsDropTarget = false;
        if (_overPane is not null)
            _overPane.IsDropTarget = false;
        _overRow = null;
        _overPane = null;
    }

    private void EndDrag()
    {
        _dragging = null;
        _target = null;
        _keepSelection = false;
        ClearOver();
        DragTag.IsVisible = false;
    }
}
