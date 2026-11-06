using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.Library;
using GZJam.Library.Servers;
using GZJam.Testing;

namespace GZJam.App.Tests;

/// <summary>
/// Stopping what comes down from a server (Danny: "I just told it to sync the wrong playlist, and
/// it's big"): the top bar's Stop, and Stop keeping on this computer on a kept server playlist.
/// </summary>
public sealed class ServerStopAppTests
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
        servers.CheckServer = (_, _, _, _) => Task.FromResult(new ServerCheck(ServerCheckOutcome.Connected, "navidrome", "0.64.2", true, true, 4));
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

    [AvaloniaFact]
    public async Task Stopping_keeping_a_playlist_removes_only_what_nothing_else_kept_needs()
    {
        var (app, _, server) = await WithServer();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        servers.SaveChoices(server, [Friday, new ServerChoice(ServerChoiceKind.Song, "s1", "Basie Boogie")]);
        Finish(servers.SyncAsync(server));
        var paths = app.Database.GetServerDownloads(server.Server.Id).ToDictionary(d => d.SongId, d => d.Path);
        Assert.Equal(3, paths.Count);
        var asked = new List<string>();
        app.ViewModel.ConfirmAsync = (heading, _, _) =>
        {
            asked.Add(heading);
            return Task.FromResult(true);
        };

        Finish(servers.StopKeepingPlaylistAsync(server.Server.Id, "pl1"));

        Assert.Equal(["Stop keeping Friday on this computer?", "Remove 2 songs from this computer?"], asked);
        Assert.Equal(ServerChoiceKind.Song, Assert.Single(app.Database.GetServerChoices(server.Server.Id)).Kind);
        Assert.True(File.Exists(paths["s1"]));   // kept as a song of its own
        Assert.False(File.Exists(paths["s2"]));
        Assert.False(File.Exists(paths["s3"]));
        var copy = Assert.Single(app.Database.GetPlaylists(), p => p.Name == "Friday");
        Assert.False(copy.IsFromServer);   // the DJ's own now
        Assert.False(servers.IsKept(server.Server.Id, "pl1"));
    }

    [AvaloniaFact]
    public async Task Stopping_keeping_it_while_it_syncs_stops_the_sync_and_makes_no_copy()
    {
        var (app, fake, server) = await WithServer();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        servers.SaveChoices(server, [Friday]);
        fake.Hold = new TaskCompletionSource();
        Task sync = servers.SyncAsync(server);
        WaitFor(() => fake.Started.Count == 1, "the first song coming");

        Finish(servers.StopKeepingPlaylistAsync(server.Server.Id, "pl1"));
        Finish(sync);
        fake.Hold.SetResult();

        Assert.False(server.IsSyncing);
        Assert.Empty(app.Database.GetServerChoices(server.Server.Id));
        Assert.DoesNotContain(app.Database.GetPlaylists(), p => p.Name == "Friday");
        Assert.Empty(app.Database.GetServerDownloads(server.Server.Id));
    }

    [AvaloniaFact]
    public async Task The_top_bars_stop_stops_a_sync_and_keeps_what_came()
    {
        var (app, fake, server) = await WithServer();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        servers.SaveChoices(server, [Friday]);
        Assert.False(app.ViewModel.CanStopBackground);
        fake.Hold = new TaskCompletionSource();
        Task sync = servers.SyncAsync(server);
        WaitFor(() => fake.Started.Count == 1, "the first song coming");
        WaitFor(() => app.ViewModel.CanStopBackground, "Stop in the top bar");

        servers.StopInBackground();
        Finish(sync);
        fake.Hold.SetResult();

        Assert.False(app.ViewModel.CanStopBackground);
        Assert.StartsWith("Stopped.", server.SyncText);
        Assert.True(servers.IsKept(server.Server.Id, "pl1"));   // still kept: SYNC goes on from there
    }
}
