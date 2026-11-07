using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.Library;
using GZJam.Library.Servers;
using GZJam.Testing;

namespace GZJam.App.Tests;

/// <summary>
/// The rest of a server (#286): a ♥ stars the song on the server (told later if it can't be
/// reached), and downloads go by themselves: Keep downloaded songs (the night only, or 4 weeks
/// after they were last played), and the least recently played making room for a sync. Never a
/// kept list's songs, nor one in the queue.
/// </summary>
public sealed class ServerHeartsTidyAppTests
{
    private static async Task<(AppHarness App, FakeNavidrome Fake, ServerItemViewModel Server)> WithServer()
    {
        var app = new AppHarness();
        var fake = new FakeNavidrome();
        foreach (string id in (string[])["s1", "s2", "s3", "s4"])
        {
            string mp3 = Path.Combine(app.WorkDirectory, $"{id}.mp3");
            TestMp3.Write(mp3, new TestMp3.Tags(Title: id));
            fake.Files[id] = File.ReadAllBytes(mp3);
        }
        app.Settings.ServerDownloadsFolder = Path.Combine(app.WorkDirectory, "GZJam from servers");
        var servers = app.ViewModel.Servers;
        servers.CheckServer = (_, _, _, _) => fake.Down
            ? Task.FromResult(new ServerCheck(ServerCheckOutcome.Unreachable))
            : Task.FromResult(new ServerCheck(ServerCheckOutcome.Connected, "navidrome", "0.64.2", true, true, 4));
        servers.ClientFor = (server, password) => new SubsonicClient(new HttpClient(fake), server.Address, server.UserName, password) { RetryAfter = TimeSpan.Zero };
        Assert.True((await servers.SaveAsync(null, new ServerEdit("synology", "synology:4533", "gzjam", "sesame"))).Saved);
        var item = Assert.Single(servers.Items);
        WaitFor(() => app.Database.GetServerCatalogCount(item.Server.Id) == 4, "the catalog");
        app.ViewModel.ConfirmAsync = (_, _, _) => Task.FromResult(true);
        return (app, fake, item);
    }

    private static void Finish(Task task)
    {
        var until = DateTime.UtcNow.AddSeconds(20);
        while (!task.IsCompleted && DateTime.UtcNow < until)
            AppHarness.Settle(30);
        Assert.True(task.IsCompleted, "didn't finish");
        if (task.Exception?.InnerException is { } failure)
            throw failure;
        AppHarness.Settle(150);
    }

    private static void WaitFor(Func<bool> ready, string what)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (!ready() && DateTime.UtcNow < until)
            AppHarness.Settle(30);
        Assert.True(ready(), what);
    }

    private static readonly ServerChoice Friday = new(ServerChoiceKind.Playlist, "pl1", "Friday");   // s2, s1, s3

    private static string PathOf(AppHarness app, ServerItemViewModel server, string id) => app.Database.GetServerSong(server.Server.Id, id)!.Path;

    /// <summary>A server's song brought here on the spot, as for a deck.</summary>
    private static async Task<Track> OnTheSpot(AppHarness app, string id)
    {
        var library = app.ViewModel.Library;
        library.SelectedFolder = Assert.Single(library.Roots, r => r.IsServerItself);
        WaitFor(() => library.Results.Any(t => t.OnServer?.Song.Id == id), "the server's songs");
        Task<Track?> bringing = app.ViewModel.Servers.BringHereAsync(library.Results.First(t => t.OnServer?.Song.Id == id), "For Deck A");
        Finish(bringing);
        return Assert.IsType<Track>(await bringing);
    }

    // ───────────── Hearts ─────────────

    [AvaloniaFact]
    public async Task A_heart_on_a_servers_song_stars_it_there_and_off_unstars_it()
    {
        var (app, fake, server) = await WithServer();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        servers.SaveChoices(server, [Friday]);
        Finish(servers.SyncAsync(server));
        Track boogie = app.Database.TrackForFile(PathOf(app, server, "s1"));

        app.ViewModel.SetRating([boogie], Rating.Loved);
        WaitFor(() => fake.Stars.Count == 1, "the star");
        app.ViewModel.SetRating([boogie], Rating.Rejected);
        WaitFor(() => fake.Stars.Count == 2, "the star taken off");

        Assert.Equal(["star s1", "unstar s1"], fake.Stars);
        Assert.Empty(app.Database.GetServerHeartsOut(server.Server.Id));
    }

    [AvaloniaFact]
    public async Task A_heart_set_while_the_server_cant_be_reached_goes_once_it_answers()
    {
        var (app, fake, server) = await WithServer();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        servers.SaveChoices(server, [Friday]);
        Finish(servers.SyncAsync(server));
        Track boogie = app.Database.TrackForFile(PathOf(app, server, "s1"));
        fake.Down = true;

        app.ViewModel.SetRating([boogie], Rating.Loved);
        WaitFor(() => server.IsOffline, "the server marked away");
        Assert.Equal([("s1", true)], app.Database.GetServerHeartsOut(server.Server.Id));
        Assert.Empty(fake.Stars);

        fake.Down = false;
        Finish(servers.CheckReachableAsync());
        WaitFor(() => fake.Stars.Count == 1, "the star, once it answers");
        Assert.Equal(["star s1"], fake.Stars);
        Assert.Empty(app.Database.GetServerHeartsOut(server.Server.Id));
    }

    [AvaloniaFact]
    public async Task Hearts_arent_sent_to_a_server_left_out()
    {
        var (app, fake, server) = await WithServer();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        servers.SaveChoices(server, [Friday]);
        Finish(servers.SyncAsync(server));
        server.SendHearts = false;

        app.ViewModel.SetRating([app.Database.TrackForFile(PathOf(app, server, "s1"))], Rating.Loved);
        AppHarness.Settle(500);

        Assert.Empty(fake.Stars);
        Assert.Empty(app.Database.GetServerHeartsOut(server.Server.Id));
        Assert.Contains(server.Server.Id, app.Settings.ServerHeartsNotSent);
    }

    // ───────────── Keep downloaded songs ─────────────

    [AvaloniaFact]
    public async Task For_the_night_only_songs_downloaded_on_the_spot_go_at_the_next_start_but_not_a_lists_or_the_queues()
    {
        var (app, _, server) = await WithServer();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        servers.SaveChoices(server, [Friday]);
        Finish(servers.SyncAsync(server));
        Track mama = await OnTheSpot(app, "s4");   // not in Friday
        string mamaPath = mama.Path;
        Track also = app.Database.TrackForFile(PathOf(app, server, "s2"));
        Assert.True(File.Exists(mamaPath));

        servers.KeepDownloadsChoice = servers.KeepDownloadsChoices.Single(c => c.Value == KeepDownloads.NightOnly);
        servers.StartedUtc = DateTime.UtcNow.AddSeconds(1);   // GZJam started again since
        Finish(servers.TidyDownloadsAsync());

        Assert.False(File.Exists(mamaPath));
        Assert.Null(app.Database.GetServerSong(server.Server.Id, "s4"));
        Assert.DoesNotContain(app.Database.GetServerChoices(server.Server.Id), c => c.RemoteId == "s4");
        Assert.Empty(app.Database.GetRemovedServerSongs(server.Server.Id));   // not held back
        Assert.True(File.Exists(also.Path));   // Friday's

        // One in the queue stays.
        Track again = await OnTheSpot(app, "s4");
        app.ViewModel.AddToQueue([again]);
        servers.StartedUtc = DateTime.UtcNow.AddSeconds(1);
        Finish(servers.TidyDownloadsAsync());
        Assert.True(File.Exists(again.Path));
    }

    [AvaloniaFact]
    public async Task For_4_weeks_songs_no_list_needs_go_once_not_played_for_4_weeks()
    {
        var (app, _, server) = await WithServer();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        servers.SaveChoices(server, [Friday]);
        Finish(servers.SyncAsync(server));
        string boogie = PathOf(app, server, "s1");
        servers.SaveChoices(server, []);   // Friday no longer kept

        servers.KeepDownloadsChoice = servers.KeepDownloadsChoices.Single(c => c.Value == KeepDownloads.FourWeeks);
        Finish(servers.TidyDownloadsAsync());
        Assert.True(File.Exists(boogie));   // came just now

        servers.UtcNow = () => DateTime.UtcNow.AddDays(29);
        Finish(servers.TidyDownloadsAsync());
        Assert.False(File.Exists(boogie));
        Assert.Empty(app.Database.GetServerDownloads(server.Server.Id));
    }

    [AvaloniaFact]
    public async Task Until_I_remove_them_nothing_goes()
    {
        var (app, _, server) = await WithServer();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        Track mama = await OnTheSpot(app, "s4");
        servers.StartedUtc = DateTime.UtcNow.AddSeconds(1);
        servers.UtcNow = () => DateTime.UtcNow.AddDays(60);

        Finish(servers.TidyDownloadsAsync());

        Assert.Equal(KeepDownloads.UntilRemoved, app.Settings.ServerKeepDownloads);
        Assert.True(File.Exists(mama.Path));
    }

    // ───────────── Room for downloaded songs ─────────────

    [AvaloniaFact]
    public async Task A_sync_past_the_limit_makes_room_with_the_song_played_least_recently()
    {
        var (app, fake, server) = await WithServer();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        Track mama = await OnTheSpot(app, "s4");   // on the spot, before the night
        string mamaPath = mama.Path;
        long three = fake.Files["s1"].Length + fake.Files["s2"].Length + fake.Files["s3"].Length;
        app.Settings.ServerDownloadsLimitGb = (three + 10) / 1_000_000_000.0;
        servers.SaveChoices(server, [Friday]);

        Finish(servers.SyncAsync(server));

        Assert.StartsWith("Synced", server.SyncText);
        Assert.False(File.Exists(mamaPath));
        Assert.Equal(["s1", "s2", "s3"], app.Database.GetServerDownloads(server.Server.Id).Select(d => d.SongId).Order());
        Assert.Contains("To make room", app.ViewModel.Notice);
    }
}
