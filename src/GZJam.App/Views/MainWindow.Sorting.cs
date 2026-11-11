using System.ComponentModel;
using Avalonia.Collections;
using Avalonia.Controls;
using GZJam.App.ViewModels;
using GZJam.Library;
using Track = GZJam.Library.Track;

namespace GZJam.App.Views;

/// <summary>
/// The song list keeps its sort (#376): My music, Servers and Files one each, shared by their
/// folders, and each playlist its own, so a playlist opens in its own order (often a planned set)
/// until the DJ sorts it. Remembered between runs. A header's clicks go ascending, descending,
/// then off, back to the list's own order.
/// </summary>
public partial class MainWindow
{
    /// <summary>The list whose sort is shown (<see cref="LibraryViewModel.SortKey"/>), and the sort itself.</summary>
    private string? _sortKey;
    private AppSettings.ListSort? _shownSort;

    private void OnLibraryChangedForSort(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LibraryViewModel.SortKey))
            RestoreSort();
    }

    /// <summary>The list showing now has its own sort: shown, if it isn't already.</summary>
    private void RestoreSort()
    {
        string key = Vm.Library.SortKey;
        if (key == _sortKey || TrackGrid.CollectionView is null)
            return;
        _sortKey = key;
        _ = ShowSort(Vm.SortFor(key));
    }

    /// <summary>What a sort calls a column: its sort path, or a tag's key for a tag's column (#119).</summary>
    private static string? ColumnId(DataGridColumn column) =>
        column.Tag is TagColumnKey tag ? "tag:" + tag.Key : column.CanUserSort ? column.SortMemberPath : null;

    /// <summary>
    /// Sorts the song list by <paramref name="sort"/>, or puts it in the list's own order (null):
    /// done when the rows show it. The songs are sorted by the library (#368); the grid only shows
    /// the column's arrow.
    /// </summary>
    private Task ShowSort(AppSettings.ListSort? sort)
    {
        if (TrackGrid.CollectionView is not { } view)
            return Task.CompletedTask;
        DataGridColumn? column = sort is null ? null : TrackGrid.Columns.FirstOrDefault(c => ColumnId(c) == sort.Column);
        _shownSort = column is null ? null : sort;
        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            if (column is not null)
            {
                var direction = sort!.Descending ? ListSortDirection.Descending : ListSortDirection.Ascending;
                view.SortDescriptions.Add(new PresortedSort(column, direction));
            }
        }
        return Vm.Library.SortByAsync(_shownSort);
    }

    /// <summary>
    /// A header clicked: ascending, descending, then the list's own order again. Added (#383) goes
    /// newest first, then oldest, as a DJ working through new music wants. Set on the grid's sort
    /// itself: the column's own Sort, called from here, sorts later and asks again, without end.
    /// </summary>
    private async void OnGridSorting(object? sender, DataGridColumnEventArgs e)
    {
        if (TrackGrid.CollectionView is null || ColumnId(e.Column) is not { } id)
            return;
        e.Handled = true;
        bool firstDescending = id == nameof(Track.AddedUtc);
        AppSettings.ListSort? now = _shownSort;
        AppSettings.ListSort? next = now?.Column != id ? new AppSettings.ListSort(id, firstDescending)
            : now.Descending == firstDescending ? now with { Descending = !firstDescending }
            : null;
        Vm.RememberSort(Vm.Library.SortKey, next);
        Log.Info($"Song list ({Vm.Library.SortKey}) sorted {(next is null ? "in its own order" : $"by {e.Column.Header}, {(next.Descending ? "descending" : "ascending")}")}");
        var selected = TrackGrid.SelectedItems.OfType<Track>().ToList();
        var rows = Vm.Library.ResultRows;
        await ShowSort(next);
        if (selected.Count == 0)
            return;
        // The songs selected stay selected, the first in view, wherever the sort put them.
        var shown = rows.ToHashSet();
        TrackGrid.SelectedItems.Clear();
        foreach (Track track in selected.Where(shown.Contains))
            TrackGrid.SelectedItems.Add(track);
        if (TrackGrid.SelectedItems.OfType<Track>().FirstOrDefault() is { } first)
            TrackGrid.ScrollIntoView(first, null);
    }
}
