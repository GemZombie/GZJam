using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.Library;
using GZJam.Library.Servers;
using GZJam.Testing;

namespace GZJam.App.Tests;

/// <summary>The downloads folder moving (#290): the songs downloaded go with it, and all that's kept by their paths.</summary>
public sealed class ServerMoveAppTests
{
    private static async Task<(AppHarness App, ServerItemViewModel Server, FakeNavidrome Fake)> WithDownloads(Func<AppHarness, Task>? first = null)
    {
        var app = new AppHarness();
        if (first is not null)
            await first(app);
        var fake = new FakeNavidrome();
        foreach (string id in (string[])["s1", "s2", "s3", "s4"])
        {
            string mp3 = Path.Combine(app.WorkDirectory, $"{id}.mp3");
            TestMp3.Write(mp3, new TestMp3.Tags(Title: id));
            fake.Files[id] = File.ReadAllBytes(mp3);
        }
        app.Settings.ServerDownloadsFolder ??= Path.Combine(app.WorkDirectory, "GZJam from servers");
        var servers = app.ViewModel.Servers;
        servers.CheckServer = (_, _, _, _) => Task.FromResult(new ServerCheck(ServerCheckOutcome.Connected, "navidrome", "0.64.2", true, true, 4));
        servers.ClientFor = (server, password) => new SubsonicClient(new HttpClient(fake), server.Address, server.UserName, password) { RetryAfter = TimeSpan.Zero };
        Assert.True((await servers.SaveAsync(null, new ServerEdit("synology", "synology:4533", "gzjam", "sesame"))).Saved);
        var item = servers.Items[0];
        WaitFor(() => app.Database.GetServerCatalogCount(item.Server.Id) == 4, "the catalog");
        servers.SaveChoices(item, [new ServerChoice(ServerChoiceKind.Playlist, "pl1", "Friday")]);   // s2, s1, s3
        Finish(servers.SyncAsync(item));
        Assert.Equal(3, app.Database.GetServerDownloads(item.Server.Id).Count);
        WaitFor(() => app.Database.GetServerDownloads(item.Server.Id).All(d => app.Database.FindTrack(d.Path) is not null), "the songs in the library");
        app.ViewModel.ConfirmAsync = (_, _, _) => Task.FromResult(true);
        return (app, item, fake);
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

    private static string PathOf(AppHarness app, ServerItemViewModel server, string songId) =>
        app.Database.GetServerDownloads(server.Server.Id).Single(d => d.SongId == songId).Path;

    [AvaloniaFact]
    public async Task Changing_the_folder_moves_the_songs_with_their_hearts_playlists_and_queue()
    {
        var (app, server, fake) = await WithDownloads();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        string old = app.Settings.ServerDownloadsFolder!;
        string boogie = PathOf(app, server, "s1"), blues = PathOf(app, server, "s3");
        app.Database.SetRating([boogie], Rating.Loved);
        app.ViewModel.AddToQueue([app.Database.TrackForFile(blues)]);
        string bigger = Path.Combine(app.WorkDirectory, "Bigger drive", "Server songs");
        Assert.True(servers.CanChangeDownloadsFolder);

        Finish(servers.ChangeDownloadsFolderAsync(bigger));

        string movedBoogie = Path.Combine(bigger, Path.GetRelativePath(old, boogie));
        Assert.Equal(movedBoogie, PathOf(app, server, "s1"));
        Assert.True(File.Exists(movedBoogie));
        Assert.False(Directory.Exists(old));   // emptied, and gone
        Assert.Equal(bigger, servers.DownloadsFolder);
        Assert.Equal(Rating.Loved, app.Database.GetMarks(movedBoogie).Rating);
        PlaylistInfo friday = Assert.Single(app.Database.GetPlaylists(), p => p.Name == "Friday");
        Assert.All(app.Database.GetPlaylistEntries(friday.Id), e => Assert.StartsWith(bigger, e.Path));
        Assert.DoesNotContain(app.Database.GetRoots(), r => r.Path == old);   // out of the library
        Assert.Contains(app.Database.GetRoots(), r => r.Path == bigger);
        WaitFor(() => app.Database.GetServerDownloads(server.Server.Id).All(d => app.Database.FindTrack(d.Path) is not null), "the songs, at their new place");
        Assert.Null(app.Database.FindTrack(boogie));
        Assert.Equal(Rating.Loved, app.Database.FindTrack(movedBoogie)!.Rating);
        Assert.StartsWith(bigger, Assert.Single(app.ViewModel.Queue.Items).Track.Path);   // the queue followed
        Assert.Null(servers.SyncStatus);

        int downloads = fake.Downloads;
        Finish(servers.SyncAsync(server));
        Assert.Equal(downloads, fake.Downloads);   // nothing downloaded again
    }

    [AvaloniaFact]
    public async Task A_song_on_a_deck_or_a_folder_inside_the_old_one_leaves_everything_where_it_is()
    {
        var (app, server, _) = await WithDownloads();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        string old = app.Settings.ServerDownloadsFolder!;
        string boogie = PathOf(app, server, "s1");

        Finish(servers.ChangeDownloadsFolderAsync(Path.Combine(old, "Inside")));
        Assert.Equal(old, servers.DownloadsFolder);

        Assert.True(await app.ViewModel.LoadToDeckAsync(app.ViewModel.Decks[0], app.Database.TrackForFile(boogie)));
        Finish(servers.ChangeDownloadsFolderAsync(Path.Combine(app.WorkDirectory, "Elsewhere")));
        Assert.Equal(old, servers.DownloadsFolder);
        Assert.True(File.Exists(boogie));
        Assert.Equal(boogie, PathOf(app, server, "s1"));
    }

    [AvaloniaFact]
    public async Task From_inside_the_DJs_music_folder_the_songs_leave_it_and_the_folder_stays()
    {
        string music = "";
        var (app, server, _) = await WithDownloads(async app =>
        {
            music = Path.Combine(app.WorkDirectory, "Music");
            Directory.CreateDirectory(music);
            TestMp3.Write(Path.Combine(music, "mine.mp3"), new TestMp3.Tags(Title: "Mine"));
            await app.ViewModel.Library.AddRootAsync(music);
            app.Settings.ServerDownloadsFolder = Path.Combine(music, "GZJam from servers");
        });
        using var _ = app;
        var servers = app.ViewModel.Servers;
        Assert.Single(app.Database.GetRoots());   // inside Music: not a music folder of its own
        string bigger = Path.Combine(app.WorkDirectory, "Bigger drive");

        Finish(servers.ChangeDownloadsFolderAsync(bigger));

        Assert.StartsWith(bigger, PathOf(app, server, "s1"));
        Assert.Contains(app.Database.GetRoots(), r => r.Path == music);   // the DJ's own stays
        Assert.Contains(app.Database.GetRoots(), r => r.Path == bigger);
        Assert.NotNull(app.Database.FindTrack(Path.Combine(music, "mine.mp3")));
        Assert.False(Directory.Exists(Path.Combine(music, "GZJam from servers")));
        WaitFor(() => app.Database.GetServerDownloads(server.Server.Id).All(d => app.Database.FindTrack(d.Path) is not null), "the songs, at their new place");
    }

    [AvaloniaFact]
    public async Task Saying_no_moves_nothing()
    {
        var (app, server, _) = await WithDownloads();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        string old = app.Settings.ServerDownloadsFolder!, boogie = PathOf(app, server, "s1");
        app.ViewModel.ConfirmAsync = (_, _, _) => Task.FromResult(false);

        Finish(servers.ChangeDownloadsFolderAsync(Path.Combine(app.WorkDirectory, "Elsewhere")));

        Assert.Equal(old, servers.DownloadsFolder);
        Assert.True(File.Exists(boogie));
    }
}
