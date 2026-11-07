using System.Collections.Specialized;
using Avalonia.Threading;
using GZJam.Library;
using GZJam.Library.Servers;

namespace GZJam.App.ViewModels;

/// <summary>
/// Songs downloaded on the spot (#290): for a deck, a double-click, a playlist, or the queue. One
/// download per song, shared by everyone waiting for it (a song queued and loaded at once comes
/// down once). The queue's downloads go two at a time, in its order, so a whole album queued
/// doesn't hold up the song a deck is waiting for, which goes at once. A queue row taken out of
/// the queue stops its download, unless something else is waiting for the same song.
/// </summary>
public sealed partial class ServersViewModel
{
    /// <summary>How many of the queue's songs download at once.</summary>
    public const int QueueDownloads = 2;

    private readonly SemaphoreSlim _queueSlots = new(QueueDownloads, QueueDownloads);
    private readonly Dictionary<(long Server, string Song), Download> _downloads = [];
    private readonly Dictionary<QueueItem, CancellationTokenSource> _queueStops = [];
    private readonly Dictionary<QueueItem, TaskCompletionSource> _queueHurry = [];
    private bool _watchingQueue;

    /// <summary>One song coming down, and how many are waiting for it.</summary>
    private sealed class Download(CancellationTokenSource stop)
    {
        public CancellationTokenSource Stop { get; } = stop;
        public Task<string> Done { get; set; } = Task.FromResult("");
        public int Waiting { get; set; }
    }

    /// <summary>How many downloads are coming down now, for tests.</summary>
    internal int DownloadsRunning => _downloads.Count;

    /// <summary>The downloads coming down now, to wait for (the downloads folder moving).</summary>
    private List<Task<string>> DownloadsUnderWay() => _downloads.Values.Select(d => d.Done).Where(t => !t.IsCompleted).ToList();

    /// <summary>
    /// The song on this computer: the copy here, or downloaded, sharing a download already under way.
    /// <paramref name="cancel"/> lets this caller go; the download stops only once nobody's waiting.
    /// </summary>
    private async Task<string> GetSongAsync(ServerItemViewModel item, SubsonicClient client, ServerSong song, CancellationToken cancel = default)
    {
        var key = (item.Server.Id, song.Id);
        if (!_downloads.TryGetValue(key, out Download? download))
        {
            download = new Download(new CancellationTokenSource());
            var sync = new ServerSync(_database, client, item.Server, DownloadsFolder) { Report = ReportTo(item, "Asked for now") };
            var stop = download.Stop.Token;
            download.Done = Task.Run(() => sync.GetOneAsync(song, stop), stop);
            _downloads[key] = download;
            var started = download;
            _ = started.Done.ContinueWith(_ => Dispatcher.UIThread.Post(() =>
            {
                if (_downloads.TryGetValue(key, out var current) && current == started)
                    _downloads.Remove(key);
                started.Stop.Dispose();
            }), TaskScheduler.Default);
        }
        download.Waiting++;
        bool left = false;
        try
        {
            return await download.Done.WaitAsync(cancel);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested && !download.Done.IsCompleted)
        {
            left = true;
            if (--download.Waiting == 0)
            {
                Log.Info($"Servers: nobody's waiting for {song.Title} from {item.Name} any more: its download stops");
                download.Stop.Cancel();
                _downloads.Remove(key);
            }
            throw;
        }
        finally
        {
            if (!left)
                download.Waiting--;
        }
    }

    /// <summary>
    /// The queue's turn: one of <see cref="QueueDownloads"/> places, held until <paramref name="work"/>
    /// is done; canceled if the row leaves the queue meanwhile.
    /// </summary>
    private async Task<bool> InQueueTurnAsync(QueueItem row, Func<CancellationToken, Task> work)
    {
        WatchQueue();
        var stop = new CancellationTokenSource();
        var hurry = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _queueStops[row] = stop;
        _queueHurry[row] = hurry;
        try
        {
            Task slot = _queueSlots.WaitAsync(stop.Token);
            bool held = await Task.WhenAny(slot, hurry.Task) == slot;
            if (held)
                await slot;
            else   // a deck wants it: now, without waiting for a place (one that comes later is given back)
                _ = slot.ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully)
                        _queueSlots.Release();
                }, TaskScheduler.Default);
            try
            {
                await work(stop.Token);
                return true;
            }
            finally
            {
                if (held)
                    _queueSlots.Release();
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            Log.Info($"Servers: {row.Title} left the queue before it came: not downloaded for it");
            return false;
        }
        finally
        {
            _queueStops.Remove(row);
            _queueHurry.Remove(row);
            stop.Dispose();
        }
    }

    /// <summary>GZJam is closing: every download stops, and none waiting starts.</summary>
    private void StopDownloads()
    {
        foreach (CancellationTokenSource stop in _queueStops.Values.ToList())
            stop.Cancel();
        foreach (Download download in _downloads.Values.ToList())
            download.Stop.Cancel();
        _downloads.Clear();
    }

    /// <summary>A deck is waiting for this queued song: it downloads now, ahead of the queue's turns.</summary>
    public void Hurry(Track track)
    {
        if (track.OnServer is { } remote)
            Downloads.ForADeck(remote.ServerId, remote.Song.Id);
        foreach (var (row, hurry) in _queueHurry)
            if (row.Track.Path == track.Path)
                hurry.TrySetResult();
    }

    /// <summary>A row taken out of the queue stops its download (checked once the change is over: a row dragged elsewhere is taken out, then put back).</summary>
    private void WatchQueue()
    {
        if (_watchingQueue)
            return;
        _watchingQueue = true;
        _owner.Queue.Items.CollectionChanged += (_, e) =>
        {
            if (e.Action is NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Replace or NotifyCollectionChangedAction.Reset)
                Dispatcher.UIThread.Post(() =>
                {
                    foreach (var (row, stop) in _queueStops.ToList())
                        if (!_owner.Queue.Items.Contains(row))
                            stop.Cancel();
                });
        };
    }
}
