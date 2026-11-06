using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.App.Views;
using GZJam.Library;
using GZJam.Library.Servers;
using GZJam.Testing;

namespace GZJam.App.Tests;

/// <summary>Freeing up space (#290): songs downloaded from a server removed from the laptop, never what's needed.</summary>
public sealed class ServerFreeSpaceAppTests
{
    private static async Task<(AppHarness App, ServerItemViewModel Server)> WithDownloads()
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
        var item = servers.Items[0];
        WaitFor(() => app.Database.GetServerCatalogCount(item.Server.Id) == 4, "the catalog");
        servers.SaveChoices(item, [new ServerChoice(ServerChoiceKind.Playlist, "pl1", "Friday")]);   // s2, s1, s3
        Finish(servers.SyncAsync(item));
        Assert.Equal(3, app.Database.GetServerDownloads(item.Server.Id).Count);
        app.ViewModel.ConfirmAsync = (_, _, _) => Task.FromResult(true);
        return (app, item);
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

    private static ServerDownload Download(AppHarness app, ServerItemViewModel server, string songId) =>
        app.Database.GetServerDownloads(server.Server.Id).Single(d => d.SongId == songId);

    [AvaloniaFact]
    public async Task Remove_from_this_laptop_deletes_the_file_and_a_sync_leaves_it_on_the_server()
    {
        var (app, server) = await WithDownloads();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        ServerDownload boogie = Download(app, server, "s1");
        Track mine = app.Database.TrackForFile(boogie.Path);

        Finish(servers.RemoveFromLaptopAsync(servers.DownloadsFor([mine])));

        Assert.False(File.Exists(boogie.Path));
        Assert.DoesNotContain(app.Database.GetServerDownloads(server.Server.Id), d => d.SongId == "s1");
        Finish(servers.SyncAsync(server));
        Assert.False(File.Exists(boogie.Path));   // removed by the DJ: not downloaded again
    }

    [AvaloniaFact]
    public async Task A_song_on_a_deck_or_in_the_queue_is_never_removed()
    {
        var (app, server) = await WithDownloads();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        ServerDownload boogie = Download(app, server, "s1"), blues = Download(app, server, "s3");
        app.ViewModel.AddToQueue([app.Database.TrackForFile(blues.Path)]);
        Assert.True(await app.ViewModel.LoadToDeckAsync(app.ViewModel.Decks[0], app.Database.TrackForFile(boogie.Path)));

        FreeUpOffer offer = servers.OfferToFreeUp(server.Server.Id);
        Assert.Equal(2, offer.Protected);
        Assert.Single(offer.Everything);   // only Air Mail Special

        Finish(servers.RemoveNowAsync(app.Database.GetServerDownloads(server.Server.Id)));
        Assert.True(File.Exists(boogie.Path));
        Assert.True(File.Exists(blues.Path));
        Assert.Equal(2, app.Database.GetServerDownloads(server.Server.Id).Count);
    }

    [AvaloniaFact]
    public async Task Free_up_space_offers_last_nights_on_the_spot_downloads_and_says_what_it_frees()
    {
        var (app, server) = await WithDownloads();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        app.Database.StartOrContinueSession(DateTime.UtcNow.AddHours(-1), TimeSpan.FromHours(1));   // the last night
        var library = app.ViewModel.Library;
        library.SelectedFolder = Assert.Single(library.Roots, r => r.IsServerItself);
        WaitFor(() => library.Results.Any(t => t.IsOnServerOnly && t.Title == "Li'l Darlin'"), "the server's songs");
        Assert.NotNull(await servers.BringHereAsync(library.Results.First(t => t.Title == "Li'l Darlin'")));   // fetched on the spot

        FreeUpOffer offer = servers.OfferToFreeUp(server.Server.Id);
        Assert.Equal("s4", Assert.Single(offer.LastNight).SongId);
        Assert.Equal(4, offer.Everything.Count);
        Assert.Empty(offer.NotPlayedLately);   // all came today

        var dialog = new FreeSpaceDialog(servers, offer, "synology");
        dialog.Show(app.Window);
        AppHarness.Settle(50);
        Assert.Equal("s4", Assert.Single(dialog.Chosen()).SongId);   // ticked to start with
        Assert.StartsWith("Removes 1 song", dialog.FindControl<Avalonia.Controls.TextBlock>("TotalText")!.Text);
        dialog.FindControl<Avalonia.Controls.CheckBox>("EverythingBox")!.IsChecked = true;
        Assert.Equal(4, dialog.Chosen().Count);
        dialog.Close();
    }

    [AvaloniaFact]
    public async Task A_folders_downloads_are_removed_and_downloading_its_album_again_brings_them_back()
    {
        var (app, server) = await WithDownloads();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        string boogie = Download(app, server, "s1").Path;

        var basie = servers.DownloadsInFolder(server.Server.Id, "Jazz & Swing/Count Basie");
        Assert.Equal("s1", Assert.Single(basie).SongId);
        Finish(servers.RemoveFromLaptopAsync(basie));
        Assert.False(File.Exists(boogie));
        Assert.False(Directory.Exists(Path.GetDirectoryName(boogie)));   // the empty folder went too

        var library = app.ViewModel.Library;
        library.SelectedFolder = Assert.Single(library.Roots, r => r.IsServerItself);
        WaitFor(() => library.Results.Any(t => t.IsOnServerOnly && t.Title == "Basie Boogie"), "Basie Boogie, on the server again");
        Finish(servers.DownloadAlbumsAsync([library.Results.First(t => t.Title == "Basie Boogie")]));
        Assert.True(File.Exists(boogie));   // asked for: back
    }
}
