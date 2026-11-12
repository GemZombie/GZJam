using GZJam.Library;
using GZJam.Library.Servers;

namespace GZJam.App.ViewModels;

/// <summary>
/// A server's playlists in the library (#290): under "Playlists" in its tree, each opened in the
/// song list like a folder (the songs not on the computer marked), and kept on the computer from
/// there. They're read with the server's catalog, and again when one is opened while the server
/// answers, so they show offline too, as last read.
/// </summary>
public sealed partial class ServersViewModel
{
    /// <summary>How long opening a playlist waits for the server's latest before showing the one last read.</summary>
    private static readonly TimeSpan PlaylistWait = TimeSpan.FromSeconds(4);

    /// <summary>The playlist's ID on the server, from its place in the tree; null for "Playlists" itself.</summary>
    internal static string? PlaylistIdOf(string folder) =>
        folder.Length > FolderNode.ServerPlaylistsPath.Length + 1 ? folder[(FolderNode.ServerPlaylistsPath.Length + 1)..] : null;

    /// <summary>The GZJam playlist a server playlist is kept as on the computer, if it is.</summary>
    public PlaylistInfo? KeptAs(long serverId, string playlistId)
    {
        long? id = _database.GetServerChoices(serverId)
            .FirstOrDefault(c => c.Kind == ServerChoiceKind.Playlist && c.RemoteId == playlistId)?.PlaylistId;
        return id is null ? null : _database.GetPlaylists().FirstOrDefault(p => p.Id == id);
    }

    /// <summary>Whether a server playlist is among what's kept on the computer (synced yet or not).</summary>
    public bool IsKept(long serverId, string playlistId) =>
        _database.GetServerChoices(serverId).Any(c => c.Kind == ServerChoiceKind.Playlist && c.RemoteId == playlistId);

    /// <summary>
    /// "Stop following the server" on a kept playlist: it's the DJ's own from now on, as it is (its
    /// songs stay on the computer), and a sync no longer changes it. It moves from under the server
    /// in the Playlists panel to the DJ's own (#355).
    /// </summary>
    public void StopFollowing(PlaylistInfo playlist)
    {
        foreach (ServerItemViewModel item in Items)
        {
            var choices = _database.GetServerChoices(item.Server.Id);
            if (!choices.Any(c => c.Kind == ServerChoiceKind.Playlist && c.PlaylistId == playlist.Id))
                continue;
            _database.SetServerChoices(item.Server.Id, choices.Where(c => !(c.Kind == ServerChoiceKind.Playlist && c.PlaylistId == playlist.Id)));
            Log.Info($"Servers: {playlist.Name} no longer follows {item.Name}: it's the DJ's own");
            RefreshKept(item);
            _owner.ShowNotice($"{playlist.Name} is yours now: a sync with {item.Name} no longer changes it.", seconds: 6);
        }
        _owner.Library.RefreshPlaylists();
        _owner.Library.RefreshServers();
    }

    /// <summary>"Keep on this computer" on one of the server's playlists: kept from now on, and synced now.</summary>
    public async Task KeepPlaylistAsync(long serverId, string playlistId)
    {
        if (ItemFor(serverId) is not { } item || _database.GetServerPlaylist(serverId, playlistId) is not { } playlist)
            return;
        if (item.IsPreviewOnly)
        {
            _owner.ShowNotice($"{item.Name} doesn't let this account download songs: they can be heard in the headphones only.", seconds: 8);
            return;
        }
        AddChoices(item, [new ServerChoice(ServerChoiceKind.Playlist, playlist.Id, playlist.Name)]);
        await SyncAsync(item);
        _owner.Library.RefreshServers();
    }

    /// <summary>A playlist's songs for the song list, in its order (or what <paramref name="text"/> finds in it).</summary>
    private async Task<ServerBrowse> BrowsePlaylistAsync(ServerItemViewModel item, string folder, string text, CancellationToken cancel)
    {
        if (PlaylistIdOf(folder) is not { } id)
            return new([], $"Choose one of {item.Name}'s playlists, under Playlists.");
        ServerPlaylistCopy? copy = _database.GetServerPlaylist(item.Server.Id, id);
        IReadOnlyList<ServerSong>? live = null;

        // Its latest, when the server answers soon enough; else as last read.
        if (string.IsNullOrWhiteSpace(text) && !item.IsOffline && SignedIn(item) is { } client)
        {
            try
            {
                using var quick = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                quick.CancelAfter(PlaylistWait);
                live = await Task.Run(() => client.GetPlaylistSongsAsync(id, quick.Token), quick.Token);
                copy = new ServerPlaylistCopy(id, copy?.Name ?? "", live.Select(s => s.Id).ToList());
                if (copy.Name.Length > 0)
                    _database.SaveServerPlaylist(item.Server.Id, copy);
            }
            catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
            {
                Log.Info($"Servers: {item.Name} was slow to send a playlist: showing it as last read");
            }
            catch (ServerException e)
            {
                if (e.Outcome == ServerCheckOutcome.Unreachable)
                    MarkOffline(item);
                Log.Info($"Servers: couldn't read a playlist from {item.Name}: {e.Message}");
            }
        }
        if (copy is null)
            return new([], $"That playlist isn't on {item.Name} any more.");

        var songs = live ?? await Task.Run(() => _database.GetServerCatalogSongs(item.Server.Id, copy.SongIds), cancel);
        int missing = copy.SongIds.Count - songs.Count;   // not in the catalog as last read
        string? note = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            CatalogSearch found = await Task.Run(() => _database.SearchServerCatalog(item.Server.Id, "", text, cancel), cancel);
            var matching = found.Songs.Select(s => s.Id).ToHashSet();
            songs = songs.Where(s => matching.Contains(s.Id)).ToList();
            if (found.LeftOut.Count > 0)
                note = $"{string.Join(" and ", found.LeftOut)} {(found.LeftOut.Count == 1 ? "is" : "are")} for the songs on this computer: left out here.";
        }
        var rows = await RowsForAsync(item, songs, cancel);
        note ??= KeptAs(item.Server.Id, id) is not null
            ? "Kept on this computer, and up to date with each sync."
            : missing > 0 ? $"{(missing == 1 ? "1 song" : $"{Format.Count(missing)} songs")} in it aren't in {item.Name}'s song list yet: Read its song list again." : null;
        return new(rows, Note: note);
    }

    /// <summary>The server's playlists, read with its catalog. One that can't be read leaves those read before.</summary>
    /// <returns>Whether they were read (a server that doesn't answer is only logged).</returns>
    private async Task<bool> ReadPlaylistsAsync(ServerItemViewModel item, SubsonicClient client)
    {
        try
        {
            var playlists = await Task.Run(() => ServerCatalogReader.ReadPlaylistsAsync(client));
            await Task.Run(() => _database.ReplaceServerPlaylists(item.Server.Id, playlists));
            Log.Info($"Servers: read {item.Name}'s {Format.Count(playlists.Count)} playlists");
            return true;
        }
        catch (ServerException e)
        {
            Log.Info($"Servers: couldn't read {item.Name}'s playlists: {e.Message}");
            return false;
        }
    }
}
