using System.Text.RegularExpressions;
using GZJam.Library;
using GZJam.Library.Servers;

namespace GZJam.App.ViewModels;

/// <summary>What the library's song list shows for a server: its songs, or why it can't, and a note.</summary>
public sealed record ServerBrowse(IReadOnlyList<Track> Tracks, string? Problem = null, string? Note = null);

/// <summary>
/// A server in the library itself (#290): its place in the folder tree, its songs in the song
/// list (those not on the laptop marked, heard in the headphones, downloaded when they're
/// wanted), and "Also on" under a library search.
/// </summary>
public sealed partial class ServersViewModel
{
    /// <summary>How many recently added albums a server's own page shows, before any search.</summary>
    private const int NewestAlbums = 12;

    private string? _previewPath;

    /// <summary>The servers' places in the library's folder tree, after the music folders.</summary>
    public IReadOnlyList<(long Id, string Name)> TreeNodes => Items.Select(i => (i.Server.Id, i.Name)).ToList();

    private ServerItemViewModel? ItemFor(long serverId) => Items.FirstOrDefault(i => i.Server.Id == serverId);

    /// <summary>A search's own filters (<c>bpm:</c>, <c>label:</c>, <c>-played:7</c>): the server doesn't know them.</summary>
    private static readonly Regex Filters = new(@"(^|\s)[-~]?[\p{L}_]+:\S*", RegexOptions.CultureInvariant);

    /// <summary>
    /// The server's songs for the song list: what was added lately (the songs of its newest
    /// albums), or what <paramref name="text"/> finds. Songs on this laptop are the library's own
    /// rows; the rest are rows of the server's (<see cref="Track.IsOnServerOnly"/>).
    /// </summary>
    public async Task<ServerBrowse> BrowseAsync(long serverId, string text, CancellationToken cancel)
    {
        if (ItemFor(serverId) is not { } item)
            return new([], "That server isn't in Settings › Servers any more.");
        if (SignedIn(item) is not { } client)
            return new([], "The password isn't on this computer: Settings › Servers › EDIT to type it.");

        string words = Filters.Replace(text, " ").Trim();
        string? note = words.Length < text.Trim().Length
            ? "Filters such as bpm: and label: are for the songs on this laptop: the server's songs are searched by their words."
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
            var here = LocalCopies(item);
            var tracks = await Task.Run(() => songs.Select(s => here.TryGetValue(s.Id, out string? path)
                ? _database.TrackForFile(path)
                : new ServerTrack(item.Server.Id, item.Name, s).ToTrack()).ToList(), cancel);
            return new(tracks, Note: note);
        }
        catch (ServerException e)
        {
            return new([], $"Couldn't ask {item.Name}: {e.Message}");
        }
    }

    /// <summary>
    /// Under a library search: the first server with songs it finds that aren't on this laptop,
    /// and what to say ("Also on synology: 12 songs not on this laptop"). Null if none, or no
    /// server answers quickly.
    /// </summary>
    public async Task<(long ServerId, string Text)?> AlsoOnAsync(string text, CancellationToken cancel)
    {
        string words = text.Trim();
        if (words.Length < 2 || Filters.IsMatch(words))
            return null;
        foreach (ServerItemViewModel item in Items.ToList())
        {
            if (SignedIn(item) is not { } client)
                continue;
            try
            {
                using var quick = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                quick.CancelAfter(TimeSpan.FromSeconds(4));
                var found = await Task.Run(() => client.SearchAsync(words, songs: 100, albums: 0, artists: 0, quick.Token), quick.Token);
                var here = LocalCopies(item);
                int missing = found.Songs.Count(s => !here.ContainsKey(s.Id));
                if (missing > 0)
                    return (item.Server.Id, $"Also on {item.Name}: {(missing == 1 ? "1 song" : $"{Format.Count(missing)}{(found.Songs.Count >= 100 ? "+" : "")} songs")} not on this laptop");
            }
            catch (Exception e) when (e is ServerException or OperationCanceledException)
            {
                // Slow or away: the library's own results are what matter.
            }
        }
        return null;
    }

    /// <summary>
    /// A server's song brought onto this laptop now, for a deck, the queue or a playlist: the copy
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
            var sync = new ServerSync(_database, client, item.Server, DownloadsFolder);
            string path = await Task.Run(() => sync.GetOneAsync(remote.Song));
            Track here = await Task.Run(() => _database.TrackForFile(path));
            Log.Info($"Servers: {title} from {item.Name} is here, at {path}");
            _owner.ShowNotice($"{title} is on this laptop now.", seconds: 3);
            AfterSync();
            RefreshKept(item);
            RefreshDownloads();
            return here;
        }
        catch (Exception e) when (e is ServerException or IOException or UnauthorizedAccessException)
        {
            Log.Info($"Servers: couldn't download {title} from {item.Name}: {e.Message}");
            _owner.ShowNotice($"Couldn't download {title}: {e.Message}", seconds: 8);
            return null;
        }
    }

    /// <summary>The song list's "Download to this laptop": the songs kept from their server, then synced.</summary>
    public Task DownloadAsync(IReadOnlyList<Track> tracks) => DownloadChoicesAsync(tracks, t =>
        new ServerChoice(ServerChoiceKind.Song, t.OnServer!.Song.Id,
            t.Artist is { Length: > 0 } artist ? $"{t.DisplayTitle} ({artist})" : t.DisplayTitle));

    /// <summary>"Download the album": each song's whole album, kept from its server and synced.</summary>
    public Task DownloadAlbumsAsync(IReadOnlyList<Track> tracks) => DownloadChoicesAsync(
        tracks.Where(t => t.OnServer?.Song.AlbumId is not null).DistinctBy(t => (t.OnServer!.ServerId, t.OnServer.Song.AlbumId)).ToList(),
        t => new ServerChoice(ServerChoiceKind.Album, t.OnServer!.Song.AlbumId!, t.Album ?? t.OnServer.Song.AlbumId!));

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
}
