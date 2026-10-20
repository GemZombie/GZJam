using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>A track waiting in the queue. The same track can be queued more than once.</summary>
public sealed partial class QueueItem(Track track) : ObservableObject
{
    public Track Track { get; } = track;

    [ObservableProperty]
    public partial int Number { get; set; }

    public string Title => Track.DisplayTitle;
    public string Artist => Track.Artist ?? "";
    public string BpmText => Format.Bpm(Track.Bpm);
    public string DurationText => Format.Duration(Track.Duration);
}

/// <summary>The upcoming tracks, in play order.</summary>
public sealed partial class QueueViewModel : ObservableObject
{
    public QueueViewModel()
    {
        Items.CollectionChanged += OnItemsChanged;
    }

    public ObservableCollection<QueueItem> Items { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    public string Summary
    {
        get
        {
            if (Items.Count == 0)
                return "";
            var total = TimeSpan.FromTicks(Items.Sum(i => i.Track.Duration.Ticks));
            return $"{Items.Count} {(Items.Count == 1 ? "track" : "tracks")} · {Format.Duration(total)}";
        }
    }

    /// <summary>Adds tracks at <paramref name="index"/>, or at the end.</summary>
    public void Add(IEnumerable<Track> tracks, int? index = null)
    {
        int at = Math.Clamp(index ?? Items.Count, 0, Items.Count);
        foreach (Track track in tracks)
            Items.Insert(at++, new QueueItem(track));
    }

    public void PlayNext(IEnumerable<Track> tracks) => Add(tracks, 0);

    /// <summary>Moves items to a new position, keeping their relative order. <paramref name="index"/> is in terms of the list before the move.</summary>
    public void Move(IReadOnlyList<QueueItem> items, int index)
    {
        var moving = Items.Where(items.Contains).ToList();
        if (moving.Count == 0)
            return;
        int target = index - moving.Count(m => Items.IndexOf(m) < index);
        foreach (QueueItem item in moving)
            Items.Remove(item);
        target = Math.Clamp(target, 0, Items.Count);
        foreach (QueueItem item in moving)
            Items.Insert(target++, item);
    }

    public void Remove(IEnumerable<QueueItem> items)
    {
        foreach (QueueItem item in items.ToList())
            Items.Remove(item);
    }

    public void Clear() => Items.Clear();

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        for (int i = 0; i < Items.Count; i++)
            Items[i].Number = i + 1;
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Summary));
    }
}
