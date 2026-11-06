using GZJam.Library;
using GZJam.Library.Servers;

namespace GZJam.App.ViewModels;

/// <summary>
/// The album art of a server's songs not on the laptop (#290): fetched small from the server
/// (getCoverArt) for the song list and the queue, once per album, and kept with the other small
/// copies, so it shows offline too. (Songs downloaded get theirs from the file, or the cover.jpg
/// a sync puts beside them.)
/// </summary>
public sealed partial class ServersViewModel
{
    /// <summary>How long a picture may take to come before it's left for later.</summary>
    private static readonly TimeSpan ArtWait = TimeSpan.FromSeconds(8);

    /// <summary>Each server, for the art thread: whether it answers now, and a client signed in to it (made when first needed).</summary>
    private volatile IReadOnlyDictionary<long, (bool Offline, Lazy<SubsonicClient?> Client)> _artServers =
        new Dictionary<long, (bool, Lazy<SubsonicClient?>)>();

    /// <summary>The servers as the art thread sees them; on the UI thread, when they're added, changed, or go and come.</summary>
    private void RefreshArtServers()
    {
        var old = _artServers;
        _artServers = Items.ToDictionary(i => i.Server.Id, i =>
        {
            var item = i;
            var client = old.TryGetValue(item.Server.Id, out var before) && before.Client.IsValueCreated
                ? before.Client
                : new Lazy<SubsonicClient?>(() => SignedIn(item));
            return (item.IsOffline, client);
        });
    }

    /// <summary>
    /// A song's picture on its server, by its row's path (for <see cref="AlbumArtCache.ServerArt"/>):
    /// named by its album, so an album's songs share one. Null if it has none, or its server isn't
    /// answering now. Called on the art thread.
    /// </summary>
    public (string Name, Func<byte[]?> Fetch)? ArtFor(string path)
    {
        if (ServerTrack.Parse(path) is not var (serverId, songId) || !_artServers.TryGetValue(serverId, out var server)
            || _database.GetServerCatalogSong(serverId, songId) is not { CoverArt: { Length: > 0 } art } song)
            return null;
        string name = $"{serverId}/{song.AlbumId ?? art}";
        return (name, () =>
        {
            if (server.Offline || server.Client.Value is not { } client)
                return null;
            try
            {
                using var wait = new CancellationTokenSource(ArtWait);
                return client.GetCoverArtAsync(art, AlbumArtCache.Size, wait.Token).GetAwaiter().GetResult();
            }
            catch (Exception e) when (e is ServerException or OperationCanceledException)
            {
                return null;
            }
        });
    }
}
