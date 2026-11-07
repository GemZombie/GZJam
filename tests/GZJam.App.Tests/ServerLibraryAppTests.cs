using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.Library;
using GZJam.Library.Servers;
using GZJam.Testing;

namespace GZJam.App.Tests;

/// <summary>A server in the library itself (#290): in the folder tree, its songs in the song list, brought here when they're wanted.</summary>
public sealed class ServerLibraryAppTests
{
    /// <summary>GZJam with the fake Navidrome as "synology", each of its songs a real (silent) MP3.</summary>
    private static async Task<(AppHarness App, FakeNavidrome Server, ServerItemViewModel Card)> WithServer()
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
        servers.CheckServer = (_, _, _, _) => Task.FromResult(new ServerCheck(ServerCheckOutcome.Connected, "navidrome", "0.64.2", true, true, 4));
        servers.ClientFor = (server, password) => new SubsonicClient(new HttpClient(fake), server.Address, server.UserName, password) { RetryAfter = TimeSpan.Zero };
        Assert.True((await servers.SaveAsync(null, new ServerEdit("synology", "synology:4533", "gzjam", "sesame"))).Saved);
        AppHarness.Settle(100);
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

    /// <summary>Waits for the song list to show what <paramref name="ready"/> wants.</summary>
    private static void WaitFor(Func<bool> ready, string what)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (!ready() && DateTime.UtcNow < until)
            AppHarness.Settle(30);
        Assert.True(ready(), what);
    }

    private static FolderNodeViewModel ServerNode(AppHarness app) =>
        Assert.Single(app.ViewModel.Library.Roots, r => r.IsServer);

    private static void ShowServer(AppHarness app)
    {
        app.ViewModel.Library.SelectedFolder = ServerNode(app);
        WaitFor(() => app.ViewModel.Library.Results.Any(t => t.IsOnServerOnly), "the server's songs");
    }

    [AvaloniaFact]
    public async Task The_server_sits_in_the_folder_tree_after_the_music_folders()
    {
        var (app, _, card) = await WithServer();
        using var _ = app;
        var roots = app.ViewModel.Library.Roots.Where(r => !r.IsFiles).ToList();   // Files' places after (#318)

        Assert.True(roots[^1].IsServer);
        Assert.Equal("synology", roots[^1].Name);
        Assert.True(roots[^1].IsServerItself);
        Assert.False(roots[^1].IsRemovableRoot);   // removed in Settings, not here

        app.ViewModel.ConfirmAsync = (_, _, _) => Task.FromResult(true);
        await app.ViewModel.Servers.RemoveAsync(card);
        AppHarness.Settle(50);
        Assert.DoesNotContain(app.ViewModel.Library.Roots, r => r.IsServer);
    }

    [AvaloniaFact]
    public async Task Selecting_it_shows_whats_new_there_marked_as_not_on_this_laptop()
    {
        var (app, _, _) = await WithServer();
        using var _ = app;
        ShowServer(app);
        var library = app.ViewModel.Library;

        Assert.Equal(4, library.Results.Count);   // the songs of its newest albums
        Assert.All(library.Results, t => Assert.True(t.IsOnServerOnly));
        Assert.All(library.Results, t => Assert.True(t.IsDimmed));
        Track boogie = library.Results.Single(t => t.Title == "Basie Boogie");
        Assert.Equal(("Count Basie", "Cute", 3), (boogie.Artist, boogie.Album, boogie.TrackNumber));
        Assert.StartsWith("on synology", library.ScopeText);
    }

    [AvaloniaFact]
    public async Task Typing_searches_the_server_and_says_the_librarys_own_filters_are_left_out()
    {
        var (app, _, _) = await WithServer();
        using var _ = app;
        ShowServer(app);
        var library = app.ViewModel.Library;

        library.SearchText = "blues rating:yes";
        WaitFor(() => library.Results.Count == 1, "the search's one song");

        Assert.Equal("Lucky Rock Blues", library.Results[0].Title);
        Assert.Equal("rating: is for the songs on this computer: left out here.", library.ServerNote);
    }

    [AvaloniaFact]
    public async Task The_servers_folders_are_in_the_tree_and_a_folder_shows_its_songs()
    {
        var (app, _, _) = await WithServer();
        using var _ = app;
        WaitFor(() => ServerNode(app).Children.Count > 0, "the server's folders");
        FolderNodeViewModel server = ServerNode(app);

        WaitFor(() => ServerNode(app).Children.Count == 3, "its playlists too");
        server = ServerNode(app);
        Assert.Equal(["Playlists", "Blues", "Jazz & Swing"], server.Children.Select(c => c.Name));   // its playlists first
        Assert.Equal("4", server.CountText);
        FolderNodeViewModel basie = server.Children[2].Children[0];
        Assert.Equal(("Count Basie", "2"), (basie.Name, basie.CountText));
        Assert.False(basie.IsServerItself);

        app.ViewModel.Library.SelectedFolder = basie;
        WaitFor(() => app.ViewModel.Library.Results.Count == 2 && app.ViewModel.Library.Results.All(t => t.IsOnServerOnly), "the folder's songs");
        Assert.Equal(["Basie Boogie", "Li'l Darlin'"], app.ViewModel.Library.Results.Select(t => t.Title));
        Assert.Contains("synology › Jazz & Swing › Count Basie", app.ViewModel.Library.ScopeText);
    }

    [AvaloniaFact]
    public async Task A_double_click_brings_a_song_here_and_loads_it_onto_a_deck()
    {
        var (app, _, _) = await WithServer();
        using var _ = app;
        ShowServer(app);
        Track boogie = app.ViewModel.Library.Results.Single(t => t.Title == "Basie Boogie");

        Task<bool> play = app.ViewModel.PlaySongAsync(boogie);
        Finish(play);

        Assert.True(play.Result);
        string here = Path.Combine(app.Settings.ServerDownloadsFolder!, "synology", "Jazz & Swing", "Count Basie", "Cute", "03 - Basie Boogie.mp3");
        Assert.True(File.Exists(here));
        Assert.Contains(app.ViewModel.Decks, d => d.Track?.Path == here);
        Assert.Contains(app.Database.GetServerChoices(app.ViewModel.Servers.Items[0].Server.Id),
            c => c.Kind == ServerChoiceKind.Song && c.RemoteId == "s1");   // kept from now on
    }

    [AvaloniaFact]
    public async Task Adding_to_the_queue_brings_the_song_here_first()
    {
        var (app, _, _) = await WithServer();
        using var _ = app;
        ShowServer(app);
        Track blues = app.ViewModel.Library.Results.Single(t => t.Title == "Lucky Rock Blues");

        app.ViewModel.AddToQueue([blues]);
        Assert.True(Assert.Single(app.ViewModel.Queue.Items).IsWaiting);   // in its place at once, downloading
        WaitFor(() => !app.ViewModel.Queue.Items[0].IsWaiting, "the song, here");

        Track queued = app.ViewModel.Queue.Items[0].Track;
        Assert.False(queued.IsOnServerOnly);
        Assert.True(File.Exists(queued.Path));
    }

    [AvaloniaFact]
    public async Task A_song_still_on_the_server_previews_in_the_headphones()
    {
        var (app, _, _) = await WithServer();
        using var _ = app;
        ShowServer(app);
        Track boogie = app.ViewModel.Library.Results.Single(t => t.Title == "Basie Boogie");

        app.ViewModel.Preview!.Toggle(boogie);
        WaitFor(() => app.ViewModel.Preview.Track is not null, "the preview");

        Assert.StartsWith(app.ViewModel.Servers.PreviewFolder, app.ViewModel.Preview.Track!.Path);
        Assert.True(app.ViewModel.Servers.IsPreviewing(boogie));
        app.ViewModel.Preview.Toggle(boogie);   // a second tap stops it
        Assert.Null(app.ViewModel.Preview.Track);
        Assert.Empty(app.Database.GetServerSongs(app.ViewModel.Servers.Items[0].Server.Id));
    }

    [AvaloniaFact]
    public async Task Download_to_this_laptop_makes_the_rows_the_librarys_own()
    {
        var (app, _, _) = await WithServer();
        using var _ = app;
        ShowServer(app);
        var library = app.ViewModel.Library;
        var basie = library.Results.Where(t => t.Artist == "Count Basie").ToList();

        Finish(app.ViewModel.Servers.DownloadAlbumsAsync([basie[0]]));
        WaitFor(() => library.Results.Count(t => !t.IsOnServerOnly) == 2, "the album's songs, here");

        Assert.All(library.Results.Where(t => t.Artist == "Count Basie"), t => Assert.False(t.IsOnServerOnly));
        Assert.Contains(app.Database.GetServerChoices(app.ViewModel.Servers.Items[0].Server.Id), c => c.Kind == ServerChoiceKind.Album);
    }

    [AvaloniaFact]
    public async Task A_library_search_says_what_the_server_has_that_isnt_here()
    {
        var (app, _, _) = await WithServer();
        using var _ = app;
        var library = app.ViewModel.Library;

        library.SearchText = "basie";
        WaitFor(() => library.HasAlsoOn, "the Also on line");

        Assert.Equal("Also: 2 on synology", library.AlsoOnText);
        library.ShowAlsoOnCommand.Execute(null);
        WaitFor(() => library.Results.Count == 2 && library.Results.All(t => t.IsOnServerOnly), "the server's songs");
        Assert.True(library.SelectedFolder?.IsServer);
        Assert.Equal("basie", library.SearchText);
    }
}
