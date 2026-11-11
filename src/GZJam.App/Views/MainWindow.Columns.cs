using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using System.ComponentModel;
using GZJam.App.ViewModels;
using GZJam.Library;
using Track = GZJam.Library.Track;

namespace GZJam.App.Views;

/// <summary>
/// Choosing the library's columns: right-click (or long-press) a column header. The track list
/// doesn't fit every column on a laptop, so each DJ shows what they use. Remembered. The song
/// files' other tags (#119) are columns too, from the same menu: whichever tags the library has.
/// </summary>
public partial class MainWindow
{
    /// <summary>Columns that can be hidden; title, artist and time always show.</summary>
    internal static readonly string[] OptionalColumns = ["♥", "Art", "Album", "Genre", "Grouping", "Comment", "Year", "BPM", "Energy", "Played"];

    private ContextMenu? _columnsMenu;

    private void InitializeColumns()
    {
        // Before the row menu sees it: a right-click on a header is about columns, not tracks.
        TrackGrid.AddHandler(ContextRequestedEvent, OnGridContextRequested, RoutingStrategies.Tunnel);
        TrackGrid.Sorting += OnGridSorting;
        DataContextChanged += (_, _) =>
        {
            ApplyHiddenColumns();
            ApplyTagColumns();
            if (DataContext is MainWindowViewModel vm)
            {
                vm.Library.PropertyChanged -= OnLibraryChangedForColumns;
                vm.Library.PropertyChanged += OnLibraryChangedForColumns;
                ApplyAddedColumn();
            }
        };
    }

    // ───────────── Added, in Files (#383) ─────────────

    private DataGridColumn AddedColumn => TrackGrid.Columns.Single(c => (string?)c.Header == "Added");

    private void OnLibraryChangedForColumns(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LibraryViewModel.IsFilesShowing))
            ApplyAddedColumn();
    }

    /// <summary>
    /// When each file arrived shows in Files only: elsewhere songs have no such date. Leaving Files
    /// sorted by it, the sort goes too, rather than leaving the list in no order anyone chose.
    /// </summary>
    private void ApplyAddedColumn()
    {
        bool shown = Vm.Library.IsFilesShowing;
        AddedColumn.IsVisible = shown;
        if (!shown && TrackGrid.CollectionView?.SortDescriptions is { } sorts
            && sorts.FirstOrDefault(d => d.PropertyPath == nameof(Track.AddedUtc)) is { } added)
            sorts.Remove(added);
    }

    /// <summary>
    /// The first click on Added sorts newest first, as a DJ working through new music wants; the
    /// next, oldest first. Set on the grid's sort itself: the column's own Sort, called from here,
    /// sorts later and asks again, without end.
    /// </summary>
    private void OnGridSorting(object? sender, DataGridColumnEventArgs e)
    {
        if ((string?)e.Column.Header != "Added" || TrackGrid.CollectionView?.SortDescriptions is not { } sorts)
            return;
        e.Handled = true;
        bool newestFirst = sorts.FirstOrDefault(d => d.PropertyPath == nameof(Track.AddedUtc)) is not { Direction: ListSortDirection.Descending };
        sorts.Clear();
        sorts.Add(Avalonia.Collections.DataGridSortDescription.FromPath(nameof(Track.AddedUtc),
            newestFirst ? ListSortDirection.Descending : ListSortDirection.Ascending));
    }

    private IEnumerable<DataGridColumn> OptionalGridColumns =>
        TrackGrid.Columns.Where(c => c.Header is string header && c.Tag is not TagColumnKey && OptionalColumns.Contains(header));

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

    /// <summary>Columns that always show: in the menu too, ticked and grayed, so a DJ looking for one finds it (#379).</summary>
    internal static readonly string[] FixedColumns = ["Title", "Artist", "Time"];

    /// <summary>The menu of columns, in the list's order, ticked when shown.</summary>
    internal ContextMenu ColumnsMenu()
    {
        _columnsMenu ??= new ContextMenu();
        _columnsMenu.Items.Clear();
        _columnsMenu.Items.Add(new MenuItem { Header = "Show columns", IsEnabled = false });
        foreach (DataGridColumn column in TrackGrid.Columns.Where(c => c.Tag is not TagColumnKey && c.Header is string))
        {
            string name = (string)column.Header!;
            if (name == "Added")
            {
                if (!column.IsVisible)
                    continue;   // Files only
                var files = new MenuItem { Header = name, ToggleType = MenuItemToggleType.CheckBox, IsChecked = true, IsEnabled = false };
                ToolTip.SetTip(files, "Shown in Files: when each file arrived on the drive");
                ToolTip.SetShowOnDisabled(files, true);
                _columnsMenu.Items.Add(files);
                continue;
            }
            if (FixedColumns.Contains(name))
            {
                var always = new MenuItem
                {
                    Header = name,
                    ToggleType = MenuItemToggleType.CheckBox,
                    IsChecked = true,
                    IsEnabled = false,
                };
                ToolTip.SetTip(always, "Always shown");
                ToolTip.SetShowOnDisabled(always, true);
                _columnsMenu.Items.Add(always);
                continue;
            }
            if (!OptionalColumns.Contains(name))
                continue;
            var item = new MenuItem
            {
                Header = name switch { "♥" => "♥ Rating", "Art" => "Album art", _ => name },
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = column.IsVisible,
            };
            item.Click += (_, _) => SetColumnShown(name, !column.IsVisible);
            _columnsMenu.Items.Add(item);
        }
        AddTagItems(_columnsMenu);
        return _columnsMenu;
    }

    // ───────────── The song files' other tags (#119) ─────────────

    /// <summary>The most tags listed in the menu itself; the rest are under "More tags".</summary>
    internal const int TagsInMenu = 12;

    /// <summary>What a tag column's <see cref="DataGridColumn.Tag"/> holds: which tag it shows.</summary>
    private sealed record TagColumnKey(string Key);

    private IEnumerable<DataGridColumn> TagGridColumns => TrackGrid.Columns.Where(c => c.Tag is TagColumnKey);

    private DataGridColumn? TagGridColumn(string key) => TagGridColumns.FirstOrDefault(c => ((TagColumnKey)c.Tag!).Key == key);

    /// <summary>The tags the library's songs have, most songs first, each with a tick if it's a column.</summary>
    private void AddTagItems(ContextMenu menu)
    {
        var tags = Vm.Library.TagNames();
        // Columns of tags no song has now (the folder with them removed) can still be taken away.
        var shownGone = Vm.TagColumns.Where(key => tags.All(t => t.Key != key))
            .Select(key => new TagName(key, ColumnName(key), 0));
        var all = tags.Concat(shownGone).ToList();
        if (all.Count == 0)
            return;
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Tags in your songs", IsEnabled = false });
        MenuItem Item(TagName tag)
        {
            var item = new MenuItem
            {
                Header = tag.Name,
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = Vm.TagColumns.Contains(tag.Key),
            };
            ToolTip.SetTip(item, tag.Count == 1 ? "1 song has it" : $"{tag.Count:N0} songs have it");
            item.Click += (_, _) => SetTagColumnShown(tag.Key, !Vm.TagColumns.Contains(tag.Key));
            return item;
        }
        // The ones shown, then the most used: a tag kept on a few songs needn't crowd the menu.
        var first = all.Where(t => Vm.TagColumns.Contains(t.Key)).Concat(all.Where(t => !Vm.TagColumns.Contains(t.Key)))
            .Take(Math.Max(TagsInMenu, Vm.TagColumns.Count)).ToList();
        foreach (TagName tag in all.Where(first.Contains))
            menu.Items.Add(Item(tag));
        var rest = all.Where(t => !first.Contains(t)).ToList();
        if (rest.Count > 0)
        {
            var more = new MenuItem { Header = "More tags" };
            foreach (TagName tag in rest.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase))
                more.Items.Add(Item(tag));
            menu.Items.Add(more);
        }
    }

    /// <summary>Shows or hides the column of a tag in the song files, by key. Remembered.</summary>
    internal void SetTagColumnShown(string key, bool shown)
    {
        if (shown == Vm.TagColumns.Contains(key))
            return;
        if (shown)
        {
            Vm.TagColumns.Add(key);
            AddTagColumn(key);
        }
        else
        {
            Vm.TagColumns.Remove(key);
            if (TagGridColumn(key) is { } column)
                TrackGrid.Columns.Remove(column);
        }
        Log.Info($"Library tag column {key}: {(shown ? "shown" : "hidden")}");
    }

    private void ApplyTagColumns()
    {
        if (DataContext is null)
            return;
        foreach (DataGridColumn column in TagGridColumns.ToList())
            TrackGrid.Columns.Remove(column);
        foreach (string key in Vm.TagColumns)
            AddTagColumn(key);
    }

    /// <summary>
    /// What a tag's column is called: the name most songs give it. One of GZJam's own column names
    /// (a tag called "Energy") says it's the tag, so the two aren't mixed up.
    /// </summary>
    private string ColumnName(string key)
    {
        string name = Vm.Library.TagNames().FirstOrDefault(t => t.Key == key)?.Name ?? char.ToUpperInvariant(key[0]) + key[1..];
        bool taken = TrackGrid.Columns.Any(c => c.Tag is not TagColumnKey && c.Header is string header
            && string.Equals(header, name, StringComparison.OrdinalIgnoreCase));
        return taken ? $"{name} (tag)" : name;
    }

    /// <summary>A tag's column, after the other text columns (before Year) and any tag columns already there.</summary>
    private void AddTagColumn(string key)
    {
        if (TagGridColumn(key) is not null)
            return;
        var column = new DataGridTemplateColumn
        {
            Header = ColumnName(key),
            Tag = new TagColumnKey(key),
            Width = new DataGridLength(120),
            MinWidth = 44,
            CanUserSort = true,
            SortMemberPath = nameof(Track.Tags),
            CustomSortComparer = new TagComparer(key),
            CellTemplate = new FuncDataTemplate<Track>((_, _) => TagCell(key)),
        };
        int year = TrackGrid.Columns.IndexOf(TrackGrid.Columns.FirstOrDefault(c => (string?)c.Header == "Year") ?? TrackGrid.Columns[^1]);
        TrackGrid.Columns.Insert(year < 0 ? TrackGrid.Columns.Count : year, column);
    }

    /// <summary>A tag's value in a row, cut off with "…", the whole of it in the tooltip, as Grouping and Comment are.</summary>
    private static TextBlock TagCell(string key)
    {
        var text = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Tag = key };
        Track? shown = null;
        bool attached = false;
        void Show()
        {
            string? value = shown?.Tag(key);
            text.Text = value;
            ToolTip.SetTip(text, value);
        }
        void OnTrackChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Track.Tags))
                Show();   // edited in its info (#129)
        }
        void Follow(Track? track)
        {
            if (shown is not null)
                shown.PropertyChanged -= OnTrackChanged;
            shown = track;
            if (shown is not null)
                shown.PropertyChanged += OnTrackChanged;
            Show();
        }
        // The grid reuses its rows: the value follows the song the row shows now.
        text.DataContextChanged += (_, _) => Follow(attached ? text.DataContext as Track : null);
        text.AttachedToVisualTree += (_, _) =>
        {
            attached = true;
            Follow(text.DataContext as Track);
        };
        text.DetachedFromVisualTree += (_, _) =>
        {
            attached = false;
            Follow(null);
        };
        return text;
    }

    /// <summary>Sorts by a tag's value, songs without it first (as the other columns put empty ones).</summary>
    private sealed class TagComparer(string key) : System.Collections.IComparer
    {
        public int Compare(object? x, object? y) =>
            string.Compare((x as Track)?.Tag(key), (y as Track)?.Tag(key), StringComparison.CurrentCultureIgnoreCase);
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
