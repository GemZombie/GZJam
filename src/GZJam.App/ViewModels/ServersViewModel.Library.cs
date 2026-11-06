using System.Text.RegularExpressions;
using GZJam.Library;
using GZJam.Library.Servers;

namespace GZJam.App.ViewModels;

/// <summary>What the library's song list shows for a server: its songs, or why it can't, and a note.</summary>
public sealed record ServerBrowse(IReadOnlyList<Track> Tracks, string? Problem = null, string? Note = null);

/// <summary>
/// A server in the library itself (#290): its place in the folder tree, its songs in the song
/// list (those not on the computer marked, heard in the headphones, downloaded when they're
/// wanted), and "Also on" under a library search.
/// </summary>
public sealed partial class ServersViewModel
{
    /// <summary>How many recently added albums a server's own page shows, before any search.</summary>
    private const int NewestAlbums = 12;

    private string? _previewPath;

    /// <summary>The servers' places in the library's folder tree, after the music folders, each with its folders from its catalog.</summary>
    public IReadOnlyList<FolderNode> TreeNodes =>
        Items.Select(i => FolderTree.BuildServer(i.Server.Id, i.Name, _database.GetServerCatalogFolders(i.Server.Id),
            _database.GetServerPlaylists(i.Server.Id).Select(p => (p.Id, p.Name, p.SongIds.Count)).ToList())).ToList();

    private ServerItemViewModel? ItemFor(long serverId) => Items.FirstOrDefault(i => i.Server.Id == serverId);

    /// <summary>A search's own filters (<c>bpm:</c>, <c>label:</c>, <c>-played:7</c>): the server doesn't know them.</summary>
    private static readonly Regex Filters = new(@"(^|\s)[-~]?[\p{L}_]+:\S*", RegexOptions.CultureInvariant);

    /// <summary>
    /// The server's songs for the song list: what was added lately (the songs of its newest
    /// albums), or what <paramref name="text"/> finds. Songs on this computer are the library's own
    /// rows; the rest are rows of the server's (<see cref="Track.IsOnServerOnly"/>).
    /// </summary>
    public async Task<ServerBrowse> BrowseAsync(long serverId, string folder, string text, CancellationToken cancel)
    {
        if (ItemFor(serverId) is not { } item)
            return new([], "That server isn't in Settings › Servers any more.");
        if (folder.StartsWith(FolderNode.ServerPlaylistsPath, StringComparison.Ordinal))
            return await BrowsePlaylistAsync(item, folder, text, cancel);

        // From its catalog when it has one: as fast as the library, its folders, its filters, offline too.
        if (_database.GetServerCatalogCount(serverId) > 0)
        {
            CatalogSearch found = await Task.Run(() => _database.SearchServerCatalog(serverId, folder, text, cancel), cancel);
            var rows = await RowsForAsync(item, found.Songs, cancel);
            string? leftOut = found.LeftOut.Count == 0 ? null
                : $"{string.Join(" and ", found.LeftOut)} {(found.LeftOut.Count == 1 ? "is" : "are")} for the songs on this computer: left out here.";
            return new(rows, Note: leftOut ?? (IsReadingCatalog(item) ? $"Reading {item.Name}'s song list again…" : null));
        }
        if (IsReadingCatalog(item))
            return new([], $"Reading {item.Name}'s song list… its songs show here in a moment.");
        if (SignedIn(item) is not { } client)
            return new([], "The password isn't on this computer: Settings › Servers › EDIT to type it.");

        string words = Filters.Replace(text, " ").Trim();
        string? note = words.Length < text.Trim().Length
            ? "Filters such as bpm: and label: are for the songs on this computer: the server's songs are searched by their words."
            : null;
        try
        {
            List<ServerSong> songs = await Task.Run(async () =>
            {
                if (words.Length > 0)
                    return (await client.SearchAsync(words, songs: 300, albums: 0, artists: 0, cancel)).Songs.ToList();
                var albums = await client.GetAlbumListAsync("newest", NewestAlbums, cancel: cancel);
                var lists = await Task.WhenAll(albums.Select(a => client.GetAlbumSongsAsync(a.Id, cancel)));
                return lists.SelectMany(l => l).ToList();
            }, cancel);
            return new(await RowsForAsync(item, songs, cancel), Note: note);
        }
        catch (ServerException e)
        {
            return new([], $"Couldn't ask {item.Name}: {e.Message}");
        }
    }

    /// <summary>
    /// Under a library search: the first server with songs it finds that aren't on this computer,
    /// and what to say ("Also on synology: 12 songs not on this computer"). Null if none, or no
    /// server answers quickly.
    /// </summary>
    public async Task<(long ServerId, string Text)?> AlsoOnAsync(string text, CancellationToken cancel)
    {
        string words = text.Trim();
        if (words.Length < 2 || Filters.IsMatch(words))
            return null;
        foreach (ServerItemViewModel item in Items.Where(i => !i.IsOffline).ToList())
        {
            if (_database.GetServerCatalogCount(item.Server.Id) > 0)
            {
                // At once, from the catalog.
                long serverId = item.Server.Id;
                int notHere = await Task.Run(() =>
                {
                    var catalog = _database.SearchServerCatalog(serverId, "", words, cancel);
                    var local = _database.GetServerSongsHere(serverId);
                    return catalog.Songs.Count(s => !local.ContainsKey(s.Id));
                }, cancel);
                if (notHere > 0)
                    return (item.Server.Id, $"Also on {item.Name}: {(notHere == 1 ? "1 song" : $"{Format.Count(notHere)} songs")} not on this computer");
                continue;
            }
            if (SignedIn(item) is not { } client)
                continue;
            try
            {
                using var quick = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                quick.CancelAfter(TimeSpan.FromSeconds(4));
                var found = await Task.Run(() => client.SearchAsync(words, songs: 100, albums: 0, artists: 0, quick.Token), quick.Token);
                long searched = item.Server.Id;
                var here = await Task.Run(() => _database.GetServerSongsHere(searched), cancel);
                int missing = found.Songs.Count(s => !here.ContainsKey(s.Id));
                if (missing > 0)
                    return (item.Server.Id, $"Also on {item.Name}: {(missing == 1 ? "1 song" : $"{Format.Count(missing)}{(found.Songs.Count >= 100 ? "+" : "")} songs")} not on this computer");
            }
            catch (Exception e) when (e is ServerException or OperationCanceledException)
            {
                // Slow or away: the library's own results are what matter.
            }
        }
        return null;
    }

    /// <summary>
    /// A server's song brought onto this computer now, for a deck, the queue or a playlist: the copy
    /// here if there is one, else downloaded, and kept from then on like any download. Null (with
    /// a notice saying why) if it can't be.
    /// </summary>
    public async Task<Track?> BringHereAsync(Track track)
    {
        if (track.OnServer is not { } remote || ItemFor(remote.ServerId) is not { } item)
            return track.IsOnServerOnly ? null : track;
        if (item.IsPreviewOnly)
        {
            _owner.ShowNotice($"{item.Name} doesn't let this account download songs: they can be heard in the headphones only.", seconds: 8);
            return null;
        }
        if (item.IsOffline)
        {
            _owner.ShowNotice($"{item.Name} can't be reached right now: its songs on this computer play as usual.", seconds: 6);
            return null;
        }
        if (SignedIn(item) is not { } client)
        {
            _owner.ShowNotice($"The password for {item.Name} isn't on this computer: Settings › Servers › EDIT to type it.", seconds: 8);
            return null;
        }
        string title = track.DisplayTitle;
        _owner.ShowNotice($"Downloading {title} from {item.Name}…", seconds: 60);
        try
        {
            await EnsureDownloadsInLibraryAsync();
            AddChoices(item, [new ServerChoice(ServerChoiceKind.Song, remote.Song.Id,
                remote.Song.Artist is { Length: > 0 } artist ? $"{title} ({artist})" : title)]);
            string path = await GetSongAsync(item, client, remote.Song);   // at once: a deck may be waiting
            Track here = await Task.Run(() => _database.TrackForFile(WhereNow(item, remote.Song.Id, path)));
            Log.Info($"Servers: {title} from {item.Name} is here, at {path}");
            _owner.ShowNotice($"{title} is on this computer now.", seconds: 3);
            AfterSync();
            RefreshKept(item);
            RefreshDownloads();
            return here;
        }
        catch (ServerException e) when (e.Outcome == ServerCheckOutcome.Unreachable)
        {
            MarkOffline(item);
            _owner.ShowNotice($"{item.Name} can't be reached right now: its songs on this computer play as usual.", seconds: 6);
            return null;
        }
        catch (Exception e) when (e is ServerException or IOException or UnauthorizedAccessException)
        {
            Log.Info($"Servers: couldn't download {title} from {item.Name}: {e.Message}");
            _owner.ShowNotice($"Couldn't download {title}: {e.Message}", seconds: 8);
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;   // GZJam is closing
        }
    }

    /// <summary>The song list's "Download to this computer": the songs kept from their server, then synced.</summary>
    public Task DownloadAsync(IReadOnlyList<Track> tracks) => DownloadChoicesAsync(tracks, t =>
        new ServerChoice(ServerChoiceKind.Song, t.OnServer!.Song.Id,
            t.Artist is { Length: > 0 } artist ? $"{t.DisplayTitle} ({artist})" : t.DisplayTitle));

    /// <summary>"Download the album": each song's whole album, kept from its server and synced.</summary>
    public Task DownloadAlbumsAsync(IReadOnlyList<Track> tracks) => DownloadChoicesAsync(
        tracks.Where(t => t.OnServer?.Song.AlbumId is not null).DistinctBy(t => (t.OnServer!.ServerId, t.OnServer.Song.AlbumId)).ToList(),
        t => new ServerChoice(ServerChoiceKind.Album, t.OnServer!.Song.AlbumId!, t.Album ?? t.OnServer.Song.AlbumId!));

    /// <summary>
    /// "Download to this computer" on a server's folder: every song in it and below it, kept from
    /// then on (songs added there on the server come with a sync), after asking.
    /// </summary>
    public async Task DownloadFolderAsync(long serverId, string folder, string name)
    {
        if (ItemFor(serverId) is not { } item)
            return;
        if (item.IsPreviewOnly)
        {
            _owner.ShowNotice($"{item.Name} doesn't let this account download songs: they can be heard in the headphones only.", seconds: 8);
            return;
        }
        var (songs, coming) = await Task.Run(() =>
        {
            var all = _database.SearchServerCatalog(serverId, folder, null).Songs;
            var here = _database.GetServerSongsHere(serverId);
            return (all, all.Where(s => !here.ContainsKey(s.Id)).ToList());
        });
        if (songs.Count == 0)
        {
            _owner.ShowNotice($"{name} has no songs in {item.Name}'s song list.", seconds: 6);
            return;
        }
        long bytes = coming.Sum(s => s.Size ?? 0);
        string count = coming.Count == 1 ? "1 song" : $"{Format.Count(coming.Count)} songs";
        if (coming.Count > 0 && (_owner.ConfirmAsync is null || !await _owner.ConfirmAsync(
                $"Download {count}{(bytes > 0 ? $", {Format.Size(bytes)}," : "")} from {name}?",
                $"They come onto this computer now, from {item.Name}, and stay. Songs added to {name} on the server come with each sync.",
                "Download")))
            return;
        AddChoices(item, [new ServerChoice(ServerChoiceKind.Folder, folder, folder.Replace("/", " › "))]);
        _owner.ShowNotice(coming.Count == 0 ? $"{name} is on this computer already: kept up to date from now on." : $"Downloading {count} from {item.Name}…", seconds: 4);
        await SyncAsync(item);
        _owner.Library.RefreshResults();
    }

    private async Task DownloadChoicesAsync(IReadOnlyList<Track> tracks, Func<Track, ServerChoice> choice)
    {
        foreach (var group in tracks.Where(t => t.IsOnServerOnly).GroupBy(t => t.OnServer!.ServerId))
        {
            if (ItemFor(group.Key) is not { } item)
                continue;
            if (item.IsPreviewOnly)
            {
                _owner.ShowNotice($"{item.Name} doesn't let this account download songs: they can be heard in the headphones only.", seconds: 8);
                continue;
            }
            AddChoices(item, group.Select(choice));
            _owner.ShowNotice($"Downloading from {item.Name}…", seconds: 4);
            await SyncAsync(item);
            _owner.Library.RefreshResults();   // the rows here are the library's own now
        }
    }

    /// <summary>A server's song in the headphones (#290), from the song list.</summary>
    public async Task PreviewTrackAsync(Track track)
    {
        if (track.OnServer is not { } remote || ItemFor(remote.ServerId) is not { } item)
            return;
        if (item.IsOffline)
        {
            _owner.ShowNotice($"{item.Name} can't be reached right now, so its songs can't be heard from it.", seconds: 6);
            return;
        }
        _owner.ShowNotice($"Getting {track.DisplayTitle} for the headphones…", seconds: 30);
        string? problem = await PreviewAsync(item, remote.Song, null);
        _owner.ShowNotice(problem ?? $"In the headphones: {track.DisplayTitle}", seconds: problem is null ? 2 : 8);
    }

    /// <summary>Whether <paramref name="track"/>, a server's song, is the one in the headphones now.</summary>
    public bool IsPreviewing(Track track) =>
        track.OnServer is { } remote && _previewPath is { } path && _owner.Preview?.Track?.Path == path
        && Path.GetFileNameWithoutExtension(path) == ServerPaths.Safe($"{remote.ServerId}-{remote.Song.Id}");

    partial void PreviewStarted(string path) => _previewPath = path;

    /// <summary>The library's tree and song list follow the servers being added, renamed or removed.</summary>
    partial void ServersChanged() => _owner.Library?.RefreshServers();

    // ───────────── The catalog ─────────────

    private readonly HashSet<long> _readingCatalog = [];

    /// <summary>How old a catalog may be before GZJam's start reads it again.</summary>
    public TimeSpan CatalogAge { get; set; } = TimeSpan.FromHours(12);

    private bool IsReadingCatalog(ServerItemViewModel item) => _readingCatalog.Contains(item.Server.Id);

    /// <summary>At GZJam's start: each server's catalog, if it's never been read or is older than <see cref="CatalogAge"/>.</summary>
    public async Task ReadCatalogsAsync()
    {
        foreach (ServerItemViewModel item in Items.Where(i => !i.IsOffline).ToList())
            if (_database.GetServerCatalogTime(item.Server.Id) is not { } read || DateTime.UtcNow - read > CatalogAge
                || !_database.ServerCatalogHasArt(item.Server.Id))   // read before its pictures were kept
                await ReadCatalogAsync(item.Server.Id, quiet: true);
        await LinkOwnCopiesAsync();   // songs added to the library since
    }

    /// <summary>
    /// Reads every song on the server into its catalog, so its folders and songs show in the
    /// library (about 30 seconds for 18,000 songs, the first time). <paramref name="quiet"/>: at
    /// startup, a server that doesn't answer is left as it was without a word.
    /// </summary>
    public async Task ReadCatalogAsync(long serverId, bool quiet = false)
    {
        if (ItemFor(serverId) is not { } item || IsReadingCatalog(item) || SignedIn(item) is not { } client)
            return;
        _readingCatalog.Add(serverId);
        _owner.Library.RefreshResults();   // says it's reading, if the server is showing
        try
        {
            SyncStatus ??= $"Reading {item.Name}'s song list…";
            var progress = new Progress<int>(n =>
            {
                if (SyncStatus is null || SyncStatus.StartsWith("Reading", StringComparison.Ordinal))
                    SyncStatus = $"Reading {item.Name}'s song list… {Format.Count(n)}";
            });
            var started = System.Diagnostics.Stopwatch.StartNew();
            List<ServerSong> songs = await Task.Run(() => ServerCatalogReader.ReadAsync(client, progress));
            if (songs.Count == 0 && _database.GetServerCatalogCount(serverId) > 0)
            {
                // No songs at all, from a server that had some: it's reading its music again, say,
                // or a folder it reads is away. What it had stays, rather than the server emptying.
                Log.Info($"Servers: {item.Name} listed no songs at all: its song list is kept as it was");
                if (!quiet)
                    _owner.ShowNotice($"{item.Name} listed no songs just now (is it still reading its music?): its song list is kept as it was.", seconds: 8);
                return;
            }
            await Task.Run(() => _database.ReplaceServerCatalog(serverId, songs, DateTime.UtcNow));
            Log.Info($"Servers: read {item.Name}'s song list, {Format.Count(songs.Count)} songs, in {started.Elapsed.TotalSeconds:0.0} s");
            await ReadPlaylistsAsync(item, client);
            _ = LinkOwnCopiesAsync();   // which of them are in the library already
            StartKeepWhole(item);   // songs added there, if it's kept whole
        }
        catch (ServerException e)
        {
            if (e.Outcome == ServerCheckOutcome.Unreachable)
                MarkOffline(item);
            Log.Info($"Servers: couldn't read {item.Name}'s song list: {e.Message}");
            if (!quiet)
                _owner.ShowNotice($"Couldn't read {item.Name}'s song list: {e.Message}", seconds: 8);
        }
        finally
        {
            _readingCatalog.Remove(serverId);
            if (SyncStatus?.StartsWith("Reading", StringComparison.Ordinal) == true)
                SyncStatus = null;
            _owner.Library.RefreshServers();
            _owner.Library.RefreshResults();
        }
    }
}
