using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.Library;
using GZJam.Library.Servers;
using GZJam.Testing;

namespace GZJam.App.Tests;

/// <summary>
/// Keeping a whole server in the library, in the background (#285): every song comes, a song at a
/// time; it can be paused and resumed, picks up after a restart, and waits while the room hears music.
/// </summary>
public sealed class ServerKeepWholeAppTests
{
    private static readonly string[] Songs = ["s1", "s2", "s3", "s4"];

    private static async Task<(AppHarness App, FakeNavidrome Fake, ServerItemViewModel Server)> WithServer(bool sync = true)
    {
        var app = new AppHarness();
        var fake = new FakeNavidrome();
        foreach (string id in Songs)
        {
            string mp3 = Path.Combine(app.WorkDirectory, $"{id}.mp3");
            TestMp3.Write(mp3, new TestMp3.Tags(Title: id));
            fake.Files[id] = File.ReadAllBytes(mp3);
        }
        app.Settings.ServerDownloadsFolder = Path.Combine(app.WorkDirectory, "GZJam from servers");
        app.Settings.ServerKeepFolder = Path.Combine(app.WorkDirectory, "My music");
        var servers = app.ViewModel.Servers;
        servers.RoomCheckEvery = TimeSpan.FromMilliseconds(50);
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

    private static void WaitFor(Func<bool> ready, string what, int seconds = 15)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (!ready() && DateTime.UtcNow < until)
            AppHarness.Settle(30);
        Assert.True(ready(), what);
    }

    private static int KeptCount(AppHarness app, ServerItemViewModel server) =>
        app.Database.GetServerSongs(server.Server.Id).Count(s => s.OwnCopy && File.Exists(s.Path));

    [AvaloniaFact]
    public async Task Every_song_comes_downloaded_ones_moving_and_the_rest_from_the_server()
    {
        var (app, fake, server) = await WithServer();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        string? asked = null;
        app.ViewModel.ConfirmAsync = (_, body, _) =>
        {
            asked = body;
            return Task.FromResult(true);
        };

        Finish(servers.KeepWholeServerAsync(server.Server.Id));

        Assert.Contains("4 songs into", asked);
        Assert.Contains("3 songs downloaded already move there first", asked);
        WaitFor(() => KeptCount(app, server) == 4, "every song kept");
        WaitFor(() => !servers.IsKeepWholeRunning(server.Server.Id), "the pass over");
        Assert.Equal((0, 0L), app.Database.GetDownloadedTotals());   // nothing left as a download
        Assert.Equal(4, fake.Downloads);   // three for the sync, then only Li'l Darlin'
        Assert.True(servers.IsKeepingWhole(server.Server.Id));   // songs added later come too
        Assert.Contains(server.Server.Id, app.Settings.ServerKeepWhole);
        Assert.True(app.ViewModel.Library.Roots.Single(r => r.IsServerItself).IsKeepingWhole);
    }

    [AvaloniaFact]
    public async Task Paused_it_waits_even_after_a_restart_and_resumed_it_finishes()
    {
        var (app, fake, server) = await WithServer(sync: false);
        using var _ = app;
        var servers = app.ViewModel.Servers;
        fake.Hold = new TaskCompletionSource();

        Finish(servers.KeepWholeServerAsync(server.Server.Id));
        WaitFor(() => fake.Started.Count == 1, "the first song coming");
        servers.PauseKeepWhole(server.Server.Id);
        WaitFor(() => !servers.IsKeepWholeRunning(server.Server.Id), "stopped");
        Assert.Equal(1, fake.Canceled);
        Assert.Equal(0, KeptCount(app, server));
        Assert.Contains(server.Server.Id, app.Settings.ServerKeepWholePaused);
        Assert.True(app.ViewModel.Library.Roots.Single(r => r.IsServerItself).CanResumeKeepWhole);
        fake.Hold.SetResult();
        fake.Hold = null;

        servers.ResumeKeepingWhole();   // as when GZJam starts again
        AppHarness.Settle(300);
        Assert.False(servers.IsKeepWholeRunning(server.Server.Id));
        Assert.Equal(0, KeptCount(app, server));

        servers.ResumeKeepWhole(server.Server.Id);
        WaitFor(() => KeptCount(app, server) == 4, "every song kept");
        Assert.DoesNotContain(server.Server.Id, app.Settings.ServerKeepWholePaused);
    }

    [AvaloniaFact]
    public async Task It_picks_up_where_it_left_off_when_GZJam_starts_again()
    {
        var (app, _, server) = await WithServer(sync: false);
        using var _ = app;
        var servers = app.ViewModel.Servers;
        app.Settings.ServerKeepWhole.Add(server.Server.Id);   // kept whole before GZJam closed
        app.Settings.ServerKeepWhole.Add(999);                // a server since removed

        servers.ResumeKeepingWhole();

        WaitFor(() => KeptCount(app, server) == 4, "every song kept");
        Assert.DoesNotContain(999L, app.Settings.ServerKeepWhole);
    }

    [AvaloniaFact]
    public async Task It_waits_while_the_room_hears_music_unless_allowed()
    {
        var (app, _, server) = await WithServer(sync: false);
        using var _ = app;
        var servers = app.ViewModel.Servers;
        var deck = app.ViewModel.Deck1!;
        await app.ViewModel.LoadToDeckAsync(deck, app.ToneTrack("Jump Steady", seconds: 60));
        deck.TogglePlayCommand.Execute(null);
        app.Play(0.2);
        Assert.NotNull(app.ViewModel.WhatTheRoomIsHearing());

        Finish(servers.KeepWholeServerAsync(server.Server.Id));
        AppHarness.Settle(500);
        Assert.Equal(0, KeptCount(app, server));
        Assert.True(servers.IsKeepWholeRunning(server.Server.Id));
        Assert.Null(servers.KeepWholeStatus);   // the top bar stays quiet meanwhile

        servers.KeepWhilePlaying = true;
        WaitFor(() => KeptCount(app, server) == 4, "every song kept, music or not");
        Assert.True(app.Settings.ServerKeepWhilePlaying);
    }

    [AvaloniaFact]
    public async Task Stopping_it_keeps_the_songs_kept_so_far()
    {
        var (app, _, server) = await WithServer();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        Finish(servers.KeepWholeServerAsync(server.Server.Id));
        WaitFor(() => KeptCount(app, server) == 4, "every song kept");

        Finish(servers.StopKeepWholeAsync(server.Server.Id));

        Assert.False(servers.IsKeepingWhole(server.Server.Id));
        Assert.Empty(app.Settings.ServerKeepWhole);
        Assert.Equal(4, KeptCount(app, server));
        Assert.True(app.ViewModel.Library.Roots.Single(r => r.IsServerItself).CanKeepWhole);
    }
}
