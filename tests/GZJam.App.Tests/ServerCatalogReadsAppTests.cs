using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.Library.Servers;
using GZJam.Testing;

namespace GZJam.App.Tests;

/// <summary>
/// GZJam's start reads a server's song list again only once the server has read its music since
/// (Navidrome's lastScan): read again unchanged, 18,000 songs took 90 seconds over the internet,
/// for nothing new. A server that doesn't say when is read as before, every 12 hours.
/// </summary>
public sealed class ServerCatalogReadsAppTests
{
    private static async Task<(AppHarness App, FakeNavidrome Fake, ServerItemViewModel Server)> WithServer(string? lastScan)
    {
        var app = new AppHarness();
        var fake = new FakeNavidrome { LastScan = lastScan };
        var servers = app.ViewModel.Servers;
        servers.CheckServer = (_, _, _, _) => Task.FromResult(new ServerCheck(ServerCheckOutcome.Connected, "navidrome", "0.64.2", true, true, 4));
        servers.ClientFor = (server, password) => new SubsonicClient(new HttpClient(fake), server.Address, server.UserName, password) { RetryAfter = TimeSpan.Zero };
        Assert.True((await servers.SaveAsync(null, new ServerEdit("synology", "synology:4533", "gzjam", "sesame"))).Saved);
        var item = Assert.Single(servers.Items);
        WaitFor(() => app.Database.GetServerCatalogCount(item.Server.Id) > 0 && !servers.IsReadingAnyCatalog, "the catalog");
        servers.CatalogAge = TimeSpan.Zero;   // as if GZJam started the next day
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
        AppHarness.Settle(100);
    }

    private static void WaitFor(Func<bool> ready, string what)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (!ready() && DateTime.UtcNow < until)
            AppHarness.Settle(30);
        Assert.True(ready(), what);
    }

    private static int Reads(FakeNavidrome fake)
    {
        lock (fake.Calls)
            return fake.Calls.GetValueOrDefault("search3");
    }

    private static int PlaylistReads(FakeNavidrome fake)
    {
        lock (fake.Calls)
            return fake.Calls.GetValueOrDefault("getPlaylists");
    }

    [AvaloniaFact]
    public async Task A_server_that_hasnt_read_its_music_since_isnt_read_again_but_its_playlists_are()
    {
        var (app, fake, _) = await WithServer("2026-11-07T08:00:00Z");
        using var _ = app;
        int reads = Reads(fake), playlists = PlaylistReads(fake);

        Finish(app.ViewModel.Servers.ReadCatalogsAsync());

        Assert.Equal(reads, Reads(fake));
        Assert.True(PlaylistReads(fake) > playlists);   // they change without it reading its music
    }

    [AvaloniaFact]
    public async Task A_server_that_has_read_its_music_since_is_read_again()
    {
        var (app, fake, server) = await WithServer("2026-11-07T08:00:00Z");
        using var _ = app;
        int reads = Reads(fake);
        fake.LastScan = "2026-11-07T09:00:00Z";

        Finish(app.ViewModel.Servers.ReadCatalogsAsync());

        Assert.True(Reads(fake) > reads);
        Assert.Equal("2026-11-07T09:00:00Z|4", app.Database.GetServerCatalogScan(server.Server.Id));
    }

    [AvaloniaFact]
    public async Task A_server_that_doesnt_say_when_it_read_its_music_is_read_again_as_before()
    {
        var (app, fake, _) = await WithServer(lastScan: null);
        using var _ = app;
        int reads = Reads(fake);

        Finish(app.ViewModel.Servers.ReadCatalogsAsync());

        Assert.True(Reads(fake) > reads);
    }

    /// <summary>
    /// Right-click › <i>Read its song list again</i> asks first too (#395): a server that hasn't read
    /// its music since has only its playlists read, and the DJ is told.
    /// </summary>
    [AvaloniaFact]
    public async Task Read_its_song_list_again_reads_only_the_playlists_of_a_server_that_hasnt_read_its_music_since()
    {
        var (app, fake, server) = await WithServer("2026-11-07T08:00:00Z");
        using var _ = app;
        int reads = Reads(fake), playlists = PlaylistReads(fake);

        Finish(app.ViewModel.Servers.ReadCatalogAgainAsync(server.Server.Id));

        Assert.Equal(reads, Reads(fake));
        Assert.True(PlaylistReads(fake) > playlists);
        Assert.Equal("synology hasn't read its music since: its playlists were read again.", app.ViewModel.Notice);
    }

    [AvaloniaFact]
    public async Task Read_its_song_list_again_reads_it_all_once_the_server_has_read_its_music_since()
    {
        var (app, fake, server) = await WithServer("2026-11-07T08:00:00Z");
        using var _ = app;
        int reads = Reads(fake);
        fake.LastScan = "2026-11-07T09:00:00Z";

        Finish(app.ViewModel.Servers.ReadCatalogAgainAsync(server.Server.Id));

        Assert.True(Reads(fake) > reads);
        Assert.Equal("2026-11-07T09:00:00Z|4", app.Database.GetServerCatalogScan(server.Server.Id));
    }

    [AvaloniaFact]
    public async Task Read_its_song_list_again_reads_it_all_from_a_server_that_doesnt_say()
    {
        var (app, fake, server) = await WithServer(lastScan: null);
        using var _ = app;
        int reads = Reads(fake);

        Finish(app.ViewModel.Servers.ReadCatalogAgainAsync(server.Server.Id));

        Assert.True(Reads(fake) > reads);
        Assert.DoesNotContain("hasn't read its music", app.ViewModel.Notice ?? "");
    }
}
