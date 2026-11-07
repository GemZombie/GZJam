using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.Library;
using GZJam.Library.Servers;
using GZJam.Testing;

namespace GZJam.App.Tests;

/// <summary>
/// The library's sources (#318): My music and Servers, each with its own folders and its own
/// search. The servers' downloads folder isn't one of the DJ's music folders: its songs are the
/// servers', shown under them.
/// </summary>
public sealed class SourcesAppTests
{
    /// <summary>GZJam with the fake Navidrome as "synology", each of its songs a real (silent) MP3 titled with its ID.</summary>
    private static async Task<(AppHarness App, FakeNavidrome Server, ServerItemViewModel Card)> WithServer(
        Action<AppHarness, FakeNavidrome>? first = null)
    {
        var app = new AppHarness();
        var fake = new FakeNavidrome();
        foreach (string id in (string[])["s1", "s2", "s3", "s4"])
        {
            string mp3 = Path.Combine(app.WorkDirectory, $"{id}.mp3");
            TestMp3.Write(mp3, new TestMp3.Tags(Title: $"Song {id}"));
            fake.Files[id] = File.ReadAllBytes(mp3);
        }
        app.Settings.ServerDownloadsFolder = Path.Combine(app.WorkDirectory, "GZJam from servers");
        first?.Invoke(app, fake);
        var servers = app.ViewModel.Servers;
        servers.PreviewFolder = Path.Combine(app.WorkDirectory, "previews");
        servers.CheckServer = (_, _, _, _) => Task.FromResult(new ServerCheck(ServerCheckOutcome.Connected, "navidrome", "0.64.2", true, true, 4));
        servers.ClientFor = (server, password) => new SubsonicClient(new HttpClient(fake), server.Address, server.UserName, password) { RetryAfter = TimeSpan.Zero };
        Assert.True((await servers.SaveAsync(null, new ServerEdit("synology", "synology:4533", "gzjam", "sesame"))).Saved);
        AppHarness.Settle(100);
        WaitFor(() => app.Library.Roots.Any(r => r.IsServer && r.Children.Count > 0), "the server's folders");
        return (app, fake, Assert.Single(servers.Items));
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

    /// <summary>Downloads the server's Count Basie album (s1 and s4) into the downloads folder.</summary>
    private static void DownloadBasie(AppHarness app)
    {
        var library = app.Library;
        library.ChooseSource(LibrarySource.Servers);
        WaitFor(() => library.Results.Any(t => t.IsOnServerOnly && t.Artist == "Count Basie"), "the server's songs");
        Finish(app.ViewModel.Servers.DownloadAlbumsAsync([library.Results.First(t => t.IsOnServerOnly && t.Artist == "Count Basie")]));
        WaitFor(() => app.Database.GetServerSongsHere(app.ViewModel.Servers.Items[0].Server.Id).Count == 2, "the album here");
        WaitFor(() => app.Database.Search("song").Count == 2, "its songs read into the index");
    }

    [AvaloniaFact]
    public void Without_a_server_there_is_only_My_music()
    {
        using var app = new AppHarness();
        var source = Assert.Single(app.Library.Sources);
        Assert.Equal(("My music", LibrarySource.MyMusic), (source.Name, source.Kind));
        Assert.Same(source, app.Library.SelectedSource);
        Assert.Equal(app.Library.Roots[0].CountText, source.CountText);
    }

    [AvaloniaFact]
    public async Task Each_source_shows_its_own_folders_in_the_tree()
    {
        var (app, _, _) = await WithServer();
        using var _ = app;
        var library = app.Library;

        Assert.Equal(["My music", "Servers"], library.Sources.Select(s => s.Name));
        Assert.Equal("4", library.Sources[1].CountText);
        Assert.Equal(LibrarySource.MyMusic, library.Source);
        Assert.All(library.FolderRows, r => Assert.False(r.IsServer));
        Assert.Contains(library.FolderRows, r => r.Name == "Music");

        library.SelectedSource = library.Sources[1];
        AppHarness.Settle(100);
        Assert.Equal(LibrarySource.Servers, library.Source);
        Assert.Equal("synology", Assert.Single(library.FolderRows).Name);   // the server, closed
        Assert.True(library.Sources[1].IsCurrent);
        Assert.False(library.Sources[0].IsCurrent);
        Assert.True(library.IsServerShowing);   // "Only on this computer" for all of them
        app.Screenshot("sources-servers");

        // A folder of the other source, chosen (by Show in folder tree, say): its source's tree.
        library.SelectedFolder = library.Roots.First(r => !r.IsServer);
        AppHarness.Settle(100);
        Assert.Equal(LibrarySource.MyMusic, library.Source);
        Assert.Null(library.SelectedSource);   // the folder is what's showing
        Assert.True(library.Sources[0].IsCurrent);
    }

    [AvaloniaFact]
    public async Task Songs_downloaded_from_a_server_are_the_servers_not_in_My_music()
    {
        var (app, _, _) = await WithServer();
        using var _ = app;
        DownloadBasie(app);
        var library = app.Library;

        library.ChooseSource(LibrarySource.MyMusic);
        AppHarness.Settle(200);
        Assert.DoesNotContain(library.Roots, r => r.Name == "GZJam from servers");   // not a music folder of the DJ's
        Assert.DoesNotContain(library.Results, t => t.Title is "Song s1" or "Song s4");
        library.SearchText = "song";
        WaitFor(() => !library.IsSearching, "the search");
        Assert.Empty(library.Results);

        // Under the server, the songs on this computer are the library's own rows, playable offline.
        library.ChooseSource(LibrarySource.Servers);
        library.SearchText = "basie";
        WaitFor(() => library.Results.Count == 2 && library.Results.All(t => !t.IsOnServerOnly), "the server's songs, here");
        Assert.All(library.Results, t => Assert.False(t.IsYours));   // downloads, not the DJ's own
    }

    [AvaloniaFact]
    public async Task Searching_the_servers_covers_every_server_and_says_what_My_music_has()
    {
        var (app, _, _) = await WithServer();
        using var _ = app;
        var library = app.Library;

        library.ChooseSource(LibrarySource.Servers);
        library.SearchText = "basie";
        WaitFor(() => library.Results.Count == 2, "the server's two Basie songs");
        Assert.All(library.Results, t => Assert.True(t.IsOnServerOnly));
        WaitFor(() => library.HasAlsoOn, "the Also line");
        Assert.Equal("Also: 8 in My music", library.AlsoOnText);   // Count Basie and Count Basie Orchestra

        library.ShowAlsoOnCommand.Execute(null);
        WaitFor(() => library.Source == LibrarySource.MyMusic && library.Results.Count == 8, "My music's songs");
        Assert.Equal("basie", library.SearchText);
        Assert.Null(library.SelectedFolder);
    }

    [AvaloniaFact]
    public async Task Searching_My_music_says_what_a_server_has_that_isnt_yours()
    {
        var (app, _, _) = await WithServer();
        using var _ = app;
        DownloadBasie(app);   // on this computer now, but still the server's
        var library = app.Library;

        library.ChooseSource(LibrarySource.MyMusic);
        library.SearchText = "basie";
        WaitFor(() => library.HasAlsoOn, "the Also line");
        Assert.Equal("Also: 2 on synology", library.AlsoOnText);

        library.ShowAlsoOnCommand.Execute(null);
        WaitFor(() => library.Source == LibrarySource.Servers && library.Results.Count == 2, "the server's songs");
        Assert.True(library.SelectedFolder?.IsServerItself);
    }

    [AvaloniaFact]
    public async Task A_servers_song_that_is_the_DJs_own_says_yours()
    {
        // The DJ's own copy of Basie Boogie, in their music folder: GZJam finds it's the server's song.
        string mine = "";
        var (app, _, card) = await WithServer((app, fake) =>
        {
            mine = Path.Combine(app.MusicPath, "Mine", "Basie Boogie.mp3");
            TestMp3.Write(mine, new TestMp3.Tags("Basie Boogie", "Count Basie"));
            fake.Sizes["s1"] = new FileInfo(mine).Length;
            Finish(app.Library.ScanRootAsync(app.Database.GetRoots().First(r => r.Path == app.MusicPath).Id));
        });
        using var _ = app;
        WaitFor(() => app.Database.GetServerOwnCopies(card.Server.Id).Contains("s1"), "Basie Boogie found to be the DJ's");

        var library = app.Library;
        library.ChooseSource(LibrarySource.Servers);
        library.SearchText = "basie";
        WaitFor(() => library.Results.Any(t => t.IsYours), "yours");
        Track boogie = Assert.Single(library.Results, t => t.IsYours);
        Assert.Equal(mine, boogie.Path);
        app.Screenshot("sources-yours");
    }

    [AvaloniaFact]
    public async Task A_downloads_folder_inside_a_music_folder_becomes_the_servers_with_what_the_DJ_gave_its_songs()
    {
        // As 1.4 left it: "GZJam from servers" inside the DJ's Music, its songs in Music's tree.
        var (app, _, card) = await WithServer();
        using var _ = app;
        string inside = Path.Combine(app.MusicPath, "GZJam from servers");
        app.Settings.ServerDownloadsFolder = inside;
        DownloadBasie(app);
        var roots = app.Database.GetRoots();
        Assert.Equal(inside, Assert.Single(roots, r => r.Path != app.MusicPath).Path);   // the downloads folder is its own
        string path = app.Database.GetServerSongsHere(card.Server.Id).Values.First();
        Assert.Equal(roots.Single(r => r.Path == inside).Id, app.Database.TrackForFile(path).RootId);

        // Music scanned again leaves it out: neither found there nor gone.
        Finish(app.Library.ScanRootAsync(roots.Single(r => r.Path == app.MusicPath).Id));
        Assert.Equal(roots.Single(r => r.Path == inside).Id, app.Database.TrackForFile(path).RootId);
        Assert.DoesNotContain(app.Library.Roots.SelectMany(r => r.SelfAndDescendants()), n => n.Name == "GZJam from servers");
    }
}
