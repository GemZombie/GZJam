using GZJam.Library;
using GZJam.Library.Servers;

namespace GZJam.App.ViewModels;

/// <summary>
/// Hearts back to the server (#286): a ♥ on a song from a server (downloaded, kept, or the DJ's
/// own copy of one) stars it there, under the DJ's account, and taking the ♥ off (or a broken
/// heart) takes the star off, so Navidrome's own app and web page show the same favorites. Server
/// by server: Settings › Servers can leave one out. A server that can't be reached is told once
/// it answers; the latest heart for a song is what's sent.
/// </summary>
public sealed partial class ServersViewModel
{
    private bool _sendingHearts, _sendHeartsAgain;

    public bool SendsHearts(long serverId) => !_settings.ServerHeartsNotSent.Contains(serverId);

    /// <summary>Hearts go to this server, or not (those waiting to go are forgotten).</summary>
    public void SetSendsHearts(long serverId, bool send)
    {
        if (send == SendsHearts(serverId))
            return;
        if (send)
            _settings.ServerHeartsNotSent.Remove(serverId);
        else
        {
            _settings.ServerHeartsNotSent.Add(serverId);
            _database.ForgetServerHeartsOut(serverId);
        }
        Log.Info($"Servers: hearts {(send ? "go" : "no longer go")} to {ItemFor(serverId)?.Name}");
    }

    /// <summary>Hearts were set on these songs: those that are a server's are starred there (or not).</summary>
    internal async Task HeartsChangedAsync(IReadOnlyList<string> paths, Rating rating)
    {
        var sending = Items.Where(i => SendsHearts(i.Server.Id)).Select(i => i.Server.Id).ToHashSet();
        if (paths.Count == 0 || sending.Count == 0)
            return;
        bool starred = rating == Rating.Loved;
        try
        {
            int songs = await Task.Run(() =>
            {
                var found = _database.GetServerSongsAt(paths).Where(s => sending.Contains(s.ServerId)).ToList();
                if (found.Count > 0)
                    _database.AddServerHeartsOut(found.Select(s => (s.ServerId, s.SongId, starred)));
                return found.Count;
            });
            if (songs > 0)
                await SendHeartsAsync();
        }
        catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            Log.Info($"Servers: couldn't note hearts for the server: {e.Message}");
        }
    }

    /// <summary>Tells each server that answers the hearts waiting for it, one pass at a time.</summary>
    public async Task SendHeartsAsync()
    {
        if (_sendingHearts)
        {
            _sendHeartsAgain = true;
            return;
        }
        _sendingHearts = true;
        try
        {
            do
            {
                _sendHeartsAgain = false;
                foreach (ServerItemViewModel item in Items.ToList())
                    if (!_closed && SendsHearts(item.Server.Id) && !item.IsOffline && SignedIn(item) is { } client)
                        await SendHeartsAsync(item, client);
            }
            while (_sendHeartsAgain);
        }
        finally
        {
            _sendingHearts = false;
        }
    }

    private async Task SendHeartsAsync(ServerItemViewModel item, SubsonicClient client)
    {
        long id = item.Server.Id;
        var hearts = await Task.Run(() => _database.GetServerHeartsOut(id));
        int sent = 0;
        foreach (var (songId, starred) in hearts)
        {
            try
            {
                await client.StarAsync(songId, starred);
                sent++;
            }
            catch (ServerException e) when (e.Outcome == ServerCheckOutcome.Unreachable)
            {
                MarkOffline(item);   // told once it answers
                break;
            }
            catch (ServerException e) when (e.Outcome == ServerCheckOutcome.WrongSignIn)
            {
                Log.Info($"Servers: {item.Name} didn't take the hearts: {e.Message}");   // kept, for when it's signed in again
                break;
            }
            catch (ServerException e)
            {
                Log.Info($"Servers: {item.Name} didn't take the heart for song {songId}: {e.Message}");   // gone from it, say: not asked again
            }
            catch (Exception e) when (e is OperationCanceledException or HttpRequestException)
            {
                break;
            }
            await Task.Run(() => _database.ServerHeartSent(id, songId, starred));
        }
        if (sent > 0)
            Log.Info($"Servers: told {item.Name} the hearts of {Format.Count(sent)} {(sent == 1 ? "song" : "songs")}");
    }
}

public sealed partial class ServerItemViewModel
{
    /// <summary>Settings › Servers: a ♥ on one of this server's songs stars it there.</summary>
    public bool SendHearts
    {
        get => _owner.SendsHearts(Server.Id);
        set
        {
            _owner.SetSendsHearts(Server.Id, value);
            OnPropertyChanged();
        }
    }
}
