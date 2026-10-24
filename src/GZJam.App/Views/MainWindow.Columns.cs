using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace GZJam.App.Views;

/// <summary>
/// Choosing the library's columns: right-click (or long-press) a column header. The track list
/// doesn't fit every column on a laptop, so each DJ shows what they use. Remembered.
/// </summary>
public partial class MainWindow
{
    /// <summary>Columns that can be hidden; title, artist and time always show.</summary>
    internal static readonly string[] OptionalColumns = ["♥", "Art", "Album", "Genre", "Year", "BPM", "Energy", "Played"];

    private ContextMenu? _columnsMenu;

    private void InitializeColumns()
    {
        // Before the row menu sees it: a right-click on a header is about columns, not tracks.
        TrackGrid.AddHandler(ContextRequestedEvent, OnGridContextRequested, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) => ApplyHiddenColumns();
    }

    private IEnumerable<DataGridColumn> OptionalGridColumns =>
        TrackGrid.Columns.Where(c => c.Header is string header && OptionalColumns.Contains(header));

    private void ApplyHiddenColumns()
    {
        if (DataContext is null)
            return;
        foreach (DataGridColumn column in OptionalGridColumns)
            column.IsVisible = !Vm.HiddenColumns.Contains((string)column.Header!);
    }

    private void OnGridContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Source is not Visual source || source.FindAncestorOfType<DataGridColumnHeader>(includeSelf: true) is not { } header)
            return;
        e.Handled = true;
        ColumnsMenu().Open(header);
    }

    /// <summary>The menu of columns, ticked when shown.</summary>
    internal ContextMenu ColumnsMenu()
    {
        _columnsMenu ??= new ContextMenu();
        _columnsMenu.Items.Clear();
        _columnsMenu.Items.Add(new MenuItem { Header = "Show columns", IsEnabled = false });
        foreach (DataGridColumn column in OptionalGridColumns)
        {
            string name = (string)column.Header!;
            var item = new MenuItem
            {
                Header = name switch { "♥" => "♥ Rating", "Art" => "Album art", _ => name },
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = column.IsVisible,
            };
            item.Click += (_, _) => SetColumnShown(name, !column.IsVisible);
            _columnsMenu.Items.Add(item);
        }
        return _columnsMenu;
    }

    internal void SetColumnShown(string name, bool shown)
    {
        if (OptionalGridColumns.FirstOrDefault(c => (string)c.Header! == name) is not { } column)
            return;
        column.IsVisible = shown;
        Vm.HiddenColumns.Remove(name);
        if (!shown)
            Vm.HiddenColumns.Add(name);
        Log.Info($"Library column {name}: {(shown ? "shown" : "hidden")}");
    }
}
