using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.Library;
using GZJam.Library.Servers;
using GZJam.Testing;

namespace GZJam.App.Tests;

/// <summary>
/// Songs downloaded on the spot (#290), from a look at another take on servers: two of the
/// queue's at a time, a deck's first, one download per song, stopped when its row leaves the
/// queue; marks never kept by a song's server address; a server that lists nothing keeps its
/// list; a kept playlist says it follows the server; https first for a name on the internet.
/// </summary>
public sealed class ServerDownloadQueueAppTests
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
        WaitFor(() => !servers.IsReadingAnyCatalog, "the first reading to finish");   // the songs are in before it's done, and a second reading waits for nothing
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

    /// <summary>The server's songs, as rows still on it, in the song list's order.</summary>
    private static List<Track> ServerRows(AppHarness app)
    {
        var library = app.ViewModel.Library;
        library.SelectedFolder = Assert.Single(library.Roots, r => r.IsServerItself);
        WaitFor(() => library.Results.Count(t => t.IsOnServerOnly) == 4, "the server's songs");
        return library.Results.Where(t => t.IsOnServerOnly).ToList();
    }

    private static string IdOf(Track track) => track.OnServer!.Song.Id;

    [AvaloniaFact]
    public async Task The_queues_songs_come_down_two_at_a_time_in_its_order()
    {
        var (app, fake, _) = await WithServer();
        using var _ = app;
        var rows = ServerRows(app);
        fake.Hold = new TaskCompletionSource();

        app.ViewModel.AddToQueue(rows);

        WaitFor(() => fake.Started.Count == 2, "two downloads under way");
        AppHarness.Settle(300);
        Assert.Equal(rows.Take(2).Select(IdOf).Order(), fake.Started.Order());   // the first two (either first), and no more
        Assert.Equal(2, app.ViewModel.Queue.Items.Count(i => i.ServerState == "Waiting its turn…"));

        fake.Hold.SetResult();
        WaitFor(() => app.ViewModel.Queue.Items.All(i => !i.IsWaiting), "every song here");
        Assert.Equal(2, fake.MostAtOnce);
        Assert.Equal(rows.Skip(2).Select(IdOf).Order(), fake.Started.Skip(2).Order());   // then the next two
    }

    [AvaloniaFact]
    public async Task A_deck_waiting_for_a_queued_song_goes_ahead_of_the_queue()
    {
        var (app, fake, server) = await WithServer();
        using var _ = app;
        var rows = ServerRows(app);
        fake.Hold = new TaskCompletionSource();
        app.ViewModel.AddToQueue(rows);
        WaitFor(() => fake.Started.Count == 2, "the queue's two");

        Assert.False(await app.ViewModel.LoadToDeckAsync(app.ViewModel.Decks[0], rows[3]));   // still downloading: it goes on when it's here

        WaitFor(() => fake.Started.Count == 3, "the deck's song, now");
        Assert.Equal(IdOf(rows[3]), fake.Started[2]);
        fake.Hold.SetResult();
        WaitFor(() => app.ViewModel.Decks[0].Track is { IsOnServerOnly: false }, "the song on the deck");
        Assert.Equal(app.Database.GetServerDownloads(server.Server.Id).Single(d => d.SongId == IdOf(rows[3])).Path,
            app.ViewModel.Decks[0].Track!.Path);
    }

    [AvaloniaFact]
    public async Task A_song_queued_and_asked_for_at_once_comes_down_once()
    {
        var (app, fake, server) = await WithServer();
        using var _ = app;
        Track song = ServerRows(app)[0];
        fake.Hold = new TaskCompletionSource();

        app.ViewModel.AddToQueue([song]);
        WaitFor(() => fake.Started.Count == 1, "its download");
        Task<Track?> asked = app.ViewModel.Servers.BringHereAsync(song);   // a double-click, say
        AppHarness.Settle(300);
        Assert.Single(fake.Started);

        fake.Hold.SetResult();
        Finish(asked);
        Assert.False(asked.Result!.IsOnServerOnly);
        WaitFor(() => app.ViewModel.Queue.Items.All(i => !i.IsWaiting), "the queue's row, here");
        Assert.Equal(1, fake.Downloads);
        Assert.Single(app.Database.GetServerDownloads(server.Server.Id));   // no "(2)" copy
    }

    [AvaloniaFact]
    public async Task A_row_taken_out_of_the_queue_stops_its_download()
    {
        var (app, fake, server) = await WithServer();
        using var _ = app;
        Track song = ServerRows(app)[0];
        fake.Hold = new TaskCompletionSource();
        app.ViewModel.AddToQueue([song]);
        WaitFor(() => fake.Started.Count == 1, "its download");

        app.ViewModel.Queue.Remove([app.ViewModel.Queue.Items[0]]);

        WaitFor(() => fake.Canceled == 1, "the download stopped");
        fake.Hold.SetResult();
        AppHarness.Settle(300);
        Assert.Empty(app.Database.GetServerDownloads(server.Server.Id));
    }

    [AvaloniaFact]
    public async Task A_row_still_downloading_takes_no_marks()
    {
        var (app, fake, _) = await WithServer();
        using var _ = app;
        Track song = ServerRows(app)[0];
        fake.Hold = new TaskCompletionSource();
        app.ViewModel.AddToQueue([song]);
        QueueItem waiting = app.ViewModel.Queue.Items[0];
        Assert.True(waiting.IsWaiting);

        app.ViewModel.SetRating([waiting.Track], Rating.Loved);
        app.ViewModel.SetLabel([waiting.Track], "Lindy", on: true);

        Assert.Equal(Rating.Unrated, app.Database.GetMarks(waiting.Track.Path).Rating);   // nothing kept by its server address
        fake.Hold.SetResult();
    }

    [AvaloniaFact]
    public async Task A_server_that_lists_no_songs_keeps_its_song_list()
    {
        var (app, fake, server) = await WithServer();
        using var _ = app;
        fake.Empty = true;

        Finish(app.ViewModel.Servers.ReadCatalogAsync(server.Server.Id));

        Assert.Equal(4, app.Database.GetServerCatalogCount(server.Server.Id));
        Assert.Contains("kept as it was", app.ViewModel.Notice);
    }

    [AvaloniaFact]
    public async Task A_kept_playlist_says_it_follows_the_server_and_can_be_made_your_own()
    {
        var (app, _, server) = await WithServer();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        WaitFor(() => app.Database.GetServerPlaylists(server.Server.Id).Count == 1, "the server's playlists");
        Finish(servers.KeepPlaylistAsync(server.Server.Id, "pl1"));
        PlaylistInfo friday = Assert.Single(app.Database.GetPlaylists(), p => p.Name == "Friday");
        Assert.Equal("synology", friday.FollowsServer);
        Assert.Contains("Kept from synology", friday.Tip);

        Track lilDarlin = (await servers.BringHereAsync(servers.TrackFor($"{ServerTrack.PathPrefix}{server.Server.Id}/s4")!))!;   // not in Friday
        app.ViewModel.AddToPlaylist(friday, [lilDarlin]);   // changed here
        Assert.Contains("follows synology", app.ViewModel.Notice);

        servers.StopFollowing(friday);
        Assert.Null(app.Database.GetPlaylist(friday.Id)!.FollowsServer);
        Assert.True(servers.KeptAs(server.Server.Id, "pl1") is null);
        Assert.Equal(4, app.Database.GetPlaylistEntries(friday.Id).Count);   // as it was, the change too
        Finish(servers.SyncAsync(server));
        Assert.Equal(4, app.Database.GetPlaylistEntries(friday.Id).Count);   // a sync leaves it be
    }

    [AvaloniaFact]
    public async Task A_server_folder_downloads_with_the_folders_in_it_and_is_kept()
    {
        var (app, _, server) = await WithServer();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        string? asked = null;
        app.ViewModel.ConfirmAsync = (heading, _, _) => { asked = heading; return Task.FromResult(true); };

        Finish(servers.DownloadFolderAsync(server.Server.Id, "Jazz & Swing", "Jazz & Swing"));

        Assert.StartsWith("Download 3 songs", asked);   // Basie's two and Ella's, below it
        Assert.Equal(["s1", "s2", "s4"], app.Database.GetServerDownloads(server.Server.Id).Select(d => d.SongId).Order());
        ServerChoice kept = Assert.Single(app.Database.GetServerChoices(server.Server.Id));
        Assert.Equal((ServerChoiceKind.Folder, "Jazz & Swing"), (kept.Kind, kept.RemoteId));
        Assert.True(server.CanSync);
        Assert.Contains("1 folder", server.KeptText);
    }

    [AvaloniaFact]
    public async Task A_name_on_the_internet_is_signed_in_to_over_https_when_it_answers()
    {
        using var app = new AppHarness();
        var servers = app.ViewModel.Servers;
        var asked = new List<string>();
        servers.CheckServer = (address, _, _, _) =>
        {
            asked.Add(address);
            return Task.FromResult(address.StartsWith("https://", StringComparison.Ordinal)
                ? new ServerCheck(ServerCheckOutcome.Connected, "navidrome", "0.64.2", true, true, 4)
                : new ServerCheck(ServerCheckOutcome.Unreachable));
        };
        Assert.True((await servers.SaveAsync(null, new ServerEdit("Ed's", "music.ed.example", "gzjam", "sesame"))).Saved);
        Assert.Equal("https://music.ed.example", servers.Items[0].Server.Address);
        Assert.Equal(["https://music.ed.example"], asked);

        asked.Clear();
        servers.CheckServer = (address, _, _, _) =>
        {
            asked.Add(address);
            return Task.FromResult(address.StartsWith("https://", StringComparison.Ordinal)
                ? new ServerCheck(ServerCheckOutcome.Unreachable)
                : new ServerCheck(ServerCheckOutcome.Connected, "navidrome", "0.64.2", true, true, 4));
        };
        Assert.True((await servers.SaveAsync(null, new ServerEdit("Tailnet", "synology.tail1234.ts.net:4533", "gzjam", "sesame"))).Saved);
        Assert.Equal("http://synology.tail1234.ts.net:4533", servers.Items.Single(i => i.Name == "Tailnet").Server.Address);
        Assert.Equal(["https://synology.tail1234.ts.net:4533", "http://synology.tail1234.ts.net:4533"], asked);
    }

    [Theory]
    [InlineData("music.ed.example", "http://music.ed.example", "https://music.ed.example")]
    [InlineData("synology:4533", "http://synology:4533", null)]
    [InlineData("192.168.1.20:4533", "http://192.168.1.20:4533", null)]
    [InlineData("http://music.ed.example", "http://music.ed.example", null)]
    public void Https_is_tried_first_only_for_a_name_on_the_internet_typed_without_one(string typed, string normalized, string? first) =>
        Assert.Equal(first, ServersViewModel.SecureFirst(typed, normalized));
}
