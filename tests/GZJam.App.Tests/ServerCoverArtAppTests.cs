using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.Library;
using GZJam.Library.Servers;
using GZJam.Testing;

namespace GZJam.App.Tests;

/// <summary>
/// A server's album art (#290): beside downloaded songs as the cover.jpg the server keeps in the
/// album's folder (a song's download doesn't bring it), and small in the rows of songs not here.
/// </summary>
public sealed class ServerCoverArtAppTests
{
    private static async Task<(AppHarness App, FakeNavidrome Fake, ServerItemViewModel Server)> WithServer(bool sync = true)
    {
        var app = new AppHarness();
        var fake = new FakeNavidrome();
        foreach (string id in (string[])["s1", "s2", "s3", "s4"])
        {
            string mp3 = Path.Combine(app.WorkDirectory, $"{id}.mp3");
            TestMp3.Write(mp3, new TestMp3.Tags(Title: id));   // no picture of their own
            fake.Files[id] = File.ReadAllBytes(mp3);
        }
        app.Settings.ServerDownloadsFolder = Path.Combine(app.WorkDirectory, "GZJam from servers");
        var servers = app.ViewModel.Servers;
        servers.CheckServer = (_, _, _, _) => Task.FromResult(new ServerCheck(ServerCheckOutcome.Connected, "navidrome", "0.64.2", true, true, 4));
        servers.ClientFor = (server, password) => new SubsonicClient(new HttpClient(fake), server.Address, server.UserName, password) { RetryAfter = TimeSpan.Zero };
        Assert.True((await servers.SaveAsync(null, new ServerEdit("synology", "synology:4533", "gzjam", "sesame"))).Saved);
        var item = Assert.Single(servers.Items);
        WaitFor(() => app.Database.GetServerCatalogCount(item.Server.Id) == 4, "the catalog");
        if (sync)
        {
            servers.SaveChoices(item, [new ServerChoice(ServerChoiceKind.Playlist, "pl1", "Friday")]);   // s2, s1, s3
            Finish(servers.SyncAsync(item));
        }
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

    private static string FolderOf(AppHarness app, ServerItemViewModel server, string songId) =>
        Path.GetDirectoryName(app.Database.GetServerDownloads(server.Server.Id).Single(d => d.SongId == songId).Path)!;

    [AvaloniaFact]
    public async Task A_synced_album_gets_its_picture_beside_its_songs_once()
    {
        var (app, fake, server) = await WithServer();
        using var _ = app;

        foreach (string id in (string[])["s1", "s2", "s3"])
            Assert.True(File.Exists(Path.Combine(FolderOf(app, server, id), "cover.png")), id);
        Assert.Equal(3, fake.ArtAsked.Count(a => a.Size is null));   // one for each album, full size
        Assert.NotNull(AlbumArtFinder.Find(app.Database.GetServerDownloads(server.Server.Id)[0].Path));

        Finish(app.ViewModel.Servers.SyncAsync(server));
        Assert.Equal(3, fake.ArtAsked.Count(a => a.Size is null));   // there already: not asked again
    }

    [AvaloniaFact]
    public async Task An_albums_picture_goes_with_its_songs_when_removed_or_moved()
    {
        var (app, _, server) = await WithServer();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        string basie = FolderOf(app, server, "s1"), ella = FolderOf(app, server, "s2");

        Finish(servers.RemoveFromLaptopAsync(servers.DownloadsInFolder(server.Server.Id, "Jazz & Swing/Count Basie")));
        Assert.False(Directory.Exists(basie));   // its picture didn't keep it

        string old = app.Settings.ServerDownloadsFolder!, bigger = Path.Combine(app.WorkDirectory, "Bigger drive");
        Finish(servers.ChangeDownloadsFolderAsync(bigger));
        Assert.True(File.Exists(Path.Combine(FolderOf(app, server, "s2"), "cover.png")));
        Assert.StartsWith(bigger, FolderOf(app, server, "s2"));
        Assert.False(Directory.Exists(ella));
        Assert.False(Directory.Exists(old));
    }

    [AvaloniaFact]
    public async Task A_row_not_on_the_laptop_shows_its_albums_picture_fetched_once_for_the_album()
    {
        var (app, fake, server) = await WithServer(sync: false);
        using var _ = app;
        var art = app.ViewModel.Art;
        string boogie = $"{ServerTrack.PathPrefix}{server.Server.Id}/s1", lilDarlin = $"{ServerTrack.PathPrefix}{server.Server.Id}/s4";

        Assert.Null(art.Get(boogie));   // not yet: it's fetched
        WaitFor(() => art.Get(boogie) is not null, "Basie Boogie's picture");
        WaitFor(() => art.Get(lilDarlin) is not null, "Li'l Darlin's, the same album's");

        Assert.Equal([("al-al-cute", "128")], fake.ArtAsked);   // small, and once for the two
    }
}
