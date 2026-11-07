using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using GZJam.App.ViewModels;
using GZJam.Library;
using GZJam.Library.Servers;
using GZJam.Testing;

namespace GZJam.App.Tests;

/// <summary>
/// The Downloads window (#304, after Danny's own): every song coming down from a server, from a
/// sync, for the queue or a deck, or being kept, saying why and how far along; what failed and
/// why; and Stop (a song, or all), Retry failed and Clear finished.
/// </summary>
public sealed class DownloadsWindowAppTests
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

    private static DownloadItem Row(DownloadsViewModel downloads, string id) => Assert.Single(downloads.Items, i => i.Song.Id == id);

    [AvaloniaFact]
    public async Task A_syncs_songs_are_listed_with_why_how_far_and_a_line_for_the_sync()
    {
        var (app, fake, server) = await WithServer();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        var downloads = servers.Downloads;
        Assert.True(downloads.IsEmpty);
        servers.SaveChoices(server, [Friday]);
        fake.Hold = new TaskCompletionSource();

        Task sync = servers.SyncAsync(server);
        WaitFor(() => fake.Started.Count == 1, "the first song coming");
        WaitFor(() => downloads.Items.Count == 1, "its row");

        var batch = Assert.Single(downloads.Batches);
        Assert.Equal("Syncing synology", batch.Text);
        Assert.Contains("songs looked at", batch.Detail);
        var first = Row(downloads, "s2");
        Assert.Equal(DownloadState.Downloading, first.State);
        Assert.Equal("Sync: Friday · synology", first.WhyText);
        Assert.True(first.CanStop);
        Assert.True(app.ViewModel.BackgroundIsDownloads);   // a click on the top bar opens the window

        fake.Hold.SetResult();
        Finish(sync);

        Assert.Empty(downloads.Batches);
        Assert.Equal(["s2", "s1", "s3"], downloads.Items.Select(i => i.Song.Id).Reverse());   // the latest first
        Assert.All(downloads.Items, i => Assert.Equal(DownloadState.Done, i.State));
        Assert.All(downloads.Items, i => Assert.Equal(100, i.Percent));
        Assert.True(downloads.CanClearFinished);
        Assert.False(downloads.CanStopAll);

        downloads.ClearFinished();
        Assert.True(downloads.IsEmpty);
    }

    [AvaloniaFact]
    public async Task Stop_on_a_songs_row_stops_that_song_and_the_sync_goes_on()
    {
        var (app, fake, server) = await WithServer();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        var downloads = servers.Downloads;
        servers.SaveChoices(server, [Friday]);
        fake.Hold = new TaskCompletionSource();
        Task sync = servers.SyncAsync(server);
        WaitFor(() => downloads.Items.Count == 1 && downloads.Items[0].IsDownloading, "the first song coming");

        Row(downloads, "s2").Stop();
        WaitFor(() => fake.Started.Count == 2, "the next song coming");
        fake.Hold.SetResult();
        Finish(sync);

        Assert.Equal(DownloadState.Stopped, Row(downloads, "s2").State);
        Assert.Equal(DownloadState.Done, Row(downloads, "s1").State);
        Assert.Equal(DownloadState.Done, Row(downloads, "s3").State);
        Assert.DoesNotContain(app.Database.GetServerDownloads(server.Server.Id), d => d.SongId == "s2");
        Assert.DoesNotContain(Directory.GetFiles(app.Settings.ServerDownloadsFolder!, "*" + ServerSync.PartialExtension, SearchOption.AllDirectories), _ => true);
    }

    [AvaloniaFact]
    public async Task A_song_that_fails_says_why_and_Retry_failed_brings_it()
    {
        var (app, fake, server) = await WithServer();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        var downloads = servers.Downloads;
        servers.SaveChoices(server, [Friday]);
        fake.Truncate = "s1";
        Finish(servers.SyncAsync(server));

        var failed = Row(downloads, "s1");
        Assert.Equal(DownloadState.Failed, failed.State);
        Assert.StartsWith("Couldn't: It arrived incomplete", failed.StateText);
        Assert.True(downloads.CanRetryFailed);

        fake.Truncate = null;
        Finish(downloads.RetryFailedAsync());

        Assert.Equal(DownloadState.Done, Row(downloads, "s1").State);
        Assert.False(downloads.CanRetryFailed);
        Assert.Contains(app.Database.GetServerDownloads(server.Server.Id), d => d.SongId == "s1");
    }

    [AvaloniaFact]
    public async Task The_queues_songs_wait_their_turn_and_Stop_all_stops_them_with_Try_again()
    {
        var (app, fake, _) = await WithServer();
        using var _ = app;
        var downloads = app.ViewModel.Servers.Downloads;
        var library = app.ViewModel.Library;
        library.SelectedFolder = Assert.Single(library.Roots, r => r.IsServerItself);
        WaitFor(() => library.Results.Count(t => t.IsOnServerOnly) == 4, "the server's songs");
        fake.Hold = new TaskCompletionSource();

        app.ViewModel.AddToQueue(library.Results.Where(t => t.IsOnServerOnly).ToList());
        WaitFor(() => fake.Started.Count == 2, "two coming down");
        WaitFor(() => downloads.Items.Count(i => i.IsDownloading) == 2, "their rows");

        Assert.Equal(4, downloads.Items.Count);
        Assert.Equal(2, downloads.Items.Count(i => i.State == DownloadState.Waiting));
        Assert.All(downloads.Items, i => Assert.Equal("For the queue · synology", i.WhyText));
        WaitFor(() => app.ViewModel.BackgroundStatus == "Downloading 4 songs…", "the top bar");
        Assert.True(app.ViewModel.CanStopBackground);

        downloads.StopAll();
        AppHarness.Settle(300);
        fake.Hold.SetResult();
        WaitFor(() => downloads.Items.All(i => i.IsFinished), "all stopped");

        Assert.All(downloads.Items, i => Assert.Equal(DownloadState.Stopped, i.State));
        Assert.All(app.ViewModel.Queue.Items, q => Assert.True(q.IsWaiting));
        Assert.All(app.ViewModel.Queue.Items, q => Assert.Equal("Download stopped", q.ServerState));
        Assert.All(app.ViewModel.Queue.Items, q => Assert.True(q.CanRetry));
        WaitFor(() => app.ViewModel.BackgroundStatus is null, "the top bar quiet");
    }

    [AvaloniaFact]
    public async Task A_song_a_deck_waits_for_cant_be_stopped()
    {
        var (app, fake, _) = await WithServer();
        using var _ = app;
        var downloads = app.ViewModel.Servers.Downloads;
        var library = app.ViewModel.Library;
        library.SelectedFolder = Assert.Single(library.Roots, r => r.IsServerItself);
        WaitFor(() => library.Results.Count(t => t.IsOnServerOnly) == 4, "the server's songs");
        Track song = library.Results.First(t => t.IsOnServerOnly);
        fake.Hold = new TaskCompletionSource();

        Task<Track?> coming = app.ViewModel.Servers.BringHereAsync(song, "For Deck A");
        WaitFor(() => downloads.Items.Count == 1 && downloads.Items[0].IsDownloading, "its row");

        var row = downloads.Items[0];
        Assert.Equal("For Deck A · synology", row.WhyText);
        Assert.False(row.CanStop);
        downloads.StopAll();
        fake.Hold.SetResult();
        Finish(coming);

        Assert.NotNull(coming.Result);
        Assert.Equal(DownloadState.Done, row.State);
    }

    [AvaloniaFact]
    public async Task Only_the_last_hundred_finished_stay()
    {
        var (app, _, server) = await WithServer();
        using var _ = app;
        var downloads = app.ViewModel.Servers.Downloads;
        for (int n = 0; n < DownloadsViewModel.FinishedKept + 20; n++)
        {
            var song = new ServerSong($"x{n}", $"Song {n}", "Basie", null, null, null, null, null, null, "mp3", 1000, TimeSpan.FromMinutes(3));
            IDownloadReport report = downloads.Begin(server.Server.Id, server.Name, song, "Sync");
            report.Started(1000);
            report.Received(1000);
            report.Ended(DownloadEnd.Arrived);
        }
        AppHarness.Settle(200);

        Assert.Equal(DownloadsViewModel.FinishedKept, downloads.Items.Count);
        Assert.Equal("Song 119", downloads.Items[0].Title);   // the latest first
        Assert.Equal("Song 20", downloads.Items[^1].Title);
    }

    [AvaloniaFact]
    public async Task The_window_opens_from_the_servers_menu_and_lists_the_songs()
    {
        var (app, _, server) = await WithServer();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        servers.SaveChoices(server, [Friday]);
        Finish(servers.SyncAsync(server));

        var window = app.Window.ShowDownloads();
        AppHarness.Settle(100);
        window.UpdateLayout();

        Assert.Same(window, app.Window.ShowDownloads());   // one at a time
        var list = window.FindControl<ListBox>("SongList")!;
        var rows = list.GetVisualDescendants().OfType<ListBoxItem>().ToList();
        Assert.Equal(3, rows.Count);
        var texts = rows[0].GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Done", texts);
        Assert.False(window.FindControl<Button>("StopAllButton")!.IsEnabled);
        Assert.True(window.FindControl<Button>("ClearButton")!.IsEnabled);
        window.Close();
        AppHarness.Settle(50);
        Assert.Null(app.Window.Downloads);
    }
}
