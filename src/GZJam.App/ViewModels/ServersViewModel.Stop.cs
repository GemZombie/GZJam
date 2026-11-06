using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>
/// Stopping what comes down from a server (Danny: "I just told it to sync the wrong playlist, and
/// it's big"): the top bar's Stop, beside the downloads' progress, and Stop keeping on this computer
/// on a server's playlist that's kept, which can take away what was downloaded for it too.
/// </summary>
public sealed partial class ServersViewModel
{
    /// <summary>Whether there's something coming down to stop: a sync or songs being kept, or a whole server.</summary>
    public bool CanStopInBackground => Items.Any(i => i.IsSyncing) || (_wholeRuns.Count > 0 && KeepWholeStatus is not null);

    /// <summary>
    /// The top bar's Stop: each sync, and songs being kept, stop after the song coming down (what
    /// came stays, and SYNC goes on from there); keeping a whole server is paused.
    /// </summary>
    public void StopInBackground()
    {
        var syncing = Items.Where(i => i.IsSyncing).ToList();
        foreach (ServerItemViewModel item in syncing)
            StopSync(item);
        foreach (long id in _wholeRuns.Keys.ToList())
            PauseKeepWhole(id);
        if (syncing.Count > 0)
            Log.Info($"Servers: stopped from the top bar: {string.Join(", ", syncing.Select(i => i.Name))}");
    }

    /// <summary>
    /// "Stop keeping on this computer" on one of the server's playlists that's kept: after asking, it's
    /// no longer downloaded or kept up to date (a sync running stops first), its copy in the DJ's
    /// playlists stays as theirs, and then the songs downloaded only for it can be removed from the
    /// laptop (asked separately: they stay on the server).
    /// </summary>
    public async Task StopKeepingPlaylistAsync(long serverId, string playlistId)
    {
        if (ItemFor(serverId) is not { } item
            || _database.GetServerChoices(serverId).FirstOrDefault(c => c.Kind == ServerChoiceKind.Playlist && c.RemoteId == playlistId) is not { } choice)
            return;
        if (_owner.ConfirmAsync is null || !await _owner.ConfirmAsync($"Stop keeping {choice.Name} on this computer?",
                $"It's no longer downloaded or kept up to date with {item.Name}"
                + (item.IsSyncing ? ", and the sync running now stops (SYNC brings the rest of what's kept)" : "")
                + ". Your copy in your playlists stays, as yours. Then you can remove the songs downloaded for it.",
                "Stop keeping"))
            return;

        // No longer chosen first: a sync stopping now makes no copy of it as it ends.
        var left = _database.GetServerChoices(serverId).Where(c => !(c.Kind == ServerChoiceKind.Playlist && c.RemoteId == playlistId)).ToList();
        _database.SetServerChoices(serverId, left);
        Log.Info($"Servers: no longer keeping {choice.Name} from {item.Name} on this computer");
        if (item.IsSyncing)
        {
            StopSync(item);
            var until = DateTime.UtcNow.AddSeconds(30);
            while (item.IsSyncing && DateTime.UtcNow < until)
                await Task.Delay(100);   // the song coming down stops, and the sync with it
        }
        RefreshKept(item);
        _owner.Library.RefreshPlaylists();
        _owner.Library.RefreshServers();

        var keep = ProtectedPaths();   // the queue and decks: read here, on the window's thread
        var downloads = await Task.Run(() => DownloadsOnlyFor(item, playlistId, left, keep));
        if (downloads.Count == 0)
        {
            _owner.ShowNotice($"{choice.Name} is no longer kept on this computer.", seconds: 5);
            return;
        }
        await RemoveFromLaptopAsync(downloads);
    }

    /// <summary>
    /// The songs downloaded for a server playlist that nothing else kept needs: not in another kept
    /// playlist, album, folder or song, and not on a deck, in the queue or played tonight.
    /// </summary>
    private List<ServerDownload> DownloadsOnlyFor(ServerItemViewModel item, string playlistId, IReadOnlyList<ServerChoice> others, HashSet<string> keep)
    {
        long serverId = item.Server.Id;
        var ids = (_database.GetServerPlaylist(serverId, playlistId)?.SongIds ?? []).ToHashSet(StringComparer.Ordinal);
        var needed = new HashSet<string>(StringComparer.Ordinal);
        foreach (ServerChoice other in others)
            needed.UnionWith(other.Kind switch
            {
                ServerChoiceKind.Playlist => _database.GetServerPlaylist(serverId, other.RemoteId)?.SongIds ?? [],
                ServerChoiceKind.Song => [other.RemoteId],
                ServerChoiceKind.Album => _database.GetServerCatalogAlbumSongIds(serverId, other.RemoteId),
                ServerChoiceKind.Folder => _database.SearchServerCatalog(serverId, other.RemoteId, null).Songs.Select(s => s.Id).ToList(),
                _ => (IReadOnlyList<string>)[],
            });
        return _database.GetServerDownloads(serverId)
            .Where(d => ids.Contains(d.SongId) && !needed.Contains(d.SongId) && !keep.Contains(d.Path))
            .ToList();
    }
}
