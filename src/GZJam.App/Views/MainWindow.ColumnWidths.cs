using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace GZJam.App.Views;

/// <summary>
/// The song list remembers the widths its columns are dragged to (#390), as the queue does
/// (#188): in the Library, playlists and Files alike, tag columns (#119) too. Saved by column name
/// (a tag column by its key), so showing, hiding or adding a column leaves the others' widths be.
/// Title, Artist and Album share the room left over (star widths): dragging one changes the
/// shares, and those are what's kept. The columns menu puts them all back to their defaults.
/// </summary>
public partial class MainWindow
{
    /// <summary>Each built-in column's width as GZJam lays it out, before any the DJ chose.</summary>
    private Dictionary<DataGridColumn, DataGridLength>? _defaultColumnWidths;

    /// <summary>A tag column's width until the DJ drags it (#119).</summary>
    private static readonly DataGridLength DefaultTagColumnWidth = new(120);

    private void InitializeColumnWidths()
    {
        _defaultColumnWidths = TrackGrid.Columns.ToDictionary(c => c, c => c.Width);
        // A drag on a header's edge ends with the button let go: then see what changed. (A click
        // that sorts changes no width, and so saves nothing.)
        TrackGrid.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if ((e.Source as Visual)?.FindAncestorOfType<DataGridColumnHeader>(includeSelf: true) is not null)
                Dispatcher.UIThread.Post(RememberColumnWidths, DispatcherPriority.Background);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>What a column's width is saved under: its name, or <c>tag:</c> and the tag's key.</summary>
    private static string? WidthKey(DataGridColumn column) => column.Tag switch
    {
        TagColumnKey tag => "tag:" + tag.Key,
        _ => column.Header as string is { Length: > 0 } name ? name : null,
    };

    private DataGridLength DefaultWidth(DataGridColumn column) =>
        _defaultColumnWidths?.TryGetValue(column, out DataGridLength width) == true ? width : DefaultTagColumnWidth;

    /// <summary>Saves the widths that differ from the defaults, and forgets the ones that don't.</summary>
    internal void RememberColumnWidths()
    {
        if (DataContext is null)
            return;
        var saved = Vm.PanelSettings.SongColumnWidths;
        foreach (DataGridColumn column in TrackGrid.Columns)
        {
            if (!column.CanUserResize || WidthKey(column) is not { } key)
                continue;
            if (SameWidth(column.Width, DefaultWidth(column)))
                saved.Remove(key);
            else if (WidthText(column.Width) is { } text && (!saved.TryGetValue(key, out string? old) || old != text))
            {
                saved[key] = text;
                Log.Info($"Library column {key}: width {text}");
            }
        }
    }

    /// <summary>Gives each column the width the DJ dragged it to, if any (at startup, and as a tag column is added).</summary>
    private void ApplyColumnWidths()
    {
        if (DataContext is null)
            return;
        foreach (DataGridColumn column in TrackGrid.Columns)
            ApplyColumnWidth(column);
    }

    private void ApplyColumnWidth(DataGridColumn column)
    {
        if (DataContext is null || !column.CanUserResize || WidthKey(column) is not { } key)
            return;
        if (Vm.PanelSettings.SongColumnWidths.TryGetValue(key, out string? text) && ParseWidth(text) is { } width)
            column.Width = width;
    }

    internal bool HasChosenColumnWidths => DataContext is not null && Vm.PanelSettings.SongColumnWidths.Count > 0;

    /// <summary>The columns menu's "Default widths": every column as GZJam lays it out.</summary>
    internal void ResetColumnWidths()
    {
        Vm.PanelSettings.SongColumnWidths.Clear();
        foreach (DataGridColumn column in TrackGrid.Columns)
        {
            if (column.CanUserResize)
                column.Width = DefaultWidth(column);
        }
        Log.Info("Library columns: default widths");
    }

    private static bool SameWidth(DataGridLength a, DataGridLength b) =>
        a.UnitType == b.UnitType && Math.Abs(a.Value - b.Value) < (a.IsStar ? 0.005 : 0.5);

    /// <summary>"120" for pixels, "2.66*" for a share; null for a width that isn't either (auto).</summary>
    internal static string? WidthText(DataGridLength width) =>
        width.IsStar ? Math.Round(width.Value, 3).ToString(CultureInfo.InvariantCulture) + "*"
        : width.IsAbsolute ? Math.Round(width.Value).ToString(CultureInfo.InvariantCulture)
        : null;

    internal static DataGridLength? ParseWidth(string text)
    {
        bool star = text.EndsWith('*');
        if (!double.TryParse(star ? text[..^1] : text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            || value <= 0 || double.IsInfinity(value))
            return null;
        return star ? new DataGridLength(value, DataGridLengthUnitType.Star) : new DataGridLength(value);
    }
}
