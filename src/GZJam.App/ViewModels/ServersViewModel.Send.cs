using GZJam.Library;
using GZJam.Library.Servers;

namespace GZJam.App.ViewModels;

/// <summary>
/// A playlist of the DJ's, sent to a server, and shared from there (#303): the night's set, saved
/// as a playlist, posted the next day. GZJam knows which of the server's songs each file is, so
/// those go up, in the playlist's order; the rest are named first and left out.
/// </summary>
public sealed partial class ServersViewModel
{
    /// <summary>How many of the songs left out are named before saying how many more.</summary>
    private const int LeftOutNamed = 8;

    /// <summary>"Send to synology": makes the playlist there, or brings the one it made up to date. Its ID there; null if it wasn't sent.</summary>
    public async Task<string?> SendPlaylistAsync(ServerItemViewModel item, PlaylistInfo playlist)
    {
        if (ClientOrSay(item) is not { } client)
            return null;
        var (ids, leftOut) = await SongsOnServerAsync(item, playlist);
        if (ids.Count == 0)
        {
            _owner.ShowNotice($"None of {playlist.Name}'s songs are on {item.Name}, so there's nothing to send.", seconds: 8);
            return null;
        }
        if (leftOut.Count > 0)
        {
            if (_owner.ConfirmAsync is null
                || !await _owner.ConfirmAsync($"Send {playlist.Name} to {item.Name}?", LeftOutMessage(item, leftOut), "Send"))
                return null;
        }

        long serverId = item.Server.Id;
        try
        {
            string? known = _database.GetSentPlaylist(playlist.Id, serverId);
            bool gone = known is not null && !await IsThereAsync(client, known);
            string id = await client.SendPlaylistAsync(gone ? null : known, playlist.Name, ids);
            _database.SaveSentPlaylist(playlist.Id, serverId, id);
            _database.SaveServerPlaylist(serverId, new ServerPlaylistCopy(id, playlist.Name, ids));
            _owner.Library.RefreshServers();
            Log.Info($"Servers: {playlist.Name} sent to {item.Name} as {id}: {ids.Count} songs, {leftOut.Count} left out");
            string songs = ids.Count == 1 ? "1 song" : $"{Format.Count(ids.Count)} songs";
            string left = leftOut.Count > 0 ? $", {Format.Count(leftOut.Count)} left out" : "";
            _owner.ShowNotice(gone
                ? $"The {playlist.Name} GZJam made on {item.Name} had been deleted there, so it's been made again: {songs}{left}."
                : $"{playlist.Name} is on {item.Name}: {songs}{left}.", seconds: 8);
            return id;
        }
        catch (Exception e) when (e is ServerException or HttpRequestException or TaskCanceledException)
        {
            Log.Info($"Servers: couldn't send {playlist.Name} to {item.Name}: {e.Message}");
            _owner.ShowNotice($"Couldn't send {playlist.Name} to {item.Name}: {e.Message}", seconds: 10);
            return null;
        }
    }

    /// <summary>
    /// "Share a link…": sends the playlist if it isn't there yet, makes a link anyone can open, and
    /// copies it, ready to paste into the post. Returns the link; null if none was made.
    /// </summary>
    public async Task<string?> SharePlaylistAsync(ServerItemViewModel item, PlaylistInfo playlist)
    {
        if (ClientOrSay(item) is not { } client || _owner.PromptAsync is null)
            return null;
        if (await _owner.PromptAsync($"Share {playlist.Name}", $"Anyone with the link can open and listen to it on {item.Name}. What should it say?",
                "Make the link", playlist.Name) is not { } description)
            return null;
        try
        {
            string? id = _database.GetSentPlaylist(playlist.Id, item.Server.Id);
            if (id is null || !await IsThereAsync(client, id))
                id = await SendPlaylistAsync(item, playlist);
            if (id is null)
                return null;

            string link = await client.CreateShareAsync(id, description);
            if (_owner.CopyTextAsync is { } copy)
                await copy(link);
            Log.Info($"Servers: {playlist.Name} shared from {item.Name}");
            _owner.ShowNotice($"Link copied, ready to paste: {link}", seconds: 12);
            return link;
        }
        catch (ServerException e) when (e.Outcome is ServerCheckOutcome.NotSubsonic or ServerCheckOutcome.Failed)
        {
            // Navidrome with sharing off answers "not implemented"; a server without shares says no.
            Log.Info($"Servers: {item.Name} made no link for {playlist.Name}: {e.Message}");
            _owner.ShowNotice($"{item.Name} didn't make a link. Turn on sharing in Navidrome (EnableSharing) to make links.", seconds: 12);
            return null;
        }
        catch (Exception e) when (e is ServerException or HttpRequestException or TaskCanceledException)
        {
            Log.Info($"Servers: couldn't share {playlist.Name} from {item.Name}: {e.Message}");
            _owner.ShowNotice($"Couldn't share {playlist.Name} from {item.Name}: {e.Message}", seconds: 10);
            return null;
        }
    }

    private SubsonicClient? ClientOrSay(ServerItemViewModel item)
    {
        if (SignedIn(item) is { } client)
            return client;
        _owner.ShowNotice($"GZJam doesn't have the password for {item.Name} on this computer: enter it in Settings › Servers.", seconds: 8);
        return null;
    }

    private static async Task<bool> IsThereAsync(SubsonicClient client, string playlistId) =>
        (await client.GetPlaylistsAsync()).Any(p => p.Id == playlistId);

    /// <summary>A playlist's songs as the server knows them, in its order, and the titles of those it doesn't have.</summary>
    private Task<(List<string> Ids, List<string> LeftOut)> SongsOnServerAsync(ServerItemViewModel item, PlaylistInfo playlist) =>
        Task.Run(() =>
        {
            var idOf = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (songId, path) in LocalCopies(item))
                idOf.TryAdd(path, songId);
            var ids = new List<string>();
            var leftOut = new List<string>();
            foreach (Track song in _owner.PlaylistSongs(playlist))
            {
                if (idOf.TryGetValue(song.Path, out string? id))
                    ids.Add(id);
                else
                    leftOut.Add(song.DisplayTitle);
            }
            return (ids, leftOut);
        });

    private static string LeftOutMessage(ServerItemViewModel item, List<string> leftOut)
    {
        string names = string.Join(Environment.NewLine, leftOut.Take(LeftOutNamed));
        if (leftOut.Count > LeftOutNamed)
            names += $"{Environment.NewLine}and {Format.Count(leftOut.Count - LeftOutNamed)} more";
        return $"{(leftOut.Count == 1 ? "This song isn't" : "These songs aren't")} on {item.Name}, so {(leftOut.Count == 1 ? "it'll" : "they'll")} be left out:{Environment.NewLine}{names}";
    }
}
