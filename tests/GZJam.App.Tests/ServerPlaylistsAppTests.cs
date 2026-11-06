using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.Library;
using GZJam.Library.Servers;
using GZJam.Testing;

namespace GZJam.App.Tests;

/// <summary>A server's playlists in the library (#290): under "Playlists" in its tree, opened in the song list, kept on the computer from there.</summary>
public sealed class ServerPlaylistsAppTests
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
        fake.Playlists["pl2"] = ["s4"];
        fake.PlaylistNames["pl2"] = "After midnight";
        app.Settings.ServerDownloadsFolder = Path.Combine(app.WorkDirectory, "GZJam from servers");
        var servers = app.ViewModel.Servers;
        servers.CheckServer = (_, _, _, _) => Task.FromResult(new ServerCheck(ServerCheckOutcome.Connected, "navidrome", "0.64.2", true, true, 4));
        servers.ClientFor = (server, password) => new SubsonicClient(new HttpClient(fake), server.Address, server.UserName, password) { RetryAfter = TimeSpan.Zero };
        Assert.True((await servers.SaveAsync(null, new ServerEdit("synology", "synology:4533", "gzjam", "sesame"))).Saved);
        var item = Assert.Single(servers.Items);
        WaitFor(() => PlaylistsNode(app) is not null, "the server's playlists in the tree");
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

    private static FolderNodeViewModel? PlaylistsNode(AppHarness app) =>
        app.ViewModel.Library.Roots.FirstOrDefault(r => r.IsServerItself)?.Children.FirstOrDefault(c => c.Node.IsServerPlaylists);

    private static FolderNodeViewModel Playlist(AppHarness app, string name) =>
        Assert.Single(PlaylistsNode(app)!.Children, c => c.Name == name);

    private static void Open(AppHarness app, string name, Func<IReadOnlyList<Track>, bool> ready)
    {
        app.ViewModel.Library.SelectedFolder = Playlist(app, name);
        WaitFor(() => ready(app.ViewModel.Library.Results), $"{name}'s songs");
    }

    [AvaloniaFact]
    public async Task The_servers_playlists_are_under_Playlists_first_and_one_opens_in_its_order()
    {
        var (app, _, _) = await WithServer();
        using var _ = app;
        var server = Assert.Single(app.ViewModel.Library.Roots, r => r.IsServerItself);
        Assert.Same(PlaylistsNode(app), server.Children[0]);   // before the server's folders
        Assert.Equal(["After midnight", "Friday"], PlaylistsNode(app)!.Children.Select(c => c.Name));
        FolderNodeViewModel friday = Playlist(app, "Friday");
        Assert.Equal("3", friday.CountText);
        Assert.True(friday.IsServerPlaylist);
        Assert.True(friday.CanKeep);
        Assert.False(friday.IsKept);

        Open(app, "Friday", r => r.Count == 3);

        var results = app.ViewModel.Library.Results;
        Assert.Equal(["Air Mail Special", "Basie Boogie", "Lucky Rock Blues"], results.Select(t => t.Title));
        Assert.All(results, t => Assert.True(t.IsOnServerOnly));
        Assert.True(app.ViewModel.Library.IsServerShowing);
    }

    [AvaloniaFact]
    public async Task Keep_on_this_laptop_downloads_it_into_a_playlist_of_yours()
    {
        var (app, _, server) = await WithServer();
        using var _ = app;

        Finish(app.ViewModel.Servers.KeepPlaylistAsync(server.Server.Id, "pl1"));

        PlaylistInfo friday = Assert.Single(app.Database.GetPlaylists(), p => p.Name == "Friday");
        Assert.Equal(3, app.Database.GetPlaylistEntries(friday.Id).Count);
        Assert.Equal(friday.Id, app.ViewModel.Servers.KeptAs(server.Server.Id, "pl1")?.Id);
        Assert.True(Playlist(app, "Friday").IsKept);
        Assert.True(Playlist(app, "After midnight").CanKeep);

        Open(app, "Friday", r => r.Count == 3 && r.All(t => !t.IsOnServerOnly));
        Assert.Contains("Kept on this computer", app.ViewModel.Library.ServerNote);
        Assert.Equal(3, app.ViewModel.Servers.DownloadsInFolder(server.Server.Id, FolderNode.ServerPlaylistPath("pl1")).Count);   // its Remove from this computer
        Assert.Empty(app.ViewModel.Servers.DownloadsInFolder(server.Server.Id, FolderNode.ServerPlaylistPath("pl2")));
    }

    [AvaloniaFact]
    public async Task Opened_it_shows_the_servers_latest_and_offline_as_last_read()
    {
        var (app, fake, server) = await WithServer();
        using var _ = app;
        fake.Playlists["pl1"] = ["s3", "s4"];   // changed on the server since

        Open(app, "Friday", r => r.Count == 2);
        Assert.Equal(["Lucky Rock Blues", "Li'l Darlin'"], app.ViewModel.Library.Results.Select(t => t.Title));

        app.ViewModel.Library.SelectedFolder = null;
        fake.Down = true;
        app.ViewModel.Servers.MarkOffline(server);
        Open(app, "Friday", r => r.Count == 2);   // as last read, from this computer
        Assert.Equal(["Lucky Rock Blues", "Li'l Darlin'"], app.ViewModel.Library.Results.Select(t => t.Title));
    }

    [AvaloniaFact]
    public async Task A_search_in_a_playlist_finds_its_songs_only()
    {
        var (app, _, _) = await WithServer();
        using var _ = app;
        var library = app.ViewModel.Library;
        Open(app, "Friday", r => r.Count == 3);

        library.SearchText = "basie";
        WaitFor(() => library.Results.Count == 1, "Friday's Basie song, not Li'l Darlin'");
        Assert.Equal("Basie Boogie", library.Results[0].Title);
    }
}
