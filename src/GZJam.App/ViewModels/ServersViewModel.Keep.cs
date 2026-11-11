using Avalonia.Threading;
using GZJam.Library;
using GZJam.Library.Servers;

namespace GZJam.App.ViewModels;

/// <summary>
/// Keep a copy in my library (#285): a server's songs brought into the DJ's own music, a bit at a
/// time: a song or a selection, a playlist or a folder of the server's. They go into a music
/// folder the DJ chooses, laid out as the server has it, never over a file that's there. A song
/// downloaded already moves there, with its hearts, cues, labels, playlists and plays; one not
/// here comes from the server. A song the DJ has already (the same title and artist, and the same
/// size or length) is left as it is. A kept song is an ordinary song of the DJ's from then on:
/// never removed by GZJam, and tag edits can be written into it.
/// </summary>
public sealed partial class ServersViewModel
{
    /// <summary>How many of the paths the question shows before "…and 40 more".</summary>
    private const int PathsShown = 4;

    private bool _keeping;
    private bool _linking, _linkAgain;

    /// <summary>Asks the DJ for a folder, with a title saying what it's for; null if they chose none. The window sets it; tests too.</summary>
    public Func<string, Task<string?>>? ChooseFolderAsync { get; set; }

    /// <summary>Where kept songs go, or null until the DJ chooses (the first time they keep one).</summary>
    public string? KeepFolder => _settings.ServerKeepFolder is { Length: > 0 } folder ? folder : null;

    public string KeepFolderText => KeepFolder ?? "Not chosen yet: you're asked the first time you add a song.";

    /// <summary>While songs are being kept: the top bar says so.</summary>
    public bool IsKeeping => _keeping;

    /// <summary>Kept songs go to <paramref name="folder"/> from now on (those kept already stay where they are). False, with a notice, if it can't be that one.</summary>
    public bool SetKeepFolder(string folder)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (IsUnder(full, DownloadsFolder))
        {
            _owner.ShowNotice($"Choose a folder outside {DownloadsFolder}: songs there are removed when space is needed, and songs you add never are.", seconds: 8);
            return false;
        }
        _settings.ServerKeepFolder = full;
        Log.Info($"Servers: songs kept from servers go to {full}");
        OnPropertyChanged(nameof(KeepFolder));
        OnPropertyChanged(nameof(KeepFolderText));
        return true;
    }

    // ───────────── What can be kept ─────────────

    /// <summary>Whether any of these songs can be kept: a server's song not here, or one downloaded from it.</summary>
    public bool CanKeepCopy(IReadOnlyList<Track> tracks)
    {
        if (tracks.Any(t => t.OnServer is { } remote && ItemFor(remote.ServerId) is { IsPreviewOnly: false }))
            return true;
        if (tracks.All(t => t.IsOnServerOnly))
            return false;
        var downloads = _database.GetServerDownloadsByPath();   // one read, however many are chosen
        return downloads.Count > 0 && tracks.Any(t => downloads.ContainsKey(t.Path));
    }

    /// <summary>Keep a copy in my library, from the song list: the server's songs among them, and those downloaded from it.</summary>
    /// <param name="place">Where they go, if the DJ has chosen already (#335): nothing more is asked.</param>
    public async Task KeepTracksAsync(IReadOnlyList<Track> tracks, AddPlace? place = null)
    {
        var wanted = new List<(long ServerId, string SongId, ServerSong? Song)>();
        var downloads = await Task.Run(_database.GetServerDownloadsByPath);
        foreach (Track track in tracks)
            if (track.OnServer is { } remote)
                wanted.Add((remote.ServerId, remote.Song.Id, remote.Song));
            else if (downloads.TryGetValue(track.Path, out var download))
                wanted.Add((download.ServerId, download.SongId, null));
        if (wanted.Count == 0)
        {
            _owner.ShowNotice("Those are in My music already: only a server's songs can be added to it.", seconds: 6);
            return;
        }
        foreach (var server in wanted.GroupBy(w => w.ServerId))
        {
            if (ItemFor(server.Key) is not { } item)
                continue;
            var catalog = _database.GetServerCatalogSongs(server.Key, server.Where(w => w.Song is null).Select(w => w.SongId))
                .ToDictionary(s => s.Id);
            var songs = server.Select(w => (w.SongId, w.Song ?? catalog.GetValueOrDefault(w.SongId))).ToList();
            await KeepAsync(item, songs, songs.Count == 1 ? tracks.FirstOrDefault(t => t.OnServer?.Song.Id == songs[0].SongId)?.DisplayTitle ?? "That song" : "Those songs",
                ask: place is null, again: place);
        }
    }

    /// <summary>Keep a copy in my library, on a server's folder: every song in it and in the folders in it.</summary>
    public async Task KeepFolderCopyAsync(long serverId, string folder, string name)
    {
        if (ItemFor(serverId) is not { } item)
            return;
        var songs = await Task.Run(() => _database.SearchServerCatalog(serverId, folder, null).Songs);
        if (songs.Count == 0)
        {
            _owner.ShowNotice($"{name} has no songs in {item.Name}'s song list.", seconds: 6);
            return;
        }
        await KeepAsync(item, songs.Select(s => (s.Id, (ServerSong?)s)).ToList(), name);
    }

    /// <summary>Keep a copy in my library, on a server's playlist: its songs (the playlist itself stays the server's).</summary>
    public async Task KeepPlaylistCopyAsync(long serverId, string playlistId)
    {
        if (ItemFor(serverId) is not { } item || _database.GetServerPlaylist(serverId, playlistId) is not { } playlist)
            return;
        var catalog = _database.GetServerCatalogSongs(serverId, playlist.SongIds).ToDictionary(s => s.Id);
        await KeepAsync(item, playlist.SongIds.Distinct().Select(id => (id, catalog.GetValueOrDefault(id))).ToList(), playlist.Name);
    }

    // ───────────── Keeping ─────────────

    /// <summary>One song to keep: from its download (<see cref="From"/>), or from the server.</summary>
    private sealed record KeepStep(string SongId, ServerSong? Song, string? From, string Target, bool Art = true)
    {
        public string Title => Song?.Title ?? Path.GetFileNameWithoutExtension(Target);
    }

    private sealed record KeepPlan(List<KeepStep> Moves, List<KeepStep> Downloads, int Mine, List<string> OnDeck);

    /// <summary>Asks (showing where they'll go), then keeps the songs in the DJ's music folder for kept songs.</summary>
    private async Task KeepAsync(ServerItemViewModel item, IReadOnlyList<(string SongId, ServerSong? Song)> songs, string what, bool ask = true, AddPlace? again = null)
    {
        if (item.IsSyncing || _keeping)
        {
            _owner.ShowNotice($"{item.Name} is busy (a sync, or songs being kept): try again once it's done.", seconds: 6);
            return;
        }
        if (_moving)
        {
            _owner.ShowNotice("The downloaded songs are moving to their new folder: try again once they're there.", seconds: 6);
            return;
        }

        // A place chosen already needs no folder asked for.
        if ((again is { } given ? (given.Folder, false) : await KeepFolderToUseAsync()) is not var (folder, choosing))
            return;

        var onDecks = _owner.Decks.Select(d => d.Track?.Path).OfType<string>().ToHashSet(StringComparer.Ordinal);
        AddPlace place = again ?? new AddPlace(folder, KeepFolders: true, IsUsual: true);
        string keepIn = folder;
        KeepPlan plan = await Task.Run(() => PlanKeep(item, songs, place, onDecks));
        bool canDownload = !item.IsPreviewOnly && !item.IsOffline && SignedIn(item) is not null;
        string? cantDownload = plan.Downloads.Count == 0 || canDownload ? null
            : item.IsPreviewOnly ? $"{item.Name} doesn't let this account download songs"
            : item.IsOffline ? $"{item.Name} can't be reached right now"
            : $"The password for {item.Name} isn't on this computer";
        var downloads = cantDownload is null ? plan.Downloads : [];
        int count = plan.Moves.Count + downloads.Count;
        if (count == 0)
        {
            _owner.ShowNotice(
                cantDownload is not null ? $"{cantDownload}: the songs not on this computer can't be added now."
                : plan.OnDeck.Count > 0 ? $"Take {plan.OnDeck[0]} off its deck first: its file can't move while it's on a deck."
                : plan.Mine > 0 ? $"{what} {(songs.Count == 1 ? "is" : "are")} in My music already."
                : $"{what}: nothing to add.", seconds: 6);
            return;
        }

        if (ask)
        {
            if (await AskAddPlaceAsync($"Add {SongCount(count)} to My music?", KeepWhat(item, plan, downloads, cantDownload), keepIn, canKeepFolders: true,
                    KeepQuestion(item, plan, downloads, keepIn, cantDownload)) is not { } chosen)
                return;
            if (chosen != place)
            {
                place = chosen;
                plan = await Task.Run(() => PlanKeep(item, songs, place, onDecks));
                downloads = cantDownload is null ? plan.Downloads : [];
            }
        }
        if (choosing && !SetKeepFolder(keepIn))
            return;
        await RunKeepAsync(item, plan.Moves.Concat(downloads).ToList(), place);
    }

    /// <summary>
    /// Asks which folder of My music songs go to (#324), opening on the last one chosen or the
    /// usual place, and remembers the answer. Null if the DJ said no.
    /// </summary>
    /// <param name="what">What will happen to the songs, wherever they go.</param>
    /// <param name="usual">Where they go unless the DJ says otherwise.</param>
    /// <param name="yesOrNo">The whole question, where there's no dialog to choose in.</param>
    /// <param name="canMove">Songs from Files: they can be moved rather than copied (#384), as chosen last time.</param>
    /// <param name="cantMoveWhy">Why they can't be after all (on a USB stick), the choice grayed.</param>
    internal async Task<AddPlace?> AskAddPlaceAsync(string heading, string what, string usual, bool canKeepFolders, string yesOrNo,
        bool canMove = false, string? cantMoveWhy = null)
    {
        if (_owner.ChooseAddPlaceAsync is not { } choose)
            return _owner.ConfirmAsync is { } confirm && await confirm(heading, yesOrNo, "Add") ? new AddPlace(usual, KeepFolders: true, IsUsual: true) : null;
        MoveChoice? move = canMove ? new MoveChoice(_settings.AddToMyMusicMove, cantMoveWhy) : null;
        if (await choose(new AddPlaceViewModel(heading, what, usual, _owner.Library.MusicFolders,
                _settings.AddToMyMusicLastFolder, _settings.AddToMyMusicKeepFolders, canKeepFolders, DownloadsFolder, move: move)) is not { } place)
            return null;
        _settings.AddToMyMusicLastFolder = place.IsUsual ? null : place.Folder;
        if (!place.IsUsual && canKeepFolders)
            _settings.AddToMyMusicKeepFolders = place.KeepFolders;
        if (move is { CantWhy: null })
            _settings.AddToMyMusicMove = place.Move;
        return place;
    }

    /// <summary>
    /// The folder kept songs go to: the one chosen, or asked for now (the first time), with
    /// whether it was asked for (it's saved once the DJ says yes). Null, with a notice, if there's
    /// none to use.
    /// </summary>
    internal async Task<(string Folder, bool Choosing)?> KeepFolderToUseAsync()
    {
        if (KeepFolder is { } folder)
        {
            if (Path.GetPathRoot(folder) is { } drive && !Directory.Exists(drive))
            {
                _owner.ShowNotice($"{folder} isn't there (a drive unplugged?): plug it in, or choose another folder in Settings › Servers.", seconds: 8);
                return null;
            }
            return (folder, false);
        }
        string? chosen = ChooseFolderAsync is { } choose ? await choose("Where songs you add to My music go (one of your music folders)") : null;
        if (chosen is null)
            return null;
        chosen = Path.TrimEndingDirectorySeparator(Path.GetFullPath(chosen));
        if (IsUnder(chosen, DownloadsFolder))
        {
            _owner.ShowNotice($"Choose a folder outside {DownloadsFolder}: songs there are removed when space is needed, and songs you add never are.", seconds: 8);
            return null;
        }
        return (chosen, true);
    }

    /// <summary>What's to happen, with the paths first (#285: "with the paths shown first").</summary>
    private static string KeepQuestion(ServerItemViewModel item, KeepPlan plan, List<KeepStep> downloads, string folder, string? cantDownload)
    {
        var all = plan.Moves.Concat(downloads).ToList();
        var lines = new List<string> { $"Into {folder}, laid out as on {item.Name}:" };
        lines.AddRange(all.Take(PathsShown).Select(s => "    " + Path.GetRelativePath(folder, s.Target)));
        if (all.Count > PathsShown)
            lines.Add($"    …and {Format.Count(all.Count - PathsShown)} more");
        lines.Add("");
        lines.Add(KeepWhat(item, plan, downloads, cantDownload));
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>What's to happen to the songs, wherever they go.</summary>
    private static string KeepWhat(ServerItemViewModel item, KeepPlan plan, List<KeepStep> downloads, string? cantDownload)
    {
        var lines = new List<string>();
        if (plan.Moves.Count > 0)
            lines.Add($"{SongCount(plan.Moves.Count)} downloaded already {(plan.Moves.Count == 1 ? "moves" : "move")} there, with {(plan.Moves.Count == 1 ? "its" : "their")} hearts, cues, labels, playlists and plays.");
        if (downloads.Count > 0)
        {
            long bytes = downloads.Sum(d => d.Song?.Size ?? 0);
            lines.Add($"{SongCount(downloads.Count)} {(downloads.Count == 1 ? "comes" : "come")} from {item.Name} now{(bytes > 0 ? $", {Format.Size(bytes)}" : "")}.");
        }
        if (cantDownload is not null)
            lines.Add($"{cantDownload}, so the {SongCount(plan.Downloads.Count)} not on this computer {(plan.Downloads.Count == 1 ? "waits" : "wait")} for another time.");
        if (plan.Mine > 0)
            lines.Add($"{SongCount(plan.Mine)} {(plan.Mine == 1 ? "is" : "are")} in My music already: left as {(plan.Mine == 1 ? "it is" : "they are")}.");
        if (plan.OnDeck.Count > 0)
            lines.Add($"{string.Join(" and ", plan.OnDeck)} {(plan.OnDeck.Count == 1 ? "is" : "are")} on a deck: add {(plan.OnDeck.Count == 1 ? "it" : "them")} once {(plan.OnDeck.Count == 1 ? "it's" : "they're")} off.");
        lines.Add("They're your own songs from then on: GZJam never removes them, and nothing in the folder is replaced.");
        return string.Join(Environment.NewLine, lines);
    }

    private static string SongCount(int n) => n == 1 ? "1 song" : $"{Format.Count(n)} songs";

    /// <summary>
    /// Where each song goes, and which are the DJ's already: those linked to their own copy, or
    /// found in the library now. A downloaded song keeps its place under its server's folder.
    /// </summary>
    private KeepPlan PlanKeep(ServerItemViewModel item, IReadOnlyList<(string SongId, ServerSong? Song)> songs, AddPlace place, HashSet<string> onDecks)
    {
        string folder = place.Folder;
        IReadOnlyList<string> Laid(IReadOnlyList<string> parts) => place.KeepFolders ? parts : [parts[^1]];   // straight into the folder chosen
        long serverId = item.Server.Id;
        bool catalogued = _database.GetServerCatalogCount(serverId) > 0;
        if (catalogued)
            _database.LinkOwnCopies(serverId, DownloadsFolder, DateTime.UtcNow);   // what's the DJ's already, all at once
        var copies = _database.GetServerSongs(serverId).Where(c => File.Exists(c.Path)).ToDictionary(c => c.SongId);
        string serverFolder = ServerPaths.ServerFolder(DownloadsFolder, item.Name);
        var planned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool Taken(string path) => planned.Contains(path) || File.Exists(path) || _database.IsServerSongPath(path);
        var moves = new List<KeepStep>();
        var downloads = new List<KeepStep>();
        var onDeck = new List<string>();
        int mine = 0;
        foreach (var (songId, song) in songs.DistinctBy(s => s.SongId))
        {
            if (copies.TryGetValue(songId, out ServerSongCopy? copy))
            {
                if (copy.OwnCopy)
                {
                    mine++;
                    continue;
                }
                if (onDecks.Contains(copy.Path))
                {
                    onDeck.Add(song?.Title ?? Path.GetFileNameWithoutExtension(copy.Path));
                    continue;
                }
                IReadOnlyList<string> parts = IsUnder(copy.Path, serverFolder)
                    ? Path.GetRelativePath(serverFolder, copy.Path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    : song is not null ? ServerPaths.RelativeParts(song) : [Path.GetFileName(copy.Path)];
                string target = ServerPaths.Place(folder, Laid(parts), Taken);
                planned.Add(target);
                moves.Add(new KeepStep(songId, song, copy.Path, target, place.KeepFolders));
                continue;
            }
            if (song is null)
                continue;   // neither here nor in the server's song list any more
            if (!catalogued && _database.FindOwnCopy(song, DownloadsFolder) is { } own)
            {
                _database.SaveServerSong(new ServerSongCopy(serverId, song.Id, own, song.Size, song.Path, OwnCopy: true), DateTime.UtcNow);
                mine++;
                continue;
            }
            string spot = ServerPaths.Place(folder, Laid(ServerPaths.RelativeParts(song)), Taken);
            planned.Add(spot);
            downloads.Add(new KeepStep(songId, song, null, spot, place.KeepFolders));
        }
        return new KeepPlan(moves, downloads, mine, onDeck);
    }

    /// <summary>
    /// Keeps each song, one after another: a downloaded one moved (with all that's kept by its
    /// path), one not here downloaded straight into its place. Stopping (Stop syncing on the
    /// server) leaves each song kept so far kept.
    /// </summary>
    private async Task RunKeepAsync(ServerItemViewModel item, List<KeepStep> steps, AddPlace place)
    {
        string folder = place.Folder;
        var stop = new CancellationTokenSource();
        item.BeginSync(stop);
        _keeping = true;
        item.ShowSync("Waiting for another server's sync…", problem: false);
        var batch = Downloads.StartBatch(("keep", item.Server.Id), $"Adding songs from {item.Name} to My music", "Stop", () => StopSync(item));
        batch.Detail = "Waiting for another server's sync…";
        await _oneSync.WaitAsync();
        var moved = new List<(string From, string To)>();
        int kept = 0, failed = 0;
        long bytes = 0;
        string? stopped = null;
        try
        {
            Directory.CreateDirectory(folder);
            if (RootOf(folder) is null)
            {
                Log.Info($"Servers: adding {folder}, where kept songs go, to the library");
                await _owner.Library.AddRootAsync(folder);
            }
            if (_owner.Preview?.Track is { } heard && steps.Any(s => s.From == heard.Path))
                _owner.Preview.Stop();
            SubsonicClient? client = steps.Any(s => s.From is null) ? SignedIn(item) : null;
            var sync = client is null ? null : new ServerSync(_database, client, item.Server, DownloadsFolder)
            {
                Report = ReportTo(item, "Add to my music", ("keep", item.Server.Id), rows => KeepAgainAsync(item, rows, place)),
            };
            Log.Info($"Servers: keeping {steps.Count} songs from {item.Name} in {folder}");

            for (int i = 0; i < steps.Count; i++)
            {
                if (stop.IsCancellationRequested)
                {
                    stopped = "Stopped.";
                    break;
                }
                KeepStep step = steps[i];
                SyncStatus = $"Adding songs to My music… {Format.Count(i)} / {Format.Count(steps.Count)}";
                SyncPercent = 100.0 * i / steps.Count;
                item.ShowSync($"Adding to My music… {Format.Count(i)} of {Format.Count(steps.Count)} songs", problem: false);
                batch.Detail = $"{Format.Count(i)} of {Format.Count(steps.Count)} songs";
                batch.Percent = SyncPercent;
                try
                {
                    var (move, got) = await KeepOneAsync(item, step, sync, folder, stop.Token);
                    if (move is { } made)
                        moved.Add(made);
                    bytes += got;
                    kept++;
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                    stopped = "Stopped.";
                    break;
                }
                catch (OperationCanceledException)
                {
                    failed++;   // just this song stopped (the Downloads window)
                }
                catch (ServerException e) when (e.Outcome == ServerCheckOutcome.Unreachable)
                {
                    MarkOffline(item);
                    failed += steps.Count - i;
                    stopped = $"{item.Name} stopped answering.";
                    break;
                }
                catch (Exception e) when (e is ServerException or IOException or UnauthorizedAccessException)
                {
                    failed++;
                    Log.Info($"Servers: couldn't keep {step.Title} from {item.Name}: {e.Message}");
                }
            }
        }
        finally
        {
            SyncStatus = null;
            Downloads.EndBatch(("keep", item.Server.Id));
            _keeping = false;
            item.EndSync();
            _oneSync.Release();
        }

        Log.Info($"Servers: kept {kept} songs from {item.Name} in {folder} ({moved.Count} moved, {Format.Size(bytes)} downloaded)"
            + (failed > 0 ? $"; {failed} couldn't be" : "") + (stopped is { } why ? $"; {why}" : ""));
        if (moved.Count > 0)
            _owner.Library.FollowMoves(moved);
        LibraryRoot? keepRoot = RootOf(folder), downloadsRoot = RootOf(DownloadsFolder);
        if (keepRoot is not null)
            await _owner.Library.ScanRootAsync(keepRoot.Id);
        if (moved.Count > 0 && downloadsRoot is not null && downloadsRoot.Id != keepRoot?.Id)
            _owner.Library.RescanRoot(downloadsRoot.Id);
        _owner.Library.RefreshPlaylists();
        RefreshKept(item);
        RefreshDownloads();
        _owner.Library.RefreshResults();

        string done = $"Added {SongCount(kept)} to My music, in {folder}.";
        string text = stopped is { } reason ? $"{reason} {done}"
            : failed > 0 ? $"{done} {SongCount(failed)} couldn't be: the activity log says why."
            : done;
        item.ShowSync(text, problem: stopped is not null || failed > 0);
        _owner.ShowNotice(text, seconds: 8);
    }

    /// <summary>The Downloads window's Retry failed, for songs being kept: kept again, without asking again.</summary>
    private Task KeepAgainAsync(ServerItemViewModel item, IReadOnlyList<DownloadItem> rows, AddPlace place) =>
        KeepAsync(item, rows.Select(r => (r.Song.Id, (ServerSong?)r.Song)).ToList(), rows.Count == 1 ? rows[0].Title : "Those songs", ask: false, again: place);

    /// <summary>
    /// One song kept: its download moved there (one coming down already, for the queue say,
    /// finishes first), or it downloads straight into its place. Returns the move, if it was one,
    /// and the bytes downloaded.
    /// </summary>
    private async Task<((string From, string To)? Move, long Bytes)> KeepOneAsync(
        ServerItemViewModel item, KeepStep step, ServerSync? sync, string folder, CancellationToken stop)
    {
        string? from = step.From;
        if (from is null && _downloads.TryGetValue((item.Server.Id, step.SongId), out Download? coming))
        {
            try
            {
                from = await coming.Done;
            }
            catch (Exception) when (!stop.IsCancellationRequested)
            {
                from = null;
            }
        }
        if (from is not null)
        {
            var move = step with { From = from };
            await _owner.Library.WithoutScansAsync(() => Task.Run(() => KeepMove(item, move)));   // no scan halfway
            return ((from, step.Target), 0);
        }
        if (sync is null || step.Song is null)
            throw new ServerException("It isn't on this computer, and can't be downloaded now.");
        return (null, await Task.Run(() => sync.KeepAsync(step.Song, step.Target, folder, stop, step.Art), stop));
    }

    /// <summary>A downloaded song into the DJ's music: its file moved, and everything kept by its path with it.</summary>
    private bool KeepMove(ServerItemViewModel item, KeepStep step)
    {
        string from = step.From!, to = step.Target;
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        DateTime modified = File.GetLastWriteTimeUtc(from);
        BusyFile.Move(from, to);   // never over a file: one there stops it
        File.SetLastWriteTimeUtc(to, modified);   // a copy to another drive is a new file
        _database.MoveSongFiles([(from, to)]);
        _database.SetServerSongKept(item.Server.Id, step.SongId, to);
        if (step.Art)   // a folder the songs were dropped into isn't their album
            CopyArtAlong(Path.GetDirectoryName(from)!, Path.GetDirectoryName(to)!);
        RemoveEmptyFolders(Path.GetDirectoryName(from));
        return true;
    }

    /// <summary>
    /// The album's picture goes with its songs: moved with the last one out of the folder, or
    /// copied if songs are left there, unless the new folder has one.
    /// </summary>
    private static void CopyArtAlong(string folder, string to)
    {
        if (!Directory.Exists(folder))
            return;
        if (OnlyArtLeft(folder) is { Count: > 0 })
        {
            MoveArtAlong(folder, to);
            return;
        }
        if (AlbumArtFinder.FolderImage(to) is not null || AlbumArtFinder.FolderImage(folder) is not { } picture
            || !ArtNames.Contains(Path.GetFileNameWithoutExtension(picture), StringComparer.OrdinalIgnoreCase))
            return;
        try
        {
            File.Copy(picture, Path.Combine(to, Path.GetFileName(picture)), overwrite: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Info($"Servers: couldn't copy the album picture {picture}: {e.Message}");
        }
    }

    // ───────────── Already have it ─────────────

    /// <summary>
    /// "Already have it" (#285): the server's songs that are in the DJ's library already are linked
    /// to their own copies, so browsing shows them as theirs and nothing downloads them. After the
    /// server's song list is read, and after a scan finds songs; in the background, one pass at a time.
    /// </summary>
    internal async Task LinkOwnCopiesAsync()
    {
        if (_linking)
        {
            _linkAgain = true;
            return;
        }
        _linking = true;
        try
        {
            do
            {
                _linkAgain = false;
                var servers = Items.Select(i => (i.Server.Id, i.Name)).ToList();
                string downloads = DownloadsFolder;
                int linked = await Task.Run(() => servers.Sum(s =>
                {
                    if (_database.GetServerCatalogCount(s.Id) == 0)
                        return 0;
                    int n = _database.LinkOwnCopies(s.Id, downloads, DateTime.UtcNow);
                    if (n > 0)
                        Log.Info($"Servers: {Format.Count(n)} of {s.Name}'s songs are in your library already");
                    return n;
                }));
                if (linked > 0 && _owner.Library.SelectedFolder is { IsServer: true })
                    _owner.Library.RefreshResults();
            }
            while (_linkAgain);
        }
        catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            Log.Info($"Servers: couldn't look for the servers' songs in your library: {e.Message}");
        }
        finally
        {
            _linking = false;
        }
    }

    /// <summary>A song's file as it is now: where its server song is (kept, a moment ago, say), else <paramref name="path"/>.</summary>
    private string WhereNow(ServerItemViewModel item, string songId, string path) =>
        File.Exists(path) ? path
        : _database.GetServerSongs(item.Server.Id).FirstOrDefault(s => s.SongId == songId && File.Exists(s.Path))?.Path ?? path;
}
