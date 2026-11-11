using System.Collections;
using System.ComponentModel;
using Avalonia.Collections;
using Avalonia.Controls;

namespace GZJam.App.Views;

/// <summary>
/// The song list's sort as the grid sees it (#368): its column, for the header's arrow, but the
/// rows kept in the order they come, since <see cref="ViewModels.ListOrder"/> has sorted them
/// already, off the window's thread. The grid's own sort took over 100 ms for 60,000 songs there.
/// A column with a comparer of its own (a tag's, #119) is found by that comparer; any other, by
/// its sort path.
/// </summary>
internal sealed class PresortedSort(DataGridColumn column, ListSortDirection direction)
    : DataGridComparerSortDescription(column.CustomSortComparer ?? NoComparer.Instance, direction)
{
    private readonly string? _path = column.CustomSortComparer is null ? column.SortMemberPath : null;

    public override string PropertyPath => _path!;

    public override IOrderedEnumerable<object> OrderBy(IEnumerable<object> seq) => new AsTheyCome(seq);

    public override IOrderedEnumerable<object> ThenBy(IOrderedEnumerable<object> seq) => seq;

    public override DataGridSortDescription SwitchSortDirection() =>
        new PresortedSort(column, Direction == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending);

    /// <summary>Stands for a column's comparer on columns that sort by their path: never asked to compare.</summary>
    private sealed class NoComparer : IComparer
    {
        public static readonly NoComparer Instance = new();

        public int Compare(object? x, object? y) => 0;
    }

    private sealed class AsTheyCome(IEnumerable<object> rows) : IOrderedEnumerable<object>
    {
        public IOrderedEnumerable<object> CreateOrderedEnumerable<TKey>(Func<object, TKey> keySelector, IComparer<TKey>? comparer, bool descending) =>
            this;

        public IEnumerator<object> GetEnumerator() => rows.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
