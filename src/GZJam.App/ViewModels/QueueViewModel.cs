using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>A track waiting in the queue. The same track can be queued more than once.</summary>
public sealed partial class QueueItem : ObservableObject
{
    public QueueItem(Track track)
    {
        Track = track;
        track.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Track.Bpm))
                OnPropertyChanged(nameof(BpmText));   // detected after it was queued
        };
    }

    public Track Track { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNext))]
    public partial int Number { get; set; }

    /// <summary>First in line: auto-play takes it next.</summary>
    public bool IsNext => Number == 1;

    public string Title => Track.DisplayTitle;
    public string Artist => Track.DisplayArtist;
    public string BpmText => Format.Bpm(Track.Bpm);
    public string DurationText => Format.Duration(Track.Duration);

    /// <summary>Still on a server (#290): waiting for its song, which takes its place when it arrives.</summary>
    public bool IsWaiting => Track.IsOnServerOnly;

    /// <summary>"Downloading…", "Waiting for synology", "Couldn't download: …"; null for a song that's here.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasServerState))]
    public partial string? ServerState { get; private set; }

    [ObservableProperty]
    public partial bool ServerProblem { get; private set; }

    [ObservableProperty]
    public partial bool CanRetry { get; private set; }

    public bool HasServerState => ServerState is not null;

    public void ShowServerState(string state, bool problem, bool canRetry)
    {
        ServerState = state;
        ServerProblem = problem;
        CanRetry = canRetry;
    }

    /// <summary>When it will start playing, by the clock ("9:42 PM"); set with the time the queue ends (<see cref="MainWindowViewModel.UpdateQueueEnd"/>).</summary>
    [ObservableProperty]
    public partial string StartsText { get; set; } = "";
}

/// <summary>The upcoming tracks, in play order.</summary>
public sealed partial class QueueViewModel : ObservableObject
{
    public QueueViewModel()
    {
        Items.CollectionChanged += OnItemsChanged;
    }

    public ObservableCollection<QueueItem> Items { get; } = [];

    /// <summary>
    /// Repeat (#255), set from auto-play's options: a song taken from the queue for a deck goes
    /// back to its end (<see cref="Took"/>), so the queue goes round and a playlist plays over and over.
    /// </summary>
    public bool Repeat { get; set; }

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

    /// <summary>Adds tracks at <paramref name="index"/>, or at the end. Returns the new rows.</summary>
    public IReadOnlyList<QueueItem> Add(IEnumerable<Track> tracks, int? index = null)
    {
        int at = Math.Clamp(index ?? Items.Count, 0, Items.Count);
        var added = new List<QueueItem>();
        foreach (Track track in tracks)
        {
            var item = new QueueItem(track);
            Items.Insert(at++, item);
            added.Add(item);
        }
        return added;
    }

    public void PlayNext(IEnumerable<Track> tracks) => Add(tracks, 0);

    /// <summary>
    /// Removes and returns the first song of the queue that's on this computer (auto-play loading it
    /// onto a deck). Songs still on a server stay where they are, in front of it, until they're here
    /// (#290): <see cref="PassedOver"/> says which were.
    /// </summary>
    public Track? TakeNext()
    {
        int at = 0;
        while (at < Items.Count && Items[at].IsWaiting)
            at++;
        if (at == Items.Count)
            return null;
        if (at > 0)
            PassedOver?.Invoke(Items.Take(at).ToList());
        Track track = Items[at].Track;
        Items.RemoveAt(at);
        return track;
    }

    /// <summary>Songs still downloading that auto-play went past, to play the next song that's here.</summary>
    public event Action<IReadOnlyList<QueueItem>>? PassedOver;

    /// <summary>A song still on a server arrived: it takes the waiting row's place.</summary>
    public void Replace(QueueItem waiting, Track here)
    {
        int at = Items.IndexOf(waiting);
        if (at >= 0)
            Items[at] = new QueueItem(here);
    }

    /// <summary>A song taken from the queue is on a deck now: with <see cref="Repeat"/>, it goes back to the end.</summary>
    public void Took(Track track)
    {
        if (Repeat)
            Items.Add(new QueueItem(track));
    }

    /// <summary>Songs loaded from the queue onto a deck: they leave it (or, with <see cref="Repeat"/>, go to its end).</summary>
    public void Loaded(IReadOnlyList<QueueItem> items)
    {
        Remove(items);
        foreach (QueueItem item in items)
            Took(item.Track);
    }

    /// <summary>
    /// Puts the queue in a random order (#254), never the order it was in when there are two or
    /// more songs. Returns the order before, for <see cref="Reorder"/> to undo it.
    /// </summary>
    public IReadOnlyList<QueueItem> Shuffle(Random? random = null)
    {
        random ??= Random.Shared;
        var before = Items.ToList();
        if (before.Count < 2)
            return before;
        QueueItem[] order;
        do
        {
            order = [.. before];
            random.Shuffle(order);
        }
        while (order.SequenceEqual(before));
        Reorder(order);
        return before;
    }

    /// <summary>
    /// Puts the songs in <paramref name="order"/> in that order, at the top; any added since go
    /// after them, as they were. Songs taken out since stay out.
    /// </summary>
    public void Reorder(IReadOnlyList<QueueItem> order)
    {
        var present = Items.ToHashSet();
        var ordered = order.ToHashSet();
        var wanted = order.Where(present.Contains).Concat(Items.Where(i => !ordered.Contains(i))).ToList();
        for (int i = 0; i < wanted.Count; i++)
        {
            int at = Items.IndexOf(wanted[i]);
            if (at != i)
                Items.Move(at, i);
        }
    }

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
