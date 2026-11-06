using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.Library;
using GZJam.Library.Servers;
using GZJam.Testing;

namespace GZJam.App.Tests;

/// <summary>A server that can't be reached, and the queue's songs still on a server (#290).</summary>
public sealed class ServerOfflineAppTests
{
    private static async Task<(AppHarness App, FakeNavidrome Server)> WithServer()
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
        servers.PreviewFolder = Path.Combine(app.WorkDirectory, "previews");
        servers.CheckServer = (_, _, _, _) => Task.FromResult(fake.Down
            ? new ServerCheck(ServerCheckOutcome.Unreachable)
            : new ServerCheck(ServerCheckOutcome.Connected, "navidrome", "0.64.2", true, true, 4));
        servers.ClientFor = (server, password) => new SubsonicClient(new HttpClient(fake), server.Address, server.UserName, password) { RetryAfter = TimeSpan.Zero };
        Assert.True((await servers.SaveAsync(null, new ServerEdit("synology", "synology:4533", "gzjam", "sesame"))).Saved);
        WaitFor(() => app.Database.GetServerCatalogCount(servers.Items[0].Server.Id) == 4, "the catalog");
        AppHarness.Settle(100);
        return (app, fake);
    }

    private static void WaitFor(Func<bool> ready, string what)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (!ready() && DateTime.UtcNow < until)
            AppHarness.Settle(30);
        Assert.True(ready(), what);
    }

    private static Track Remote(AppHarness app, string title)
    {
        var server = app.ViewModel.Servers.Items[0];
        ServerSong song = app.Database.SearchServerCatalog(server.Server.Id, "", title).Songs.First();
        return new ServerTrack(server.Server.Id, server.Name, song) { Unreachable = server.IsOffline }.ToTrack();
    }

    [AvaloniaFact]
    public async Task A_queued_song_from_the_server_waits_in_its_place_then_becomes_itself()
    {
        var (app, _) = await WithServer();
        using var _ = app;
        Track here = app.ToneTrack("Here first");
        app.ViewModel.AddToQueue([here]);

        app.ViewModel.AddToQueue([Remote(app, "Basie Boogie")], 0);

        QueueItem waiting = app.ViewModel.Queue.Items[0];
        Assert.True(waiting.IsWaiting);   // in the queue at once, in its place
        WaitFor(() => !app.ViewModel.Queue.Items[0].IsWaiting, "the song, here");
        Track arrived = app.ViewModel.Queue.Items[0].Track;
        Assert.Equal("03 - Basie Boogie.mp3", Path.GetFileName(arrived.Path));
        Assert.True(File.Exists(arrived.Path));
        Assert.Same(here, app.ViewModel.Queue.Items[1].Track);
    }

    [AvaloniaFact]
    public async Task While_the_server_cant_be_reached_a_queued_song_waits_and_comes_when_it_answers()
    {
        var (app, fake) = await WithServer();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        fake.Down = true;
        await servers.CheckReachableAsync();
        Assert.True(servers.Items[0].IsOffline);
        Assert.True(Assert.Single(app.ViewModel.Library.Roots, r => r.IsServerItself).IsOffline);

        app.ViewModel.AddToQueue([Remote(app, "Lucky Rock Blues")]);
        QueueItem waiting = Assert.Single(app.ViewModel.Queue.Items);
        Assert.Equal("Waiting for synology", waiting.ServerState);
        Assert.False(waiting.ServerProblem);

        fake.Down = false;
        await servers.CheckReachableAsync();
        WaitFor(() => !app.ViewModel.Queue.Items[0].IsWaiting, "the song, here");
        Assert.False(servers.Items[0].IsOffline);
    }

    [AvaloniaFact]
    public async Task Auto_play_goes_past_a_song_still_downloading_and_it_stays_at_the_top()
    {
        var (app, fake) = await WithServer();
        using var _ = app;
        fake.Down = true;
        await app.ViewModel.Servers.CheckReachableAsync();
        Track here = app.ToneTrack("Ready to play");
        app.ViewModel.AddToQueue([Remote(app, "Basie Boogie"), here]);
        IReadOnlyList<QueueItem>? passed = null;
        app.ViewModel.Queue.PassedOver += p => passed = p;

        Track? next = app.ViewModel.Queue.TakeNext();

        Assert.Same(here, next);
        Assert.Equal("Basie Boogie", Assert.Single(passed!).Title);
        Assert.True(Assert.Single(app.ViewModel.Queue.Items).IsWaiting);
    }

    [AvaloniaFact]
    public async Task A_download_that_fails_says_so_and_Try_again_brings_it()
    {
        var (app, fake) = await WithServer();
        using var _ = app;
        fake.Truncate = "s3";

        app.ViewModel.AddToQueue([Remote(app, "Lucky Rock Blues")]);
        WaitFor(() => app.ViewModel.Queue.Items[0].CanRetry, "the failure");
        QueueItem failed = app.ViewModel.Queue.Items[0];
        Assert.StartsWith("Couldn't download: It arrived incomplete", failed.ServerState);
        Assert.True(failed.ServerProblem);
        Assert.True(failed.IsWaiting);   // never taken out of the queue by itself

        fake.Truncate = null;
        app.ViewModel.Servers.FetchForQueue(failed);
        WaitFor(() => !app.ViewModel.Queue.Items[0].IsWaiting, "the song, here");
    }

    [AvaloniaFact]
    public async Task Offline_a_double_click_says_so_at_once_and_the_rows_show_it()
    {
        var (app, fake) = await WithServer();
        using var _ = app;
        var library = app.ViewModel.Library;
        library.SelectedFolder = Assert.Single(library.Roots, r => r.IsServerItself);
        WaitFor(() => library.Results.Any(t => t.IsOnServerOnly), "the server's songs");
        Track boogie = library.Results.First(t => t.Title == "Basie Boogie");

        fake.Down = true;
        await app.ViewModel.Servers.CheckReachableAsync();
        Assert.True(library.OnlyHere);   // on by itself: what can be played tonight
        WaitFor(() => library.Results.Count == 0, "only what's here (nothing yet)");

        var clock = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(await app.ViewModel.PlaySongAsync(boogie));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1));
        Assert.StartsWith("synology can't be reached right now", app.ViewModel.Notice);

        library.OnlyHere = false;
        WaitFor(() => library.Results.Count == 4, "every song, marked");
        Assert.All(library.Results, t => Assert.True(t.IsOnUnreachableServer));

        fake.Down = false;
        await app.ViewModel.Servers.CheckReachableAsync();
        WaitFor(() => library.Results.All(t => t.IsOnReachableServer), "the plain cloud again");
    }

    [AvaloniaFact]
    public async Task A_song_left_waiting_in_the_queue_comes_back_from_its_server_after_a_restart()
    {
        var (app, _) = await WithServer();
        using var _ = app;
        Track remote = Remote(app, "Air Mail Special");

        Track? again = app.ViewModel.Servers.TrackFor(remote.Path);

        Assert.NotNull(again);
        Assert.True(again.IsOnServerOnly);
        Assert.Equal("Air Mail Special", again.Title);
        Assert.Null(app.ViewModel.Servers.TrackFor(ServerTrack.PathPrefix + "999/s1"));
    }

    [AvaloniaFact]
    public async Task A_deck_asked_for_a_song_still_downloading_takes_it_when_its_here()
    {
        var (app, fake) = await WithServer();
        using var _ = app;
        fake.DownloadDelay = TimeSpan.FromMilliseconds(600);
        app.ViewModel.AddToQueue([Remote(app, "Basie Boogie")]);
        QueueItem waiting = Assert.Single(app.ViewModel.Queue.Items);
        DeckViewModel deck = app.ViewModel.Decks[1];

        Assert.False(await app.ViewModel.LoadToDeckAsync(deck, waiting.Track));
        Assert.Contains("as soon as it's here", app.ViewModel.Notice);

        WaitFor(() => deck.Track is not null, "the deck, loaded when the song came");
        Assert.Equal("03 - Basie Boogie.mp3", Path.GetFileName(deck.Track!.Path));
        Assert.Empty(app.ViewModel.Queue.Items);   // loaded from the queue: it left it
    }

    [AvaloniaFact]
    public async Task When_the_queue_ends_counts_songs_still_downloading_and_says_so()
    {
        var (app, fake) = await WithServer();
        using var _ = app;
        var vm = app.ViewModel;
        fake.Down = true;
        await vm.Servers.CheckReachableAsync();
        Assert.True(AppHarness.Await(vm.LoadToDeckAsync(vm.Decks[0], app.ToneTrack("Playing", seconds: 30))));
        vm.AddToQueue([Remote(app, "Lucky Rock Blues")]);
        vm.Decks[0].TogglePlayCommand.Execute(null);
        app.Play(0.1);

        AppHarness.WaitFor(() =>
        {
            vm.UpdateQueueEnd();
            return vm.QueueEndText != "—";
        });
        Assert.EndsWith(" ↓", vm.QueueEndText);
        Assert.EndsWith("It counts 1 song still downloading.", vm.QueueEndTip);
    }

    [AvaloniaFact]
    public async Task The_server_shows_its_sync_in_the_tree()
    {
        var (app, _) = await WithServer();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        ServerItemViewModel server = servers.Items[0];
        servers.SaveChoices(server, [new ServerChoice(ServerChoiceKind.Playlist, "pl1", "Friday")]);
        FolderNodeViewModel node = Assert.Single(app.ViewModel.Library.Roots, r => r.IsServerItself);
        Assert.True(node.CanSync);

        Task sync = servers.SyncAsync(server);
        Assert.True(node.IsSyncing);
        Assert.Equal("↻", node.CountText);
        Assert.False(node.CanSync);
        WaitFor(() => sync.IsCompleted, "the sync");
        FolderNodeViewModel after = Assert.Single(app.ViewModel.Library.Roots, r => r.IsServerItself);
        Assert.False(after.IsSyncing);
        Assert.Equal("4", after.CountText);
    }
}
