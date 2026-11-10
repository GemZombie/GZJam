using System.Collections;
using System.Collections.Specialized;
using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>
/// The song list's rows: one collection for the life of the window, whose songs are swapped when
/// a search or a folder changes what the list shows. The grid reuses its rows when the same
/// collection is reset; handed a new collection, it throws every row away and builds new ones,
/// styles and all. While browsing, that was tens of MB of garbage per second, and the garbage
/// collections it caused stopped the music (#343). Keeping one collection also keeps the column
/// the list is sorted by.
/// </summary>
public sealed class ResultRows : IReadOnlyList<Track>, IList, INotifyCollectionChanged
{
    private static readonly NotifyCollectionChangedEventArgs ResetArgs = new(NotifyCollectionChangedAction.Reset);

    private IReadOnlyList<Track> _tracks = [];

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    /// <summary>Shows <paramref name="tracks"/> in place of what's there.</summary>
    public void Replace(IReadOnlyList<Track> tracks)
    {
        _tracks = tracks;
        CollectionChanged?.Invoke(this, ResetArgs);
    }

    public int Count => _tracks.Count;

    public Track this[int index] => _tracks[index];

    public IEnumerator<Track> GetEnumerator() => _tracks.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // Read-only IList: the grid reads by index; only Replace changes the rows.
    object? IList.this[int index]
    {
        get => _tracks[index];
        set => throw new NotSupportedException();
    }

    bool IList.IsReadOnly => true;
    bool IList.IsFixedSize => true;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => this;

    int IList.IndexOf(object? value)
    {
        for (int i = 0; i < _tracks.Count; i++)
            if (ReferenceEquals(_tracks[i], value))
                return i;
        return -1;
    }

    bool IList.Contains(object? value) => ((IList)this).IndexOf(value) >= 0;

    void ICollection.CopyTo(Array array, int index)
    {
        for (int i = 0; i < _tracks.Count; i++)
            array.SetValue(_tracks[i], index + i);
    }

    int IList.Add(object? value) => throw new NotSupportedException();
    void IList.Clear() => throw new NotSupportedException();
    void IList.Insert(int index, object? value) => throw new NotSupportedException();
    void IList.Remove(object? value) => throw new NotSupportedException();
    void IList.RemoveAt(int index) => throw new NotSupportedException();
}
