using CommunityToolkit.Mvvm.ComponentModel;
using GZJam.Library;
using GZJam.Library.Servers;

namespace GZJam.App.ViewModels;

/// <summary>
/// Keeping a whole server in the DJ's library, in the background (#285): every song of it comes
/// into the music folder for kept songs, a song at a time, by the same rules as Keep a copy in my
/// library (songs the DJ has already are left as they are, downloaded ones move, nothing is
/// replaced). It waits while the room hears music (unless the DJ allows it), can be held to a
/// speed, paused and resumed, and picks up where it left off when GZJam starts again. Songs added
/// to the server later come too, once its song list is read again. It takes turns with syncs,
/// so it never holds one up for long.
/// </summary>
public sealed partial class ServersViewModel
{
    private readonly Dictionary<long, CancellationTokenSource> _wholeRuns = [];

    /// <summary>How often a whole server waiting for the room to go quiet looks again.</summary>
    public TimeSpan RoomCheckEvery { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The top bar's line while a whole server is being kept (not while it waits for a quiet room); null otherwise.</summary>
    [ObservableProperty]
    public partial string? KeepWholeStatus { get; private set; }

    [ObservableProperty]
    public partial double KeepWholePercent { get; private set; }

    partial void OnKeepWholeStatusChanged(string? value) => _owner.BackgroundChanged();

    partial void OnKeepWholePercentChanged(double value) => _owner.BackgroundChanged();

    public bool IsKeepingWhole(long serverId) => _settings.ServerKeepWhole.Contains(serverId);

    public bool IsKeepWholePaused(long serverId) => _settings.ServerKeepWholePaused.Contains(serverId);

    /// <summary>For the server's node in the tree.</summary>
    public (bool Keeping, bool Paused) KeepWholeState(long serverId) => (IsKeepingWhole(serverId), IsKeepWholePaused(serverId));

    /// <summary>Whether a pass over the server is running now, for tests.</summary>
    internal bool IsKeepWholeRunning(long serverId) => _wholeRuns.ContainsKey(serverId);

    public IReadOnlyList<Choice<double?>> KeepSpeedChoices { get; } =
    [
        new(null, "As fast as the network goes"),
        new(0.5, "0.5 MB a second"),
        new(1, "1 MB a second"),
        new(2, "2 MB a second"),
        new(5, "5 MB a second"),
        new(10, "10 MB a second"),
    ];

    /// <summary>The fastest a whole server comes (Settings › Servers), from the next song on.</summary>
    public Choice<double?> KeepSpeed
    {
        get => KeepSpeedChoices.FirstOrDefault(c => c.Value == _settings.ServerKeepSpeedMBps) ?? KeepSpeedChoices[0];
        set
        {
            _settings.ServerKeepSpeedMBps = value?.Value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// A server is being kept whole: Settings › Servers shows how fast, and whether it goes on
    /// while a song plays. Until then those two rows are left out: they do nothing, and in among
    /// the others they made no sense on their own.
    /// </summary>
    public bool KeepsAnyWhole => Items.Any(i => IsKeepingWhole(i.Server.Id));

    /// <summary>Keeping a whole server goes on while a song plays to the speakers (Settings › Servers); off: it pauses.</summary>
    public bool KeepWhilePlaying
    {
        get => _settings.ServerKeepWhilePlaying;
        set
        {
            _settings.ServerKeepWhilePlaying = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Right-click the server › Keep the whole server in my library…: asks, showing how much and where, then starts.</summary>
    public async Task KeepWholeServerAsync(long serverId)
    {
        if (ItemFor(serverId) is not { } item)
            return;
        if (item.IsPreviewOnly)
        {
            _owner.ShowNotice($"{item.Name} doesn't let this account download songs: they can be heard in the headphones only.", seconds: 8);
            return;
        }
        if (_database.GetServerCatalogCount(serverId) == 0)
        {
            _owner.ShowNotice($"GZJam hasn't read {item.Name}'s song list yet: right-click it › Read its song list again, then try.", seconds: 8);
            return;
        }
        if (await KeepFolderToUseAsync() is not var (folder, choosing))
            return;

        var songs = await Task.Run(() => _database.SearchServerCatalog(serverId, "", null).Songs.Select(s => (s.Id, (ServerSong?)s)).ToList());
        var onDecks = _owner.Decks.Select(d => d.Track?.Path).OfType<string>().ToHashSet(StringComparer.Ordinal);
        KeepPlan plan = await Task.Run(() => PlanKeep(item, songs, folder, onDecks));
        int count = plan.Moves.Count + plan.Downloads.Count + plan.OnDeck.Count;
        if (count == 0)
        {
            _owner.ShowNotice($"All of {item.Name}'s songs are in your library already.", seconds: 6);
            return;
        }

        long bytes = plan.Downloads.Sum(d => d.Song?.Size ?? 0);
        var lines = new List<string>
        {
            $"{SongCount(count)} into {folder}, laid out as on {item.Name}{(bytes > 0 ? $": {Format.Size(bytes)} to download" : "")}.",
        };
        if (plan.Moves.Count > 0)
            lines.Add($"{SongCount(plan.Moves.Count)} downloaded already {(plan.Moves.Count == 1 ? "moves" : "move")} there first, with their hearts, cues, labels, playlists and plays.");
        if (plan.Mine > 0)
            lines.Add($"{SongCount(plan.Mine)} in your library already {(plan.Mine == 1 ? "is" : "are")} left as {(plan.Mine == 1 ? "it is" : "they are")}.");
        if (DriveOf(folder)?.AvailableFreeSpace is { } free)
            lines.Add(free < bytes + (1L << 30)
                ? $"There's {Format.Size(free)} free on that drive: not enough for all of them, so it stops when the drive is nearly full."
                : $"There's {Format.Size(free)} free on that drive.");
        lines.Add("");
        lines.Add($"It goes on in the background, a song at a time{(KeepSpeed.Value is { } mb ? $", at most {mb:0.#} MB a second" : "")}, "
            + (KeepWhilePlaying ? "even while a song plays to the speakers" : "pausing while a song plays to the speakers")
            + $" (Settings › Servers changes both, once it's started). It picks up where it left off when GZJam starts again, and songs added to {item.Name} later come too. Right-click the server to pause or stop it.");
        if (_owner.ConfirmAsync is null || !await _owner.ConfirmAsync($"Keep all of {item.Name} in your library?",
                string.Join(Environment.NewLine, lines), "Keep all"))
            return;
        if (choosing && !SetKeepFolder(folder))
            return;

        if (!_settings.ServerKeepWhole.Contains(serverId))
            _settings.ServerKeepWhole.Add(serverId);
        _settings.ServerKeepWholePaused.Remove(serverId);
        Log.Info($"Servers: keeping all of {item.Name} in {folder}, in the background");
        TreeKeepWhole(serverId);
        _owner.ShowNotice($"Keeping all of {item.Name} in your library, in the background.", seconds: 5);
        StartKeepWhole(item);
    }

    /// <summary>Pause keeping the whole server: the song coming now stops, and it waits until resumed (after a restart too).</summary>
    public void PauseKeepWhole(long serverId)
    {
        if (!IsKeepingWhole(serverId) || ItemFor(serverId) is not { } item)
            return;
        if (!_settings.ServerKeepWholePaused.Contains(serverId))
            _settings.ServerKeepWholePaused.Add(serverId);
        _wholeRuns.GetValueOrDefault(serverId)?.Cancel();
        Log.Info($"Servers: keeping all of {item.Name} paused");
        TreeKeepWhole(serverId);
        _owner.ShowNotice($"Paused keeping all of {item.Name}: Resume picks up where it left off.", seconds: 5);
    }

    public void ResumeKeepWhole(long serverId)
    {
        if (!IsKeepingWhole(serverId) || ItemFor(serverId) is not { } item)
            return;
        _settings.ServerKeepWholePaused.Remove(serverId);
        Log.Info($"Servers: keeping all of {item.Name} resumed");
        TreeKeepWhole(serverId);
        if (item.IsOffline)
            _owner.ShowNotice($"{item.Name} can't be reached right now: its songs come once it answers.", seconds: 6);
        StartKeepWhole(item);
    }

    /// <summary>Stop keeping the whole server: after asking; the songs kept so far stay the DJ's.</summary>
    public async Task StopKeepWholeAsync(long serverId)
    {
        if (!IsKeepingWhole(serverId) || ItemFor(serverId) is not { } item)
            return;
        if (_owner.ConfirmAsync is null || !await _owner.ConfirmAsync($"Stop keeping all of {item.Name}?",
                "The songs kept so far stay in your library, as your own. You can keep more of it, or all of it again, at any time.", "Stop"))
            return;
        ForgetKeepWhole(serverId);
        Log.Info($"Servers: no longer keeping all of {item.Name}");
    }

    private void ForgetKeepWhole(long serverId)
    {
        _settings.ServerKeepWhole.Remove(serverId);
        _settings.ServerKeepWholePaused.Remove(serverId);
        _wholeRuns.GetValueOrDefault(serverId)?.Cancel();
        TreeKeepWhole(serverId);
    }

    private void TreeKeepWhole(long serverId)
    {
        _owner.Library?.SetServerKeepWhole(serverId, IsKeepingWhole(serverId), IsKeepWholePaused(serverId));
        OnPropertyChanged(nameof(KeepsAnyWhole));
    }

    /// <summary>
    /// Each server being kept whole goes on, if it isn't paused and answers: when GZJam starts,
    /// when a server answers again, and when its song list is read (songs added there). A server
    /// no longer in Settings › Servers is forgotten.
    /// </summary>
    public void ResumeKeepingWhole()
    {
        foreach (long gone in _settings.ServerKeepWhole.Where(id => ItemFor(id) is null).ToList())
            ForgetKeepWhole(gone);
        foreach (ServerItemViewModel item in Items.ToList())
            StartKeepWhole(item);
    }

    private void StartKeepWhole(ServerItemViewModel item)
    {
        long id = item.Server.Id;
        if (_closed || !IsKeepingWhole(id) || IsKeepWholePaused(id) || _wholeRuns.ContainsKey(id) || item.IsOffline || item.IsPreviewOnly)
            return;
        var stop = new CancellationTokenSource();
        _wholeRuns[id] = stop;
        _ = KeepWholePassAsync(item, stop);
    }

    /// <summary>
    /// Once over the server: what isn't the DJ's yet, kept a song at a time, each taking its turn
    /// with syncs and moves. Stops when paused, when the server stops answering (it goes on when
    /// it answers), or when the drive is nearly full (paused, and said).
    /// </summary>
    private async Task KeepWholePassAsync(ServerItemViewModel item, CancellationTokenSource stop)
    {
        long id = item.Server.Id;
        int kept = 0, failed = 0, total = 0;
        long bytes = 0;
        var moved = new List<(string From, string To)>();
        string? folder = KeepFolder;
        try
        {
            if (folder is null || (Path.GetPathRoot(folder) is { } drive && !Directory.Exists(drive)))
            {
                Log.Info($"Servers: keeping all of {item.Name} waits: {(folder is null ? "no folder is chosen for kept songs" : $"{folder} isn't there")}");
                return;
            }
            if (SignedIn(item) is not { } client || _database.GetServerCatalogCount(id) == 0)
                return;
            Directory.CreateDirectory(folder);
            if (RootOf(folder) is null)
                await _owner.Library.AddRootAsync(folder);

            var songs = await Task.Run(() => _database.SearchServerCatalog(id, "", null).Songs.Select(s => (s.Id, (ServerSong?)s)).ToList(), stop.Token);
            var onDecks = _owner.Decks.Select(d => d.Track?.Path).OfType<string>().ToHashSet(StringComparer.Ordinal);
            string keepIn = folder;
            KeepPlan plan = await Task.Run(() => PlanKeep(item, songs, keepIn, onDecks), stop.Token);
            var steps = plan.Moves.Concat(plan.Downloads).ToList();
            total = steps.Count;
            if (total == 0)
            {
                Log.Info($"Servers: all of {item.Name} is in your library");
                return;
            }
            Log.Info($"Servers: keeping all of {item.Name}: {Format.Count(plan.Moves.Count)} to move, {Format.Count(plan.Downloads.Count)} to download");
            var batch = Downloads.StartBatch(("whole", id), $"Keeping all of {item.Name} in your library", "Pause", () => PauseKeepWhole(id));
            var report = ReportTo(item, "Keeping the whole server", ("whole", id), _ =>
            {
                StartKeepWhole(item);
                return Task.CompletedTask;
            });

            for (int i = 0; i < steps.Count; i++)
            {
                await WaitForQuietRoomAsync(batch, stop.Token);
                await _oneSync.WaitAsync(stop.Token);
                try
                {
                    KeepWholeStatus = $"Keeping all of {item.Name} in your library… {Format.Count(i)} / {Format.Count(total)}";
                    KeepWholePercent = 100.0 * i / total;
                    batch.Detail = $"{Format.Count(i)} of {Format.Count(total)} songs";
                    batch.Percent = KeepWholePercent;
                    KeepStep step = steps[i];
                    if (_database.GetServerSong(id, step.SongId) is { OwnCopy: true } mine && File.Exists(mine.Path))
                        continue;   // kept meanwhile
                    if (File.Exists(step.Target))
                        step = step with { Target = Untaken(step.Target) };
                    var sync = new ServerSync(_database, client, item.Server, DownloadsFolder)
                    {
                        BytesPerSecond = KeepSpeed.Value is { } mb ? (long)(mb * 1_000_000) : null,
                        Report = report,
                    };
                    var (move, got) = await KeepOneAsync(item, step, sync, keepIn, stop.Token);
                    if (move is { } made)
                        moved.Add(made);
                    bytes += got;
                    kept++;
                }
                catch (OperationCanceledException) when (!stop.IsCancellationRequested)
                {
                    failed++;   // just this song stopped (the Downloads window): tried again next time
                }
                catch (ServerException e) when (e.Outcome == ServerCheckOutcome.Unreachable)
                {
                    MarkOffline(item);   // goes on once it answers
                    break;
                }
                catch (IOException e) when (e.Message == ServerSync.DiskNearlyFull)
                {
                    if (!_settings.ServerKeepWholePaused.Contains(id))
                        _settings.ServerKeepWholePaused.Add(id);
                    _owner.ShowNotice($"Keeping all of {item.Name} is paused: the drive {keepIn} is on is nearly full. Make room, then right-click the server › Resume.", seconds: 10);
                    break;
                }
                catch (Exception e) when (e is ServerException or IOException or UnauthorizedAccessException)
                {
                    failed++;   // tried again next time
                    Log.Info($"Servers: couldn't keep {steps[i].Title} from {item.Name}: {e.Message}");
                }
                finally
                {
                    _oneSync.Release();
                }
                if (moved.Count > 0 && moved.Count % 100 == 0)
                    _owner.Library.FollowMoves(moved.GetRange(moved.Count - 100, 100));   // the queue and decks keep up
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Paused, stopped, or GZJam closing: each song kept so far is kept.
        }
        catch (Exception e) when (!_closed)
        {
            // Never the end of GZJam (this runs on its own): it's tried again next time.
            Log.Error($"Servers: keeping all of {item.Name} failed", e);
        }
        finally
        {
            if (_wholeRuns.GetValueOrDefault(id) == stop)
                _wholeRuns.Remove(id);
            Downloads.EndBatch(("whole", id));
            stop.Dispose();
            if (_wholeRuns.Count == 0)
                KeepWholeStatus = null;
            if (kept > 0 || failed > 0)
                Log.Info($"Servers: kept {Format.Count(kept)} of {Format.Count(total)} songs of {item.Name} in your library "
                    + $"({Format.Size(bytes)} downloaded){(failed > 0 ? $"; {failed} couldn't be, tried again next time" : "")}");
            TreeKeepWhole(id);
        }
        if (_closed || kept == 0)
            return;
        if (moved.Count % 100 is var left and > 0)
            _owner.Library.FollowMoves(moved.GetRange(moved.Count - left, left));
        if (folder is not null && RootOf(folder) is { } keepRoot)
            await _owner.Library.ScanRootAsync(keepRoot.Id);
        if (moved.Count > 0 && RootOf(DownloadsFolder) is { } downloadsRoot && downloadsRoot.Id != RootOf(folder!)?.Id)
            _owner.Library.RescanRoot(downloadsRoot.Id);
        _owner.Library.RefreshPlaylists();
        RefreshKept(item);
        RefreshDownloads();
        _owner.Library.RefreshResults();
        if (kept == total)
            _owner.ShowNotice($"All of {item.Name} is in your library now. Songs added to it later come too.", seconds: 6);
    }

    /// <summary>Waits while the room hears music, unless the DJ allows keeping a server then; the top bar says nothing meanwhile.</summary>
    private async Task WaitForQuietRoomAsync(DownloadBatch batch, CancellationToken stop)
    {
        bool waited = false;
        while (!_settings.ServerKeepWhilePlaying && _owner.WhatTheRoomIsHearing() is not null)
        {
            if (!waited)
            {
                waited = true;
                KeepWholeStatus = null;
                batch.Detail = "Waiting while the room hears music";
                Log.Info("Servers: keeping a whole server waits while the room hears music");
            }
            await Task.Delay(RoomCheckEvery, stop);
        }
    }
}
