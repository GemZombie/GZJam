using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GZJam.Library;
using GZJam.Library.Servers;

namespace GZJam.App.ViewModels;

public enum DownloadState
{
    Waiting,
    Downloading,
    Done,
    Failed,
    Stopped,
}

/// <summary>
/// Everything coming down from the servers, at a glance (#304, after Danny's own Downloads window):
/// each song from a sync, for the queue or a deck, Keep a copy in my library, or keeping a whole
/// server, saying why, how far along it is, and what failed and why. Above them, a line for each
/// sync or keep running, with its Stop. The last <see cref="FinishedKept"/> finished stay for the
/// session. Songs report from their download's own thread; the rows show it a few times a second.
/// </summary>
public sealed partial class DownloadsViewModel : ObservableObject
{
    /// <summary>How many finished songs stay in the list.</summary>
    public const int FinishedKept = 100;

    private readonly ServersViewModel _servers;
    private readonly Action _changed;
    private readonly object _gate = new();
    private readonly Dictionary<(long Server, string Song), DownloadItem> _active = [];   // under _gate: waiting or coming down
    private DispatcherTimer? _timer;

    internal DownloadsViewModel(ServersViewModel servers, Action changed)
    {
        _servers = servers;
        _changed = changed;
        Items.CollectionChanged += (_, _) => ListChanged();
        Batches.CollectionChanged += (_, _) => ListChanged();
    }

    /// <summary>The songs: those waiting or coming down first, then those finished, the latest first.</summary>
    public ObservableCollection<DownloadItem> Items { get; } = [];

    /// <summary>The syncs and keeps running, a line each.</summary>
    public ObservableCollection<DownloadBatch> Batches { get; } = [];

    public bool IsEmpty => Items.Count == 0 && Batches.Count == 0;

    public bool CanStopAll => Batches.Count > 0 || Items.Any(i => i.CanStop);

    public bool CanRetryFailed => Items.Any(i => i is { State: DownloadState.Failed, CanRetry: true });

    public bool CanClearFinished => Items.Any(i => i.IsFinished);

    /// <summary>"Downloading 3 songs…" for the top bar, while songs come down on their own (for the queue or a deck); null otherwise.</summary>
    [ObservableProperty]
    public partial string? Status { get; private set; }

    [ObservableProperty]
    public partial double Percent { get; private set; }

    partial void OnStatusChanged(string? value) => _changed();

    partial void OnPercentChanged(double value) => _changed();

    // ───────────── What reports here ─────────────

    /// <summary>
    /// A song asked for that comes once it's its turn (for the queue, a deck): a waiting row, or
    /// the one there already for it. <paramref name="stop"/> null: it can't be stopped (a deck
    /// waits for it), which holds for the row from then on.
    /// </summary>
    internal DownloadItem Expect(long serverId, string serverName, ServerSong song, string why, Action? stop, Func<Task>? retry)
    {
        DownloadItem row;
        bool added = false;
        lock (_gate)
        {
            if (!_active.TryGetValue((serverId, song.Id), out row!))
            {
                row = new DownloadItem(this, serverId, serverName, song, why)
                {
                    StopInstead = stop,
                    CanBeStopped = stop is not null,
                    Retry = retry is null ? null : _ => retry(),
                };
                _active[(serverId, song.Id)] = row;
                added = true;
            }
        }
        if (added)
            Add(row);
        else if (stop is null && row.CanBeStopped)
            row.Unstoppable(why);   // a deck waits for it now
        return row;
    }

    /// <summary>
    /// A song starting to come down (<see cref="ServerSync.Report"/>, on its thread): the row
    /// waiting for it, or a new one. Stop on a new row stops just this song.
    /// </summary>
    internal DownloadItem Begin(long serverId, string serverName, ServerSong song, string why,
        object? retryKey = null, Func<IReadOnlyList<DownloadItem>, Task>? retry = null)
    {
        DownloadItem row;
        bool added = false;
        lock (_gate)
        {
            if (!_active.TryGetValue((serverId, song.Id), out row!))
            {
                row = new DownloadItem(this, serverId, serverName, song, why) { RetryKey = retryKey ?? new object(), Retry = retry };
                _active[(serverId, song.Id)] = row;
                added = true;
            }
        }
        if (added)
            Dispatcher.UIThread.Post(() => Add(row));
        return row;
    }

    /// <summary>The row's song is no longer coming: taken out of what's active, so asking again makes a new row.</summary>
    internal void Release(DownloadItem row)
    {
        lock (_gate)
        {
            if (_active.TryGetValue((row.ServerId, row.Song.Id), out var current) && current == row)
                _active.Remove((row.ServerId, row.Song.Id));
        }
    }

    private void Add(DownloadItem row)
    {
        Items.Insert(ActiveCount(), row);
        WatchBytes();
    }

    /// <summary>A row finished: to the top of those finished, and the oldest past <see cref="FinishedKept"/> go.</summary>
    internal void Finished(DownloadItem row)
    {
        Release(row);
        int at = Items.IndexOf(row);
        if (at < 0)
            return;
        int top = ActiveCount();   // the rows still active, all above it once it's moved
        if (at != top)
            Items.Move(at, top);
        var finished = Items.Where(i => i.IsFinished).ToList();
        foreach (DownloadItem old in finished.Skip(FinishedKept))
            Items.Remove(old);
        ListChanged();
    }

    /// <summary>A row that never came down (its song was here already): gone from the list.</summary>
    internal void Drop(DownloadItem row)
    {
        Release(row);
        Items.Remove(row);
    }

    private int ActiveCount() => Items.Count(i => i.IsActive);

    // ───────────── The lines for syncs and keeps ─────────────

    internal DownloadBatch StartBatch(object key, string text, string stopText, Action stop)
    {
        if (Batches.FirstOrDefault(b => Equals(b.Key, key)) is { } running)
            return running;
        var batch = new DownloadBatch(key, text, stopText, stop);
        Batches.Add(batch);
        return batch;
    }

    internal void EndBatch(object key)
    {
        if (Batches.FirstOrDefault(b => Equals(b.Key, key)) is { } batch)
            Batches.Remove(batch);
    }

    // ───────────── The window's buttons ─────────────

    /// <summary>
    /// Stop all, the same as the top bar's Stop: syncs and keeps stop after the song coming down,
    /// a whole server pauses, and the queue's songs stop (a song a deck waits for still comes).
    /// </summary>
    [RelayCommand]
    public void StopAll() => _servers.StopInBackground();

    /// <summary>Each failed song tried again: a sync's with the sync again, a kept one kept again, the queue's for its place.</summary>
    [RelayCommand]
    public async Task RetryFailedAsync()
    {
        var failed = Items.Where(i => i is { State: DownloadState.Failed, CanRetry: true }).ToList();
        foreach (DownloadItem row in failed)
            Items.Remove(row);
        foreach (var group in failed.GroupBy(r => r.RetryKey))
            await group.First().Retry!(group.ToList());
    }

    [RelayCommand]
    public void ClearFinished()
    {
        foreach (DownloadItem row in Items.Where(i => i.IsFinished).ToList())
            Items.Remove(row);
    }

    /// <summary>Whether songs are coming for the queue that the top bar's Stop stops.</summary>
    internal bool HasQueueRows => Items.Any(i => i.IsActive && i.StopInstead is not null);

    /// <summary>A deck waits for this song now: its row can't be stopped.</summary>
    internal void ForADeck(long serverId, string songId)
    {
        DownloadItem? row;
        lock (_gate)
            _active.TryGetValue((serverId, songId), out row);
        row?.Unstoppable("For a deck");
    }

    /// <summary>The queue's rows the top bar's Stop stops: those no deck waits for.</summary>
    internal void StopQueueRows()
    {
        foreach (DownloadItem row in Items.Where(i => i.IsActive && i.StopInstead is not null).ToList())
            row.Stop();
    }

    // ───────────── Showing it ─────────────

    /// <summary>Bytes arrive many times a second: the rows and the top bar show them four times a second, while any come.</summary>
    private void WatchBytes()
    {
        _timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Tick());
        if (!_timer.IsEnabled)
            _timer.Start();
        Tick();
    }

    private void Tick()
    {
        var coming = Items.Where(i => i.IsActive).ToList();
        foreach (DownloadItem row in coming)
            row.Refresh();
        int count = coming.Count;
        Status = count == 0 ? null : count == 1 ? "Downloading 1 song…" : $"Downloading {Format.Count(count)} songs…";
        long total = coming.Sum(r => r.Total ?? 0), received = coming.Where(r => r.Total is not null).Sum(r => r.ReceivedBytes);
        Percent = total > 0 ? 100.0 * received / total : 0;
        if (count == 0)
            _timer?.Stop();
    }

    internal void ListChanged()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanStopAll));
        OnPropertyChanged(nameof(CanRetryFailed));
        OnPropertyChanged(nameof(CanClearFinished));
        Tick();
        if (Items.Any(i => i.IsActive))
            WatchBytes();
    }
}

/// <summary>One song in the Downloads window, told how its download goes (<see cref="IDownloadReport"/>).</summary>
public sealed partial class DownloadItem : ObservableObject, IDownloadReport
{
    private readonly DownloadsViewModel _list;
    private readonly CancellationTokenSource _skip = new();
    private long _received, _shown = -1;
    private long _size;   // 0 until known
    private int _started;

    internal DownloadItem(DownloadsViewModel list, long serverId, string serverName, ServerSong song, string why)
    {
        _list = list;
        ServerId = serverId;
        ServerName = serverName;
        Song = song;
        Why = why;
        _size = song.Size is > 0 and var s ? s : 0;
    }

    public long ServerId { get; }
    public string ServerName { get; }
    public ServerSong Song { get; }
    public string Title => Song.Title is { Length: > 0 } title ? title : Song.Id;
    public string Artist => Song.Artist ?? "";

    /// <summary>"Sync: Friday", "For the queue", "Keep a copy in my library"…</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WhyText))]
    public partial string Why { get; private set; }

    /// <summary>"For the queue · navidrome".</summary>
    public string WhyText => $"{Why} · {ServerName}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive), nameof(IsFinished), nameof(IsDownloading), nameof(CanStop), nameof(StateText),
        nameof(IsProblem), nameof(SizeText))]
    public partial DownloadState State { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    public partial string? Problem { get; private set; }

    public bool IsActive => State is DownloadState.Waiting or DownloadState.Downloading;
    public bool IsFinished => !IsActive;
    public bool IsDownloading => State == DownloadState.Downloading;
    public bool IsProblem => State == DownloadState.Failed;

    /// <summary>Its size, once known.</summary>
    public long? Total => Volatile.Read(ref _size) is { } size and > 0 ? size : null;

    public long ReceivedBytes => Volatile.Read(ref _received);

    public double Percent => Total is { } total ? Math.Min(100, 100.0 * ReceivedBytes / total) : 0;

    /// <summary>"4.2 of 9.8 MB" while it comes; its size once it's here.</summary>
    public string SizeText => State switch
    {
        DownloadState.Downloading => Total is { } total ? $"{Mb(ReceivedBytes)} of {Mb(total)} MB" : $"{Mb(ReceivedBytes)} MB",
        DownloadState.Done or DownloadState.Waiting => Total is { } size ? $"{Mb(size)} MB" : "",
        _ => "",
    };

    public string StateText => State switch
    {
        DownloadState.Waiting => "Waiting",
        DownloadState.Downloading => "Downloading",
        DownloadState.Done => "Done",
        DownloadState.Failed => Problem is { Length: > 0 } why ? $"Couldn't: {why}" : "Couldn't download it",
        _ => "Stopped",
    };

    private static string Mb(long bytes) => (bytes / (double)(1 << 20)).ToString("0.0", CultureInfo.CurrentCulture);

    // ───────────── Stopping and trying again ─────────────

    /// <summary>What Stop does instead of stopping just this song (the queue's: its place stops waiting for it).</summary>
    internal Action? StopInstead { get; set; }

    /// <summary>Whether Stop is offered at all: not for a song a deck waits for.</summary>
    internal bool CanBeStopped { get => _canBeStopped; init => _canBeStopped = value; }

    private bool _canBeStopped = true;

    public bool CanStop => IsActive && CanBeStopped;

    /// <summary>Failed songs with the same key are tried again together (a sync's, by syncing again).</summary>
    internal object RetryKey { get; set; } = new();

    internal Func<IReadOnlyList<DownloadItem>, Task>? Retry { get; set; }

    public bool CanRetry => Retry is not null;

    /// <summary>Stop on its row: this song stops (what else is coming goes on).</summary>
    [RelayCommand]
    public void Stop()
    {
        if (!CanStop)
            return;
        if (StopInstead is { } instead)
            instead();
        else
            _skip.Cancel();
    }

    internal void Unstoppable(string why)
    {
        _canBeStopped = false;
        StopInstead = null;
        Why = why;
        OnPropertyChanged(nameof(CanStop));
        _list.ListChanged();
    }

    // ───────────── Told how it goes (any thread) ─────────────

    public CancellationToken Skip => _skip.Token;

    /// <summary>Whether it started coming down (so it's told how it ends).</summary>
    internal bool HasStarted => Volatile.Read(ref _started) == 1;

    public void Started(long? size)
    {
        Volatile.Write(ref _started, 1);
        Volatile.Write(ref _received, 0);
        if (size is > 0)
            Volatile.Write(ref _size, size.Value);
        Dispatcher.UIThread.Post(() =>
        {
            if (IsActive)
                State = DownloadState.Downloading;
        });
    }

    public void Received(long bytes) => Volatile.Write(ref _received, bytes);

    public void Ended(DownloadEnd end, string? problem = null)
    {
        Volatile.Write(ref _started, 1);
        Dispatcher.UIThread.Post(() => Finish(end switch
        {
            DownloadEnd.Arrived => DownloadState.Done,
            DownloadEnd.Failed => DownloadState.Failed,
            _ => DownloadState.Stopped,
        }, end == DownloadEnd.Failed ? problem : null));
    }

    /// <summary>
    /// Its asker is done with it (on the window's thread): a row that never started coming down
    /// is gone if its song was here already, else it ends as <paramref name="state"/>.
    /// </summary>
    internal void Settle(DownloadState? state, string? problem = null)
    {
        if (!IsActive || HasStarted)
            return;
        if (state is { } ended)
            Finish(ended, problem);
        else
            _list.Drop(this);
    }

    private void Finish(DownloadState state, string? problem)
    {
        if (!IsActive)
            return;
        if (state == DownloadState.Done && Total is { } total)
            Volatile.Write(ref _received, total);
        Problem = problem;
        State = state;
        Refresh();
        _list.Finished(this);
    }

    /// <summary>The bytes so far, shown (four times a second at most).</summary>
    internal void Refresh()
    {
        long now = ReceivedBytes;
        if (now == _shown)
            return;
        _shown = now;
        OnPropertyChanged(nameof(ReceivedBytes));
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(Percent));
        OnPropertyChanged(nameof(SizeText));
    }
}

/// <summary>A sync or a keep running, in the Downloads window: what, how far, and its Stop (or Pause).</summary>
public sealed partial class DownloadBatch(object key, string text, string stopText, Action stop) : ObservableObject
{
    internal object Key { get; } = key;

    /// <summary>"Syncing navidrome", "Keeping all of navidrome in your library".</summary>
    [ObservableProperty]
    public partial string Text { get; set; } = text;

    /// <summary>"120 of 800 songs looked at · 680 to come".</summary>
    [ObservableProperty]
    public partial string Detail { get; set; } = "";

    [ObservableProperty]
    public partial double Percent { get; set; }

    public string StopText { get; } = stopText;

    [RelayCommand]
    public void Stop() => stop();
}
