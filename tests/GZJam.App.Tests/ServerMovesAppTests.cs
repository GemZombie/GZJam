using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.Library;
using GZJam.Library.Servers;
using GZJam.Testing;

namespace GZJam.App.Tests;

/// <summary>
/// Songs moved or renamed on the server (#286): when its song list is read again, the copy here
/// follows (moved to its new place, with its hearts and plays), rather than being downloaded
/// again, whether the server keeps the song's ID or (as Navidrome does) gives it a new one.
/// </summary>
public sealed class ServerMovesAppTests
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

    private static string Here(AppHarness app, params string[] parts) =>
        Path.Combine([app.Settings.ServerDownloadsFolder!, "synology", .. parts]);

    [AvaloniaTheory]
    [InlineData(null)]          // the server keeps its ID
    [InlineData("s1-moved")]    // Navidrome: a new ID, from the new path
    public async Task A_song_moved_on_the_server_moves_here_with_its_heart_rather_than_coming_again(string? newId)
    {
        var (app, fake, server) = await WithServer();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        servers.SaveChoices(server, [Friday]);
        Finish(servers.SyncAsync(server));
        string was = Here(app, "Jazz & Swing", "Count Basie", "Cute", "03 - Basie Boogie.mp3");
        Assert.True(File.Exists(was));
        app.ViewModel.SetRating([app.Database.TrackForFile(was)], Rating.Loved);
        int downloads = fake.Downloads;

        fake.Move("s1", "/music/Swing/Count Basie/Cute (1958)/03 - Basie Boogie.mp3", newId);
        Finish(servers.ReadCatalogAsync(server.Server.Id));

        string now = Here(app, "Swing", "Count Basie", "Cute (1958)", "03 - Basie Boogie.mp3");
        Assert.False(File.Exists(was));
        Assert.True(File.Exists(now));
        Assert.False(Directory.Exists(Path.GetDirectoryName(was)));   // the folder left empty goes
        var copy = Assert.Single(app.Database.GetServerSongs(server.Server.Id), s => s.Path == now);
        Assert.Equal(newId ?? "s1", copy.SongId);
        Assert.Equal(Rating.Loved, app.Database.TrackForFile(now).Rating);   // its heart came along

        Finish(servers.SyncAsync(server));
        Assert.Equal(downloads, fake.Downloads);   // nothing downloaded again
        Assert.Contains(now, app.Database.GetPlaylistEntries(app.Database.GetPlaylists().Single(p => p.Name == "Friday").Id).Select(e => e.Path));
    }

    [AvaloniaFact]
    public async Task A_song_gone_from_the_server_with_nothing_like_it_is_left_as_it_is()
    {
        var (app, fake, server) = await WithServer();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        servers.SaveChoices(server, [Friday]);
        Finish(servers.SyncAsync(server));
        string was = Here(app, "Jazz & Swing", "Count Basie", "Cute", "03 - Basie Boogie.mp3");

        fake.Move("s1", "/music/Elsewhere/Something Else.mp3", "s9");
        fake.Sizes["s9"] = 12345;   // another file altogether
        Finish(servers.ReadCatalogAsync(server.Server.Id));

        Assert.True(File.Exists(was));
        Assert.Equal("s1", Assert.Single(app.Database.GetServerSongs(server.Server.Id), s => s.Path == was).SongId);
    }

    [AvaloniaFact]
    public async Task A_song_on_a_deck_is_left_for_next_time()
    {
        var (app, fake, server) = await WithServer();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        servers.SaveChoices(server, [Friday]);
        Finish(servers.SyncAsync(server));
        string was = Here(app, "Jazz & Swing", "Count Basie", "Cute", "03 - Basie Boogie.mp3");
        Assert.True(await app.ViewModel.LoadToDeckAsync(app.ViewModel.Decks[0], app.Database.TrackForFile(was)));

        fake.Move("s1", "/music/Swing/Count Basie/Cute/03 - Basie Boogie.mp3");
        Finish(servers.ReadCatalogAsync(server.Server.Id));

        Assert.True(File.Exists(was));
        app.ViewModel.Decks[0].EjectCommand.Execute(null);
        Finish(servers.ReadCatalogAsync(server.Server.Id));
        Assert.False(File.Exists(was));
    }
}
