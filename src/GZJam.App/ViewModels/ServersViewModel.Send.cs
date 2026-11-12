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
        // With the server's playlist folders in names (#355), one in the DJ's folders goes with
        // them in its name ("WCS/Setlists/Nov 8"), and shows in the same folders in its apps.
        string sentName = PlaylistNames.Join(_database.PlaylistFolderPath(playlist.FolderId), playlist.Name, item.Server.PlaylistSeparator);
        try
        {
            string? known = _database.GetSentPlaylist(playlist.Id, serverId);
            IReadOnlyList<ServerPlaylist> there = await client.GetPlaylistsAsync();
            bool gone = known is not null && !there.Any(p => p.Id == known);
            string? target = gone ? null : known;
            bool replaced = false;
            if (target is null)
            {
                // Not sent here before: one of that name already there, made elsewhere (#396)?
                switch (await SameNameAsync(item, sentName, there))
                {
                    case null:
                        return null;
                    case { Length: > 0 } existing:
                        target = existing;
                        replaced = true;
                        break;
                }
            }
            string id = await client.SendPlaylistAsync(target, sentName, ids);
            _database.SaveSentPlaylist(playlist.Id, serverId, id);
            SavePlaylistCopy(serverId, new ServerPlaylistCopy(id, sentName, ids));
            _owner.Library.RefreshServers();
            Log.Info($"Servers: {playlist.Name} sent to {item.Name} as {id}: {ids.Count} songs, {leftOut.Count} left out");
            string songs = ids.Count == 1 ? "1 song" : $"{Format.Count(ids.Count)} songs";
            string left = leftOut.Count > 0 ? $", {Format.Count(leftOut.Count)} left out" : "";
            _owner.ShowNotice(replaced
                ? $"{item.Name}'s {playlist.Name} has this playlist's songs now: {songs}{left}."
                : gone
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

    /// <summary>
    /// A playlist GZJam hasn't sent to this server, whose name the server has already (#396): made
    /// in its web page, another app, or by GZJam on another computer. Rather than a second one of
    /// that name beside it, the DJ is asked. The answer: "" to send it as another one (also with
    /// nothing of that name there), the ID of the one to replace, or null for Cancel. Another
    /// user's playlist can't be changed, and of several, GZJam doesn't guess which: then only
    /// another one, or Cancel.
    /// </summary>
    private async Task<string?> SameNameAsync(ServerItemViewModel item, string name, IReadOnlyList<ServerPlaylist> there)
    {
        var same = there.Where(p => string.Equals(p.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (same.Count == 0)
            return "";
        ServerPlaylist first = same[0];
        bool theirs = first.Owner is { Length: > 0 } owner && !string.Equals(owner, item.Server.UserName, StringComparison.OrdinalIgnoreCase);
        bool canReplace = same.Count == 1 && !theirs;
        string message = same.Count > 1
            ? $"It has {same.Count} of them, so GZJam can't tell which one to replace. Send this as another one?"
            : theirs
            ? $"It's {first.Owner}'s, so GZJam can't change it. Send this as another one?"
            : "Replace its songs with this playlist's, in this order: it keeps its place on the server, so links and other apps follow it, and later sends update it. Or send this as another one of the same name.";
        string[] answers = canReplace ? ["Replace it", "Send it as another one"] : ["Send it as another one"];
        if (_owner.ChooseAsync is null
            || await _owner.ChooseAsync($"{item.Name} already has a playlist called {first.Name}.", message, answers) is not { } chosen)
            return null;
        Log.Info($"Servers: {name} on {item.Name} has the name of one there already: {(canReplace && chosen == 0 ? "replaced" : "sent as another one")}");
        return canReplace && chosen == 0 ? first.Id : "";
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
