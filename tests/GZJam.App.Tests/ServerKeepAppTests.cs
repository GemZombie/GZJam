using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.Library;
using GZJam.Library.Servers;
using GZJam.Testing;

namespace GZJam.App.Tests;

/// <summary>
/// Keep a copy in my library (#285): a server's songs into the DJ's own music folder, laid out as
/// on the server, never over a file; a downloaded one moves with all that's kept by its path; a
/// song the DJ has already is left as it is; kept songs are the DJ's own from then on.
/// </summary>
public sealed class ServerKeepAppTests
{
    private const string BoogiePlace = "Jazz & Swing/Count Basie/Cute/03 - Basie Boogie.mp3";
    private const string DarlinPlace = "Jazz & Swing/Count Basie/Cute/05 - Li'l Darlin'.mp3";

    private static async Task<(AppHarness App, FakeNavidrome Fake, ServerItemViewModel Server)> WithServer(
        bool sync = true, Func<AppHarness, FakeNavidrome, Task>? first = null)
    {
        var app = new AppHarness();
        var fake = new FakeNavidrome();
        foreach (string id in (string[])["s1", "s2", "s3", "s4"])
        {
            string mp3 = Path.Combine(app.WorkDirectory, $"{id}.mp3");
            TestMp3.Write(mp3, new TestMp3.Tags(Title: id));
            fake.Files[id] = File.ReadAllBytes(mp3);
        }
        if (first is not null)
            await first(app, fake);
        app.Settings.ServerDownloadsFolder = Path.Combine(app.WorkDirectory, "GZJam from servers");
        app.Settings.ServerKeepFolder = KeepFolder(app);
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
            WaitFor(() => app.Database.GetServerDownloads(item.Server.Id).All(d => app.Database.FindTrack(d.Path) is not null), "the songs in the library");
        }
        app.ViewModel.ConfirmAsync = (_, _, _) => Task.FromResult(true);
        return (app, fake, item);
    }

    private static string KeepFolder(AppHarness app) => Path.Combine(app.WorkDirectory, "My music");

    private static string Kept(AppHarness app, string place) => Path.Combine([KeepFolder(app), .. place.Split('/')]);

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
        app.Database.GetServerSongs(server.Server.Id).Single(d => d.SongId == songId).Path;

    private static Track NotHere(AppHarness app, ServerItemViewModel server, string songId) =>
        app.ViewModel.Servers.TrackFor($"{ServerTrack.PathPrefix}{server.Server.Id}/{songId}")!;

    [AvaloniaFact]
    public async Task A_downloaded_song_moves_into_my_music_with_its_hearts_playlists_and_queue()
    {
        var (app, _, server) = await WithServer();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        string boogie = PathOf(app, server, "s1");
        app.Database.SetRating([boogie], Rating.Loved);
        app.ViewModel.AddToQueue([app.Database.TrackForFile(boogie)]);
        long friday = Assert.Single(app.Database.GetServerChoices(server.Server.Id)).PlaylistId!.Value;
        Assert.True(servers.CanKeepCopy([app.Database.TrackForFile(boogie)]));

        Finish(servers.KeepTracksAsync([app.Database.TrackForFile(boogie)]));

        string kept = Kept(app, BoogiePlace);
        Assert.True(File.Exists(kept));
        Assert.False(File.Exists(boogie));
        Assert.Equal(kept, PathOf(app, server, "s1"));
        Assert.True(app.Database.GetServerSongs(server.Server.Id).Single(s => s.SongId == "s1").OwnCopy);
        Assert.DoesNotContain(app.Database.GetServerDownloads(server.Server.Id), d => d.SongId == "s1");   // never removed to free up space
        Assert.Equal(Rating.Loved, app.Database.GetMarks(kept).Rating);
        Assert.Contains(app.Database.GetPlaylistEntries(friday), e => e.Path == kept);
        Assert.Contains(app.ViewModel.Queue.Items, q => q.Track.Path == kept);
        WaitFor(() => app.Database.FindTrack(kept) is not null, "the kept song in the library");
        Assert.Contains(app.Database.GetRoots(), r => r.Path == KeepFolder(app));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(kept)!, "cover.png")));   // its album's picture came too
        Assert.Empty(servers.DownloadsFor([app.Database.TrackForFile(kept)]));
        Assert.False(servers.CanKeepCopy([app.Database.TrackForFile(kept)]));

        Finish(servers.SyncAsync(server));
        Assert.True(File.Exists(kept));
        Assert.False(File.Exists(boogie));   // a sync doesn't download it again
    }

    [AvaloniaFact]
    public async Task A_song_not_here_comes_straight_into_my_music_and_never_counts_as_a_download()
    {
        var (app, fake, server) = await WithServer(sync: false);
        using var _ = app;
        var servers = app.ViewModel.Servers;

        Finish(servers.KeepTracksAsync([NotHere(app, server, "s4")]));

        string kept = Kept(app, DarlinPlace);
        Assert.True(File.Exists(kept));
        Assert.Equal(fake.Files["s4"], File.ReadAllBytes(kept));
        Assert.Equal((0, 0L), app.Database.GetDownloadedTotals());
        Assert.True(app.Database.GetServerSongs(server.Server.Id).Single().OwnCopy);
        Assert.False(Directory.Exists(Path.Combine(app.Settings.ServerDownloadsFolder!, "synology", "Jazz & Swing")));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(kept)!, "cover.png")));
        WaitFor(() => app.Database.FindTrack(kept) is not null, "the kept song in the library");
    }

    [AvaloniaFact]
    public async Task The_first_time_it_asks_where_and_shows_the_paths_before_anything_moves()
    {
        var (app, _, server) = await WithServer();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        app.Settings.ServerKeepFolder = null;
        string boogie = PathOf(app, server, "s1");
        string? asked = null;
        servers.ChooseFolderAsync = _ => Task.FromResult<string?>(null);

        Finish(servers.KeepTracksAsync([app.Database.TrackForFile(boogie)]));
        Assert.True(File.Exists(boogie));   // no folder chosen: nothing happens
        Assert.Null(app.Settings.ServerKeepFolder);

        string music = Path.Combine(app.WorkDirectory, "Music");
        servers.ChooseFolderAsync = _ => Task.FromResult<string?>(music);
        app.ViewModel.ConfirmAsync = (_, body, _) =>
        {
            asked = body;
            return Task.FromResult(false);
        };
        Finish(servers.KeepFolderCopyAsync(server.Server.Id, "Jazz & Swing/Count Basie", "Count Basie"));
        Assert.NotNull(asked);
        Assert.Contains(music, asked);
        Assert.Contains(Path.Combine("Jazz & Swing", "Count Basie", "Cute", "03 - Basie Boogie.mp3"), asked);
        Assert.Contains("1 song downloaded already moves there", asked);
        Assert.Contains("1 song comes from synology now", asked);
        Assert.True(File.Exists(boogie));   // said no: nothing moved, and the folder isn't kept
        Assert.Null(app.Settings.ServerKeepFolder);

        app.ViewModel.ConfirmAsync = (_, _, _) => Task.FromResult(true);
        Finish(servers.KeepFolderCopyAsync(server.Server.Id, "Jazz & Swing/Count Basie", "Count Basie"));
        Assert.Equal(music, app.Settings.ServerKeepFolder);
        Assert.True(File.Exists(Path.Combine(music, "Jazz & Swing", "Count Basie", "Cute", "03 - Basie Boogie.mp3")));
        Assert.True(File.Exists(Path.Combine(music, "Jazz & Swing", "Count Basie", "Cute", "05 - Li'l Darlin'.mp3")));
    }

    [AvaloniaFact]
    public async Task A_song_already_in_my_library_shows_as_mine_and_is_left_as_it_is()
    {
        string mine = "";
        var (app, fake, server) = await WithServer(sync: false, first: async (app, fake) =>
        {
            string music = Path.Combine(app.WorkDirectory, "Old music");
            Directory.CreateDirectory(music);
            mine = Path.Combine(music, "darlin.mp3");
            TestMp3.Write(mine, new TestMp3.Tags(Title: "Li'l Darlin'", Artist: "Count Basie", Comment: "my own, tagged my way"));
            fake.Sizes["s4"] = new FileInfo(mine).Length + 1000;   // another size (tagged otherwise), the same length
            await app.ViewModel.Library.AddRootAsync(music);
        });
        using var _ = app;
        var servers = app.ViewModel.Servers;

        WaitFor(() => app.Database.GetServerSongs(server.Server.Id).Any(s => s.SongId == "s4"), "Li'l Darlin' found to be mine");
        var copy = app.Database.GetServerSongs(server.Server.Id).Single(s => s.SongId == "s4");
        Assert.Equal(mine, copy.Path);
        Assert.True(copy.OwnCopy);
        var browse = await servers.BrowseAsync(server.Server.Id, "Jazz & Swing/Count Basie", "", CancellationToken.None);
        Assert.Contains(browse.Tracks, t => t.Path == mine && !t.IsOnServerOnly);

        string? asked = null;
        app.ViewModel.ConfirmAsync = (_, body, _) =>
        {
            asked = body;
            return Task.FromResult(true);
        };
        Finish(servers.KeepFolderCopyAsync(server.Server.Id, "Jazz & Swing/Count Basie", "Count Basie"));
        Assert.Contains("1 song is in your library already", asked);
        Assert.True(File.Exists(Kept(app, BoogiePlace)));
        Assert.False(File.Exists(Kept(app, DarlinPlace)));   // not twice
        Assert.True(File.Exists(mine));
        Assert.Equal(1, fake.Downloads);   // Basie Boogie only
    }

    [AvaloniaFact]
    public async Task It_never_replaces_a_file_and_never_goes_inside_the_downloads_folder()
    {
        var (app, _, server) = await WithServer(sync: false);
        using var _ = app;
        var servers = app.ViewModel.Servers;
        string there = Kept(app, DarlinPlace);
        Directory.CreateDirectory(Path.GetDirectoryName(there)!);
        File.WriteAllText(there, "a file of the DJ's");

        Finish(servers.KeepTracksAsync([NotHere(app, server, "s4")]));

        Assert.Equal("a file of the DJ's", File.ReadAllText(there));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(there)!, "05 - Li'l Darlin' (2).mp3")));

        Assert.False(servers.SetKeepFolder(Path.Combine(servers.DownloadsFolder, "Mine")));
        Assert.Equal(KeepFolder(app), servers.KeepFolder);
    }
}
