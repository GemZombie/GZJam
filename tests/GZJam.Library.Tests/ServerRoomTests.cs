using GZJam.Library.Servers;
using GZJam.Testing;

namespace GZJam.Library.Tests;

/// <summary>
/// What kept lists still need, known offline, and making room for a sync past the downloads'
/// limit (#286): never with what the sync keeps, and a song removed to make room isn't
/// downloaded again in the same sync.
/// </summary>
public sealed class ServerRoomTests : IDisposable
{
    private readonly TempLibrary _lib = new();
    private readonly FakeNavidrome _server = new();
    private readonly MusicServer _record;

    public ServerRoomTests()
    {
        _record = _lib.Database.AddServer("synology", "http://synology:4533", "gzjam");
        Downloads = Path.Combine(_lib.Directory, "GZJam from servers");
    }

    public void Dispose() => _lib.Dispose();

    private string Downloads { get; }

    private ServerSync Sync(long? limit = null, Func<long, IReadOnlySet<string>, IReadOnlyList<(string, long)>>? makeRoom = null) =>
        new(_lib.Database, _server.Client(), _record, Downloads) { Limit = limit, MakeRoom = makeRoom, KeepFree = 0 };

    private void Choose(params ServerChoice[] choices) => _lib.Database.SetServerChoices(_record.Id, choices);

    private static ServerChoice Song(string id, string name) => new(ServerChoiceKind.Song, id, name);

    private long Size(string id) => FakeNavidrome.Bytes(id).Length;

    [Fact]
    public async Task What_the_lists_brought_is_kept_with_them_and_a_song_downloaded_on_the_spot_isnt_kept_for_itself()
    {
        Choose(new ServerChoice(ServerChoiceKind.Playlist, "pl1", "Friday"), Song("s4", "Mama"));
        await Sync().RunAsync(cancel: TestContext.Current.CancellationToken);
        Assert.Equal(["s1", "s2", "s3", "s4"], _lib.Database.GetServerSongsKeptByChoices(_record.Id).Order());

        // s4 again, as if fetched for the queue: a song choice of its own, downloaded on the spot
        string path = _lib.Database.GetServerSong(_record.Id, "s4")!.Path;
        File.Delete(path);
        _lib.Database.ForgetServerSong(_record.Id, "s4");
        var s4 = (await _server.Client().GetSongAsync("s4", TestContext.Current.CancellationToken))!;
        await Sync().GetOneAsync(s4, TestContext.Current.CancellationToken);

        Assert.Equal(["s1", "s2", "s3"], _lib.Database.GetServerSongsKeptByChoices(_record.Id).Order());
    }

    [Fact]
    public async Task Past_the_limit_room_is_made_from_what_the_sync_doesnt_keep()
    {
        var s1 = (await _server.Client().GetSongAsync("s1", TestContext.Current.CancellationToken))!;
        await Sync().GetOneAsync(s1, TestContext.Current.CancellationToken);   // on the spot, earlier
        Choose(Song("s1", "b Basie Boogie"), Song("s4", "a Mama"));
        var asked = new List<(long Bytes, string[] Keeping)>();

        SyncResult result = await Sync(limit: Size("s1") + Size("s4") - 1, makeRoom: (bytes, keeping) =>
        {
            asked.Add((bytes, keeping.Order().ToArray()));
            var copy = _lib.Database.GetServerSong(_record.Id, "s1")!;
            File.Delete(copy.Path);
            _lib.Database.ForgetServerDownloads(_record.Id, ["s1"]);
            return [("s1", Size("s1"))];
        }).RunAsync(cancel: TestContext.Current.CancellationToken);

        Assert.True(result.Finished, result.Stopped);
        var (bytes, keeping) = Assert.Single(asked);
        Assert.Equal(1, bytes);   // just over the limit
        Assert.Equal(["s4"], keeping);   // s1 came on the spot: not kept for itself
        Assert.Equal(1, result.Downloaded);   // s4; s1 made room, and isn't downloaded again now
        Assert.Equal(["s4"], _lib.Database.GetServerSongs(_record.Id).Select(s => s.SongId));
        Assert.DoesNotContain(_lib.Database.GetServerChoices(_record.Id), c => c.RemoteId == "s1");
        Assert.Empty(_lib.Database.GetRemovedServerSongs(_record.Id));   // not held back: a list that wants it brings it again
    }

    [Fact]
    public async Task With_nothing_to_make_room_with_the_sync_stops_as_before()
    {
        Choose(new ServerChoice(ServerChoiceKind.Playlist, "pl1", "Friday"));
        SyncResult result = await Sync(limit: Size("s2") + 1, makeRoom: (_, _) => []).RunAsync(cancel: TestContext.Current.CancellationToken);

        Assert.Equal("The room set aside for downloaded songs is full.", result.Stopped);
        Assert.Equal(1, result.Downloaded);
    }

    [Fact]
    public void Hearts_waiting_for_a_server_keep_the_latest_and_go_once_sent()
    {
        _lib.Database.AddServerHeartsOut([(_record.Id, "s1", true), (_record.Id, "s2", true)]);
        _lib.Database.AddServerHeartsOut([(_record.Id, "s1", false)]);
        Assert.Equal([("s1", false), ("s2", true)], _lib.Database.GetServerHeartsOut(_record.Id).OrderBy(h => h.SongId));

        _lib.Database.ServerHeartSent(_record.Id, "s1", true);   // an older heart: the newer one waits still
        _lib.Database.ServerHeartSent(_record.Id, "s2", true);
        Assert.Equal([("s1", false)], _lib.Database.GetServerHeartsOut(_record.Id));
    }
}
