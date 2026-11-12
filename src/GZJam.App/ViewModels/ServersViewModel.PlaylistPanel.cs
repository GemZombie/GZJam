using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>
/// A server's playlists in the library's Playlists panel (#355): read again from its heading (↻),
/// and each one's menu: add to the queue, play, keep on the computer or stop, and copy into a
/// playlist of the DJ's own. Their songs are the server's playlist's, as the song list shows it
/// open: those on the computer as they are, the rest fetched in their place.
/// </summary>
public sealed partial class ServersViewModel
{
    /// <summary>↻ on a server's heading: its playlists read again. Offline, they stay as last read, and it says so.</summary>
    public async Task ReadPlaylistsAgainAsync(long serverId)
    {
        if (ItemFor(serverId) is not { } item)
            return;
        if (item.IsOffline || SignedIn(item) is not { } client || !await ReadPlaylistsAsync(item, client))
        {
            _owner.ShowNotice($"{item.Name} isn't answering: its playlists are as last read.", seconds: 6);
            return;
        }
        _owner.Library.RefreshServerPlaylists();
        if (_owner.Library.SelectedServerPlaylist?.ServerId == serverId)
            _owner.Library.RefreshResults();
    }

    /// <summary>A server playlist's songs, in its order, as the song list shows it open.</summary>
    public async Task<IReadOnlyList<Track>> SongsOfAsync(ServerPlaylistRow playlist)
    {
        ServerBrowse found = await BrowseAsync(playlist.ServerId, FolderNode.ServerPlaylistPath(playlist.PlaylistId), "", CancellationToken.None);
        if (found.Problem is { } problem)
            _owner.ShowNotice(problem, seconds: 6);
        return found.Tracks;
    }

    public async Task AddPlaylistToQueueAsync(ServerPlaylistRow playlist, bool shuffled = false)
    {
        var songs = (await SongsOfAsync(playlist)).ToList();
        if (songs.Count == 0)
            return;
        if (shuffled)
            Random.Shared.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(songs));
        _owner.AddToQueue(songs);
        Log.Info($"Servers: {playlist.FullName} from {playlist.ServerName} added to the queue{(shuffled ? ", shuffled" : "")}: {songs.Count} songs");
        _owner.ShowNotice((songs.Count == 1 ? "1 song added to the queue" : $"{songs.Count} songs added to the queue") + (shuffled && songs.Count > 1 ? ", shuffled" : ""));
    }

    public async Task PlayPlaylistAsync(ServerPlaylistRow playlist, bool shuffled = false) =>
        _owner.PlaySongs(playlist.Name, (await SongsOfAsync(playlist)).ToList(), shuffled);

    /// <summary>
    /// Copy to my playlists: a playlist of the DJ's own with the same songs, in order, not linked to
    /// the server (a sync never changes it). Songs not on the computer come as they're fetched.
    /// </summary>
    public async Task CopyPlaylistAsync(ServerPlaylistRow playlist)
    {
        var songs = await SongsOfAsync(playlist);
        if (_owner.CreatePlaylist(playlist.Name, null, _owner.Library.NewPlaylistFolder) is not { } copy)
            return;
        if (songs.Count > 0)
            _owner.AddToPlaylist(copy, songs);
        Log.Info($"Servers: {playlist.FullName} from {playlist.ServerName} copied into the DJ's playlist {copy.Name}: {songs.Count} songs");
        _owner.ShowNotice($"\"{copy.Name}\" is in your playlists: {(songs.Count == 1 ? "1 song" : $"{songs.Count} songs")}, yours to change.", seconds: 6);
    }
}
