using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.Library.Servers;
using GZJam.Testing;

namespace GZJam.App.Tests;

/// <summary>
/// The folder tree after a scan (#286): each music folder and server keeps its place, taking the
/// new counts, and only a music folder whose folders changed is made again. Made again with every
/// scan, as with each song a sync brings down, the old trees stayed in memory (Avalonia keeps the
/// rows it has shown), a server's thousands of folders each time: 2.4 GB after an evening of
/// syncing, and the window stuttered.
/// </summary>
public sealed class FolderTreeRefreshAppTests
{
    private static AppHarness WithServer()
    {
        var app = new AppHarness();
        var server = app.Database.AddServer("navidrome", "http://nas:4533", "danny");
        var songs = Enumerable.Range(0, 300).Select(a => new ServerSong($"s{a}", "Song", $"Artist {a:000}", "Album", $"Artist {a:000}", 1, 1, 2000,
            $"/music/Artist {a:000}/Album/01 - Song.mp3", "mp3", 4_000_000, TimeSpan.FromMinutes(3))).ToList();
        app.Database.ReplaceServerCatalog(server.Id, songs, DateTime.UtcNow);
        app.ViewModel.Servers.Refresh();
        AppHarness.Settle(200);
        return app;
    }

    private static FolderNodeViewModel Music(AppHarness app) => app.Library.Roots.Single(r => r.IsRoot && !r.IsServer);

    private static FolderNodeViewModel Server(AppHarness app) => app.Library.Roots.Single(r => r.IsServerItself);

    private static void Scan(AppHarness app, Func<bool> done)
    {
        app.Library.RescanRoot(app.Database.GetRoots().Single().Id);
        AppHarness.WaitFor(done, TimeSpan.FromSeconds(10));
        AppHarness.Settle(200);
    }

    [AvaloniaFact]
    public void A_scan_that_only_finds_songs_keeps_every_folder_and_shows_the_new_counts()
    {
        using var app = WithServer();
        FolderNodeViewModel music = Music(app), server = Server(app);
        FolderNodeViewModel folder = music.Children[0];
        var serverFolders = server.SelfAndDescendants().ToList();
        string before = folder.CountText;
        server.IsExpanded = true;
        app.Library.SelectedFolder = folder;

        TestMp3.Write(Path.Combine(folder.Node.FullPath, "A New Song.mp3"), new TestMp3.Tags(Title: "A New Song"));
        Scan(app, () => Music(app).Children[0].CountText != before);

        Assert.Same(music, Music(app));
        Assert.Same(folder, Music(app).Children[0]);
        Assert.Equal(serverFolders, Server(app).SelfAndDescendants());   // not one made again
        Assert.True(Server(app).IsExpanded);
        Assert.Same(folder, app.Library.SelectedFolder);
    }

    [AvaloniaFact]
    public void A_new_folder_makes_only_its_music_folder_again()
    {
        using var app = WithServer();
        FolderNodeViewModel music = Music(app), server = Server(app);
        server.IsExpanded = true;
        server.Children[5].IsExpanded = true;
        string chosen = music.Children[0].Name;
        app.Library.SelectedFolder = music.Children[0];

        TestMp3.Write(Path.Combine(music.Node.FullPath, "A New Folder", "A New Song.mp3"), new TestMp3.Tags(Title: "A New Song"));
        Scan(app, () => Music(app).SelfAndDescendants().Any(n => n.Name == "A New Folder"));

        Assert.NotSame(music, Music(app));   // its folders changed
        Assert.Same(server, Server(app));   // the server's thousands of folders stay as they were
        Assert.True(Server(app).Children[5].IsExpanded);
        Assert.Equal(chosen, app.Library.SelectedFolder?.Name);   // chosen again, in the new tree
        Assert.True(Music(app).IsExpanded);
    }

    [AvaloniaFact]
    public void A_servers_own_change_still_makes_it_again()
    {
        using var app = WithServer();
        FolderNodeViewModel server = Server(app);

        app.Library.RefreshServers();   // its song list read again, a playlist kept: its marks follow

        Assert.NotSame(server, Server(app));
    }
}
