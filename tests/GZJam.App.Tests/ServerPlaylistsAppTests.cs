using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.Library;
using GZJam.Library.Servers;
using GZJam.Testing;

namespace GZJam.App.Tests;

/// <summary>
/// A server's playlists in the library (#290), in the Playlists panel (#355): under a heading for
/// the server, after the DJ's own, folding away; opened in the song list; kept on the computer,
/// shown once; copied into the DJ's own; and in folders from their names, with the server's
/// <i>Playlist folders in names</i> on.
/// </summary>
public sealed class ServerPlaylistsAppTests
{
    private static async Task<(AppHarness App, FakeNavidrome Fake, ServerItemViewModel Server)> WithServer(string? separator = null)
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
        Assert.True((await servers.SaveAsync(null, new ServerEdit("synology", "synology:4533", "gzjam", "sesame", separator))).Saved);
        var item = Assert.Single(servers.Items);
        WaitFor(() => app.Library.PlaylistRows.OfType<ServerPlaylistRow>().Count() == 2, "the server's playlists in the Playlists panel");
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

    /// <summary>The panel's rows as text, as the DJ reads them down the panel.</summary>
    private static List<string> Rows(AppHarness app) => app.Library.PlaylistRows.Select(r => r switch
    {
        ServerPlaylistsHeading h => $"{(h.IsExpanded ? "▾" : "▸")} [{h.Name} ({h.Count})]",
        ServerPlaylistFolderRow f => new string(' ', f.Path.Count * 2) + $"{(f.IsExpanded ? "▾" : "▸")} {f.Name} ({f.Count})",
        ServerPlaylistRow p => new string(' ', p.Depth * 2) + p.Name + (p.IsKept ? " (kept)" : ""),
        PlaylistInfo p => p.Name,
        PlaylistFolderRow f => $"▾ {f.Name}",
        _ => "?",
    }).ToList();

    private static ServerPlaylistRow Playlist(AppHarness app, string name) =>
        Assert.Single(app.Library.PlaylistRows.OfType<ServerPlaylistRow>(), r => r.Name == name);

    private static void Open(AppHarness app, string name, Func<IReadOnlyList<Track>, bool> ready)
    {
        app.Library.SelectedPlaylistRow = Playlist(app, name);
        WaitFor(() => ready(app.Library.Results), $"{name}'s songs");
    }

    [AvaloniaFact]
    public async Task The_servers_playlists_are_under_its_heading_after_your_own_and_one_opens_in_its_order()
    {
        var (app, _, _) = await WithServer();
        using var _ = app;
        app.ViewModel.CreatePlaylist("Mine", null);
        AppHarness.Settle(100);

        Assert.Equal(["Mine", "▾ [synology (2)]", "  After midnight", "  Friday"], Rows(app));
        // Not in the Servers source's tree any more: its folders only.
        Assert.DoesNotContain(app.Library.Roots.Where(r => r.IsServerItself).SelectMany(r => r.Children), c => c.Name == "Playlists");

        Open(app, "Friday", r => r.Count == 3);

        var results = app.Library.Results;
        Assert.Equal(["Air Mail Special", "Basie Boogie", "Lucky Rock Blues"], results.Select(t => t.Title));
        Assert.All(results, t => Assert.True(t.IsOnServerOnly));
        Assert.Same(Playlist(app, "Friday"), app.Library.SelectedPlaylistRow);
        Assert.Null(app.Library.SelectedPlaylist);
        Assert.Contains("Friday on synology", app.Library.ScopeText);
    }

    [AvaloniaFact]
    public async Task A_servers_heading_folds_away_and_stays_folded()
    {
        var (app, _, server) = await WithServer();
        using var _ = app;
        var heading = Assert.Single(app.Library.PlaylistRows.OfType<ServerPlaylistsHeading>());

        heading.IsExpanded = false;
        AppHarness.Settle(100);

        Assert.Equal(["▸ [synology (2)]"], Rows(app));
        Assert.Contains(server.Server.Id, app.Settings.FoldedServerPlaylists);
    }

    [AvaloniaFact]
    public async Task Read_again_brings_a_playlist_made_since_and_offline_it_says_so()
    {
        var (app, fake, server) = await WithServer();
        using var _ = app;
        fake.Playlists["pl3"] = ["s1"];
        fake.PlaylistNames["pl3"] = "Brand new";

        Finish(app.ViewModel.Servers.ReadPlaylistsAgainAsync(server.Server.Id));
        Assert.Equal(["▾ [synology (3)]", "  After midnight", "  Brand new", "  Friday"], Rows(app));

        fake.Down = true;
        app.ViewModel.Servers.MarkOffline(server);
        AppHarness.Settle(100);
        Finish(app.ViewModel.Servers.ReadPlaylistsAgainAsync(server.Server.Id));
        Assert.Contains("isn't answering", app.ViewModel.Notice);
        Assert.True(app.Library.PlaylistRows.OfType<ServerPlaylistsHeading>().Single().IsOffline);
        Assert.Equal(3, app.Library.PlaylistRows.OfType<ServerPlaylistRow>().Count());   // as last read
    }

    [AvaloniaFact]
    public async Task Kept_on_this_computer_it_shows_once_under_its_server_and_stop_following_makes_it_yours()
    {
        var (app, _, server) = await WithServer();
        using var _ = app;

        Finish(app.ViewModel.Servers.KeepPlaylistAsync(server.Server.Id, "pl1"));

        PlaylistInfo friday = Assert.Single(app.Database.GetPlaylists(), p => p.Name == "Friday");
        Assert.Equal(3, app.Database.GetPlaylistEntries(friday.Id).Count);
        Assert.Equal(["▾ [synology (2)]", "  After midnight", "  Friday (kept)"], Rows(app));   // not among your own too

        Open(app, "Friday", r => r.Count == 3 && r.All(t => !t.IsOnServerOnly));
        Assert.Contains("Kept on this computer", app.Library.ServerNote);
        Assert.Equal(3, app.ViewModel.Servers.DownloadsInFolder(server.Server.Id, FolderNode.ServerPlaylistPath("pl1")).Count);

        app.ViewModel.Servers.StopFollowing(Playlist(app, "Friday").Kept!);
        AppHarness.Settle(100);
        Assert.Equal(["Friday", "▾ [synology (2)]", "  After midnight", "  Friday"], Rows(app));
    }

    [AvaloniaFact]
    public async Task Opened_it_shows_the_servers_latest_and_offline_as_last_read()
    {
        var (app, fake, server) = await WithServer();
        using var _ = app;
        fake.Playlists["pl1"] = ["s3", "s4"];   // changed on the server since

        Open(app, "Friday", r => r.Count == 2);
        Assert.Equal(["Lucky Rock Blues", "Li'l Darlin'"], app.Library.Results.Select(t => t.Title));

        app.Library.SelectedServerPlaylist = null;
        fake.Down = true;
        app.ViewModel.Servers.MarkOffline(server);
        Open(app, "Friday", r => r.Count == 2);   // as last read, from this computer
        Assert.Equal(["Lucky Rock Blues", "Li'l Darlin'"], app.Library.Results.Select(t => t.Title));
    }

    [AvaloniaFact]
    public async Task A_search_in_a_playlist_finds_its_songs_only()
    {
        var (app, _, _) = await WithServer();
        using var _ = app;
        var library = app.Library;
        Open(app, "Friday", r => r.Count == 3);

        library.SearchText = "basie";
        WaitFor(() => library.Results.Count == 1, "Friday's Basie song, not Li'l Darlin'");
        Assert.Equal("Basie Boogie", library.Results[0].Title);
    }

    [AvaloniaFact]
    public async Task Copy_to_my_playlists_makes_one_of_your_own_with_its_songs()
    {
        var (app, _, _) = await WithServer();
        using var _ = app;

        Finish(app.ViewModel.Servers.CopyPlaylistAsync(Playlist(app, "Friday")));
        WaitFor(() => app.Database.GetPlaylists().SingleOrDefault(p => p.Name == "Friday")?.Count == 3, "its songs, fetched into the copy");

        Assert.Equal(["Friday", "▾ [synology (2)]", "  After midnight", "  Friday"], Rows(app));   // yours, and the server's still
        Assert.Null(app.Database.GetPlaylists().Single(p => p.Name == "Friday").FollowsServer);
    }

    /// <summary>With the server's playlist folders in names on, "WCS/Setlists/Nov 8" is Nov 8 in WCS › Setlists, as Feishin shows it.</summary>
    [AvaloniaFact]
    public async Task With_folders_in_names_a_servers_playlists_are_in_folders_and_one_sent_from_yours_gets_its_path()
    {
        var (app, fake, server) = await WithServer(separator: "/");
        using var _ = app;
        fake.Playlists["pl3"] = ["s1"];
        fake.PlaylistNames["pl3"] = "WCS/Setlists/Nov 8";
        fake.Playlists["pl4"] = ["s2"];
        fake.PlaylistNames["pl4"] = "WCS/Warm-ups";
        Finish(app.ViewModel.Servers.ReadPlaylistsAgainAsync(server.Server.Id));

        Assert.Equal(["▾ [synology (4)]", "  ▸ WCS (2)", "  After midnight", "  Friday"], Rows(app));
        app.Library.PlaylistRows.OfType<ServerPlaylistFolderRow>().Single().IsExpanded = true;
        AppHarness.Settle(50);
        app.Library.PlaylistRows.OfType<ServerPlaylistFolderRow>().Single(f => f.Name == "Setlists").IsExpanded = true;
        AppHarness.Settle(50);
        Assert.Equal(["▾ [synology (4)]", "  ▾ WCS (2)", "    ▾ Setlists (1)", "      Nov 8", "    Warm-ups", "  After midnight", "  Friday"], Rows(app));
        app.Screenshot("server-playlists-folders");

        // One of yours in your folders goes with them in its name, and shows in the same folder there.
        var wcs = app.Database.CreatePlaylistFolder("WCS");
        var setlists = app.Database.CreatePlaylistFolder("Setlists", wcs.Id);
        var song = app.Library.Results.First(t => !t.IsOnServerOnly);
        var nov15 = app.ViewModel.CreatePlaylist("Nov 15", [song], setlists.Id)!;
        // The server has its song too, as s2.
        app.Database.SaveServerSong(new ServerSongCopy(server.Server.Id, "s2", song.Path, null, "/music/a.mp3", OwnCopy: true), DateTime.UtcNow);
        app.ViewModel.ConfirmAsync = (_, _, _) => Task.FromResult(true);
        Finish(app.ViewModel.Servers.SendPlaylistAsync(server, nov15));
        Assert.Contains("WCS/Setlists/Nov 15", fake.PlaylistNames.Values);
        AppHarness.Settle(100);
        Assert.Contains("      Nov 15", Rows(app));
    }
}
