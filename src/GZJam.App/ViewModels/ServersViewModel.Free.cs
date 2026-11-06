using GZJam.Library;
using GZJam.Library.Servers;

namespace GZJam.App.ViewModels;

/// <summary>What Free up space… offers, each with its songs: those GZJam downloaded and may remove now.</summary>
public sealed record FreeUpOffer(
    IReadOnlyList<ServerDownload> LastNight,
    IReadOnlyList<ServerDownload> NotPlayedLately,
    IReadOnlyList<ServerDownload> Everything,
    int Protected,
    long? DiskFree);

/// <summary>
/// Freeing up space (#290), for a laptop too small for the library: songs downloaded from a
/// server are removed from the computer (they stay on the server, and come back when asked for).
/// Only GZJam's downloads, never the DJ's own copies or songs kept in their music folders; never
/// the song on a deck, in the queue, or played in tonight's session.
/// </summary>
public sealed partial class ServersViewModel
{
    /// <summary>"Not played in the last 4 weeks".</summary>
    public static readonly TimeSpan NotPlayedFor = TimeSpan.FromDays(28);

    /// <summary>Paths that are never removed: on a deck, in the queue, or heard tonight.</summary>
    private HashSet<string> ProtectedPaths()
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (QueueItem item in _owner.Queue.Items)
            paths.Add(item.Track.Path);
        foreach (DeckViewModel deck in _owner.Decks)
            if (deck.Track is { } track)
                paths.Add(track.Path);
        if (_owner.History.CurrentSessionStartUtc is { } tonight)
            paths.UnionWith(_database.GetPathsPlayedSince(tonight));
        return paths;
    }

    /// <summary>What can be removed from the server's downloads now, sorted into Free up space…'s choices.</summary>
    public FreeUpOffer OfferToFreeUp(long serverId)
    {
        var all = _database.GetServerDownloads(serverId);
        var keep = ProtectedPaths();
        var removable = all.Where(d => !keep.Contains(d.Path) && File.Exists(d.Path)).ToList();
        DateTime? lastNight = _database.GetSessions().FirstOrDefault()?.StartedUtc;
        DateTime cutoff = DateTime.UtcNow - NotPlayedFor;
        return new FreeUpOffer(
            lastNight is { } since ? removable.Where(d => d.OnTheSpot && d.DownloadedUtc >= since).ToList() : [],
            removable.Where(d => (d.LastPlayedUtc ?? DateTime.MinValue) < cutoff && d.DownloadedUtc < cutoff).ToList(),
            removable,
            all.Count - removable.Count,
            FreeSpace());
    }

    /// <summary>Of these library songs, the ones GZJam downloaded from a server and may remove now.</summary>
    public IReadOnlyList<ServerDownload> DownloadsFor(IEnumerable<Track> tracks)
    {
        var paths = tracks.Where(t => !t.IsOnServerOnly).Select(t => t.Path).ToHashSet(StringComparer.Ordinal);
        if (paths.Count == 0)
            return [];
        var keep = ProtectedPaths();
        return Items.SelectMany(i => _database.GetServerDownloads(i.Server.Id))
            .Where(d => paths.Contains(d.Path) && !keep.Contains(d.Path)).ToList();
    }

    /// <summary>The downloads of a server's folder and those below it, that may be removed now.</summary>
    public IReadOnlyList<ServerDownload> DownloadsInFolder(long serverId, string folder)
    {
        var ids = folder.StartsWith(FolderNode.ServerPlaylistsPath, StringComparison.Ordinal)
            ? PlaylistIdOf(folder) is { } playlist ? (_database.GetServerPlaylist(serverId, playlist)?.SongIds ?? []).ToHashSet() : []
            : _database.SearchServerCatalog(serverId, folder, null).Songs.Select(s => s.Id).ToHashSet();
        var keep = ProtectedPaths();
        return _database.GetServerDownloads(serverId).Where(d => ids.Contains(d.SongId) && !keep.Contains(d.Path)).ToList();
    }

    /// <summary>"42 songs, 310 MB".</summary>
    public static string Describe(IReadOnlyCollection<ServerDownload> downloads) =>
        $"{(downloads.Count == 1 ? "1 song" : $"{Format.Count(downloads.Count)} songs")}, {Format.Size(downloads.Sum(d => d.Size))}";

    /// <summary>Asks, then removes these downloads from the computer (Remove from this computer).</summary>
    public async Task RemoveFromLaptopAsync(IReadOnlyList<ServerDownload> downloads)
    {
        if (downloads.Count == 0)
        {
            _owner.ShowNotice("Nothing to remove: those aren't songs GZJam downloaded, or they're on a deck, in the queue, or played tonight.", seconds: 6);
            return;
        }
        string where = string.Join(" and ", downloads.Select(d => ItemFor(d.ServerId)?.Name).OfType<string>().Distinct());
        if (_owner.ConfirmAsync is null || !await _owner.ConfirmAsync(
                downloads.Count == 1 ? "Remove this song from this computer?" : $"Remove {Format.Count(downloads.Count)} songs from this computer?",
                $"They stay on {where}, and come back when you ask for one. Their hearts, cues and EQ are kept. Frees {Format.Size(downloads.Sum(d => d.Size))}.",
                "Remove"))
            return;
        await RemoveNowAsync(downloads);
    }

    /// <summary>Removes these downloads from the computer, without asking (Free up space… has asked).</summary>
    public async Task RemoveNowAsync(IReadOnlyList<ServerDownload> downloads)
    {
        var keep = ProtectedPaths();
        var removable = downloads.Where(d => !keep.Contains(d.Path)).DistinctBy(d => (d.ServerId, d.SongId)).ToList();
        var removed = await Task.Run(() =>
        {
            var gone = new List<ServerDownload>();
            foreach (ServerDownload download in removable)
            {
                try
                {
                    if (File.Exists(download.Path))
                        File.Delete(download.Path);
                    gone.Add(download);
                    RemoveEmptyFolders(Path.GetDirectoryName(download.Path));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Log.Info($"Servers: couldn't remove {download.Path}: {e.Message}");
                }
            }
            foreach (var server in gone.GroupBy(d => d.ServerId))
                _database.MarkServerSongsRemoved(server.Key, server.Select(d => d.SongId), DateTime.UtcNow);
            return gone;
        });
        long freed = removed.Sum(d => d.Size);
        Log.Info($"Servers: removed {removed.Count} downloaded songs from this computer, {Format.Size(freed)}");
        _owner.ShowNotice(removed.Count == removable.Count
            ? $"Removed {Describe(removed)} from this computer."
            : $"Removed {Describe(removed)}; {removable.Count - removed.Count} couldn't be (in use?): the activity log says which.", seconds: 6);
        AfterSync();
        foreach (ServerItemViewModel item in Items)
            RefreshKept(item);
        RefreshDownloads();
        _owner.Library.RefreshResults();
    }

    /// <summary>
    /// Folders left empty under the downloads folder (or <paramref name="under"/>) go too, up to
    /// (not including) it; one with nothing left but the album's picture (a cover.jpg GZJam
    /// brought from the server) is empty too.
    /// </summary>
    private void RemoveEmptyFolders(string? folder, string? under = null)
    {
        string top = Path.TrimEndingDirectorySeparator(Path.GetFullPath(under ?? DownloadsFolder));
        while (folder is not null && Path.GetFullPath(folder).Length > top.Length
               && Path.GetFullPath(folder).StartsWith(top, StringComparison.OrdinalIgnoreCase)
               && Directory.Exists(folder) && OnlyArtLeft(folder) is { } art)
        {
            foreach (string picture in art)
                File.Delete(picture);
            Directory.Delete(folder);
            folder = Path.GetDirectoryName(folder);
        }
    }

    private static readonly string[] ArtNames = ["cover", "folder", "front"];
    private static readonly string[] ArtExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    /// <summary>The folder's album pictures if they're all that's in it (no songs, no folders); empty if it's empty; null otherwise.</summary>
    internal static List<string>? OnlyArtLeft(string folder)
    {
        if (Directory.EnumerateDirectories(folder).Any())
            return null;
        var files = Directory.EnumerateFiles(folder).ToList();
        return files.All(f => ArtNames.Contains(Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase)
                              && ArtExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            ? files : null;
    }
}
