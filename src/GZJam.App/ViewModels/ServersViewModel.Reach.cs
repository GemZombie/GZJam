using System.Net.NetworkInformation;
using Avalonia.Threading;
using GZJam.Library;
using GZJam.Library.Servers;

namespace GZJam.App.ViewModels;

/// <summary>
/// Whether each server answers, and the queue's songs still on a server (#290). The catalog is on
/// the laptop, so an unreachable server's folders and songs still browse; GZJam just says so at
/// once rather than trying, and a song queued from it waits, and arrives by itself once it answers.
/// </summary>
public sealed partial class ServersViewModel
{
    private DispatcherTimer? _recheck;
    private bool _watchingNetwork;
    private bool _onlyHereByItself;

    /// <summary>How often an unreachable server is asked again, quietly.</summary>
    public TimeSpan RecheckEvery { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>How long a quick check waits for an answer.</summary>
    public TimeSpan QuickCheck { get; set; } = TimeSpan.FromSeconds(3);

    public bool IsOffline(long serverId) => ItemFor(serverId)?.IsOffline == true;

    /// <summary>
    /// Asks each server whether it answers (at startup, when the network changes): quickly, and
    /// without a word for one that doesn't, beyond marking it.
    /// </summary>
    public async Task CheckReachableAsync()
    {
        if (Items.Count > 0)
            WatchNetwork();   // only with a server to ask
        foreach (ServerItemViewModel item in Items.ToList())
            await CheckReachableAsync(item);
    }

    private async Task CheckReachableAsync(ServerItemViewModel item)
    {
        if (TryGetPassword(item.Server.PasswordKey, out _) is not { } password)
            return;
        using var quick = new CancellationTokenSource(QuickCheck);
        ServerCheck check;
        try
        {
            check = await CheckServer(item.Server.Address, item.Server.UserName, password, quick.Token);
        }
        catch (OperationCanceledException)
        {
            check = new ServerCheck(ServerCheckOutcome.Unreachable);
        }
        if (check.Outcome == ServerCheckOutcome.Unreachable)
            MarkOffline(item);
        else
            MarkOnline(item);
    }

    /// <summary>A request to the server failed for want of an answer: it's offline until it answers again.</summary>
    internal void MarkOffline(ServerItemViewModel item)
    {
        if (item.IsOffline)
            return;
        item.GoOffline(DateTime.Now);
        Log.Info($"Servers: {item.Name} can't be reached");
        Changed(item, offline: true);
        if (_closed)
            return;
        _recheck ??= new DispatcherTimer(RecheckEvery, DispatcherPriority.Background, async (_, _) => await RecheckOfflineAsync());
        _recheck.Start();
    }

    private void MarkOnline(ServerItemViewModel item)
    {
        if (!item.IsOffline)
            return;
        item.GoOnline();
        Log.Info($"Servers: {item.Name} answers again");
        Changed(item, offline: false);
        if (Items.All(i => !i.IsOffline))
            _recheck?.Stop();
        foreach (QueueItem waiting in _owner.Queue.Items.Where(q => q.Track.OnServer?.ServerId == item.Server.Id).ToList())
            FetchForQueue(waiting);
        StartKeepWhole(item);
    }

    /// <summary>The tree's mark, the song list's rows, and "Only what's on this laptop" follow a server going or coming back.</summary>
    private void Changed(ServerItemViewModel item, bool offline)
    {
        RefreshArtServers();
        if (!offline)
            _owner.Art.ForgetMissingServerArt();   // its pictures can come now
        var library = _owner.Library;
        library.SetServerOffline(item.Server.Id, offline);
        bool showing = library.SelectedFolder is { IsServer: true, Node.ServerId: long id } && id == item.Server.Id;
        if (offline && showing && !library.OnlyHere)
        {
            library.OnlyHere = true;   // by itself: what can be played tonight
            _onlyHereByItself = true;
        }
        else if (!offline && _onlyHereByItself)
        {
            library.OnlyHere = false;
            _onlyHereByItself = false;
        }
        library.RefreshResults();
    }

    private async Task RecheckOfflineAsync()
    {
        foreach (ServerItemViewModel item in Items.Where(i => i.IsOffline).ToList())
            await CheckReachableAsync(item);
    }

    private NetworkAvailabilityChangedEventHandler? _availabilityChanged;
    private NetworkAddressChangedEventHandler? _addressChanged;
    private bool _closed;

    /// <summary>The network came or went (Wi-Fi joined, a cable unplugged): ask the servers again.</summary>
    private void WatchNetwork()
    {
        if (_watchingNetwork || _closed)
            return;
        _watchingNetwork = true;
        void Again() => Dispatcher.UIThread.Post(async () =>
        {
            if (!_closed)
                await CheckReachableAsync();
        });
        _availabilityChanged = (_, _) => Again();
        _addressChanged = (_, _) => Again();
        NetworkChange.NetworkAvailabilityChanged += _availabilityChanged;
        NetworkChange.NetworkAddressChanged += _addressChanged;
    }

    /// <summary>
    /// GZJam is closing: let go of the network's events and the recheck timer, which would
    /// otherwise keep everything alive (the events are the whole process's, not this window's).
    /// </summary>
    public void Close()
    {
        _closed = true;
        StopDownloads();
        foreach (CancellationTokenSource run in _wholeRuns.Values.ToList())
            run.Cancel();
        _recheck?.Stop();
        _recheck = null;
        if (_availabilityChanged is not null)
            NetworkChange.NetworkAvailabilityChanged -= _availabilityChanged;
        if (_addressChanged is not null)
            NetworkChange.NetworkAddressChanged -= _addressChanged;
        _availabilityChanged = null;
        _addressChanged = null;
    }

    // ───────────── The queue's songs still on a server ─────────────

    private readonly HashSet<QueueItem> _fetching = [];

    /// <summary>A queued song still on a server is here now (its path on the server, and its new row).</summary>
    public event Action<string, QueueItem>? Arrived;

    /// <summary>Whether this song, still on a server, is being downloaded for the queue now.</summary>
    public bool IsFetching(Track track) => _fetching.Any(q => q.Track.Path == track.Path);

    /// <summary>
    /// Brings a queued song still on a server onto the laptop, for its place in the queue: it says
    /// "Downloading…", then becomes the song itself, in the same place. While its server can't be
    /// reached it says "Waiting for synology" and comes when the server answers; a download that
    /// fails another way says so, with Try again. Never taken out of the queue by itself.
    /// </summary>
    public async void FetchForQueue(QueueItem item)
    {
        if (item.Track.OnServer is not { } remote || _fetching.Contains(item))
            return;
        if (ItemFor(remote.ServerId) is not { } server)
        {
            item.ShowServerState("That server isn't in Settings › Servers any more", problem: true, canRetry: false);
            return;
        }
        if (server.IsOffline)
        {
            item.ShowServerState($"Waiting for {server.Name}", problem: false, canRetry: false);
            return;
        }
        if (server.IsPreviewOnly || SignedIn(server) is not { } client)
        {
            item.ShowServerState(server.IsPreviewOnly ? $"{server.Name} doesn't allow downloads" : "Its server's password isn't on this computer",
                problem: true, canRetry: false);
            return;
        }

        _fetching.Add(item);
        item.ShowServerState(_queueSlots.CurrentCount > 0 ? "Downloading…" : "Waiting its turn…", problem: false, canRetry: false);
        try
        {
            await InQueueTurnAsync(item, async stop =>
            {
                item.ShowServerState("Downloading…", problem: false, canRetry: false);
                await EnsureDownloadsInLibraryAsync();
                AddChoices(server, [new ServerChoice(ServerChoiceKind.Song, remote.Song.Id,
                    remote.Song.Artist is { Length: > 0 } artist ? $"{item.Title} ({artist})" : item.Title)]);
                string path = await GetSongAsync(server, client, remote.Song, stop);
                Track here = await Task.Run(() => _database.TrackForFile(WhereNow(server, remote.Song.Id, path)));   // kept meanwhile, say
                Log.Info($"Servers: {item.Title} from {server.Name} is here, for the queue");
                _owner.Queue.Replace(item, here);
                if (_owner.Queue.Items.FirstOrDefault(q => q.Track == here) is { } arrived)
                    Arrived?.Invoke(remote.PseudoPath, arrived);
                AfterSync();
                RefreshKept(server);
                RefreshDownloads();
            });
        }
        catch (ServerException e) when (e.Outcome == ServerCheckOutcome.Unreachable)
        {
            MarkOffline(server);
            item.ShowServerState($"Waiting for {server.Name}", problem: false, canRetry: false);
        }
        catch (Exception e) when (e is ServerException or IOException or UnauthorizedAccessException)
        {
            Log.Info($"Servers: couldn't download {item.Title} from {server.Name} for the queue: {e.Message}");
            item.ShowServerState($"Couldn't download: {e.Message}", problem: true, canRetry: true);
        }
        catch (Exception) when (_closed)
        {
            // GZJam is closing: the download stopped with it.
        }
        catch (Exception e)
        {
            // Never the end of GZJam (this runs on its own): the row says so, and can try again.
            Log.Error($"Servers: downloading {item.Title} from {server.Name} for the queue failed", e);
            item.ShowServerState($"Couldn't download: {e.Message}", problem: true, canRetry: true);
        }
        finally
        {
            _fetching.Remove(item);
        }
    }

    /// <summary>A song from the saved night (or anywhere) by its <see cref="ServerTrack.PseudoPath"/>, as a row still on its server; null if it's gone.</summary>
    public Track? TrackFor(string pseudoPath)
    {
        if (ServerTrack.Parse(pseudoPath) is not var (serverId, songId) || ItemFor(serverId) is not { } server
            || _database.GetServerCatalogSong(serverId, songId) is not { } song)
            return null;
        return new ServerTrack(serverId, server.Name, song) { Unreachable = server.IsOffline }.ToTrack();
    }
}

public sealed partial class ServerItemViewModel
{
    /// <summary>The server didn't answer the last time it was asked.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    public partial bool IsOffline { get; private set; }

    public DateTime? OfflineSince { get; private set; }

    /// <summary>"synology can't be reached (since 9:40 PM). Songs on this laptop play as usual."</summary>
    public string OfflineTip => OfflineSince is { } since
        ? $"{Name} can't be reached (since {since.ToString("t", System.Globalization.CultureInfo.CurrentCulture)}). Songs on this laptop play as usual."
        : "";

    internal void GoOffline(DateTime since)
    {
        OfflineSince = since;
        IsOffline = true;
    }

    internal void GoOnline()
    {
        OfflineSince = null;
        IsOffline = false;
    }
}
