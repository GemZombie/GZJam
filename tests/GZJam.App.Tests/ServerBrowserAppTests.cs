using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.App.Views;
using GZJam.Library;
using GZJam.Library.Servers;
using GZJam.Testing;

namespace GZJam.App.Tests;

/// <summary>Browsing a server (#284): search it, open its albums, preview in the headphones, download to the laptop.</summary>
public sealed class ServerBrowserAppTests
{
    private static async Task<(AppHarness App, FakeNavidrome Server, ServerItemViewModel Card)> WithServer()
    {
        var app = new AppHarness();
        var fake = new FakeNavidrome();
        app.Settings.ServerDownloadsFolder = Path.Combine(app.WorkDirectory, "GZJam from servers");
        var servers = app.ViewModel.Servers;
        servers.PreviewFolder = Path.Combine(app.WorkDirectory, "previews");
        servers.CheckServer = (_, _, _, _) => Task.FromResult(new ServerCheck(ServerCheckOutcome.Connected, "navidrome", "0.64.2", true, true, 4));
        servers.ClientFor = (server, password) => new SubsonicClient(new HttpClient(fake), server.Address, server.UserName, password) { RetryAfter = TimeSpan.Zero };
        Assert.True((await servers.SaveAsync(null, new ServerEdit("synology", "synology:4533", "gzjam", "sesame"))).Saved);
        return (app, fake, Assert.Single(servers.Items));
    }

    /// <summary>Runs <paramref name="task"/> to its end, keeping the UI going (it does its work on the thread pool).</summary>
    private static void Finish(Task task)
    {
        var until = DateTime.UtcNow.AddSeconds(20);
        while (!task.IsCompleted && DateTime.UtcNow < until)
            AppHarness.Settle(30);
        Assert.True(task.IsCompleted, "didn't finish");
        if (task.Exception?.InnerException is { } failure)
            throw failure;
        AppHarness.Settle(100);
    }

    private static ServerBrowserViewModel Browser(AppHarness app, ServerItemViewModel card) => new(app.ViewModel.Servers, card);

    /// <summary>Types a search, as a DJ would, and waits for it.</summary>
    private static void Search(ServerBrowserViewModel browser, string text)
    {
        browser.SearchDelay = TimeSpan.FromMilliseconds(20);
        browser.SearchText = text;
        var until = DateTime.UtcNow.AddSeconds(10);
        while (browser.Heading != $"Search: {text}" && DateTime.UtcNow < until)
            AppHarness.Settle(30);
        AppHarness.Settle(100);
        Assert.Equal($"Search: {text}", browser.Heading);
    }

    [AvaloniaFact]
    public async Task It_opens_on_whats_new_then_an_album_shows_its_songs_and_BACK_returns()
    {
        var (app, _, card) = await WithServer();
        using var _ = app;
        var browser = Browser(app, card);

        Finish(browser.ShowHomeAsync());
        Assert.Equal("Recently added", browser.Heading);
        Assert.Equal(["Cute", "Ella Swings", "Mother of the Blues"], browser.Rows.Select(r => r.Title));
        Assert.All(browser.Rows, r => Assert.Equal("ALBUM", r.KindText));
        Assert.Equal("Count Basie · 1958 · 2 songs · 6:00", browser.Rows[0].Detail);
        Assert.False(browser.CanGoBack);

        Finish(browser.OpenAsync(browser.Rows[0]));
        Assert.Equal("Cute", browser.Heading);
        Assert.Equal(["Basie Boogie", "Li'l Darlin'"], browser.Rows.Select(r => r.Title));
        Assert.True(browser.CanGoBack);
        Assert.True(browser.CanDownloadAll);
        Assert.Equal("DOWNLOAD THE ALBUM", browser.DownloadAllText);

        browser.GoBack();
        Assert.Equal("Recently added", browser.Heading);
        Assert.Equal(3, browser.Rows.Count);
        Assert.False(browser.CanDownloadAll);
    }

    [AvaloniaFact]
    public async Task Search_finds_the_artist_their_albums_and_songs()
    {
        var (app, _, card) = await WithServer();
        using var _ = app;
        var browser = Browser(app, card);

        Search(browser, "basie");

        Assert.Equal("Search: basie", browser.Heading);
        Assert.Equal([("ARTIST", "Count Basie"), ("ALBUM", "Cute"), ("", "Basie Boogie"), ("", "Li'l Darlin'")],
            browser.Rows.Select(r => (r.KindText, r.Title)));

        Finish(browser.OpenAsync(browser.Rows[0]));   // the artist: their albums
        Assert.Equal("Count Basie", browser.Heading);
        Assert.Equal("Cute", Assert.Single(browser.Rows).Title);
        Assert.Equal("DOWNLOAD THE ARTIST", browser.DownloadAllText);
    }

    [AvaloniaFact]
    public async Task DOWNLOAD_THE_ALBUM_brings_its_songs_to_the_laptop_and_marks_them()
    {
        var (app, _, card) = await WithServer();
        using var _ = app;
        var browser = Browser(app, card);
        Finish(browser.ShowHomeAsync());
        Finish(browser.OpenAsync(browser.Rows[0]));

        Finish(browser.DownloadAllAsync());

        string folder = Path.Combine(app.Settings.ServerDownloadsFolder!, "synology", "Jazz & Swing", "Count Basie", "Cute");
        Assert.True(File.Exists(Path.Combine(folder, "03 - Basie Boogie.mp3")));
        Assert.True(File.Exists(Path.Combine(folder, "05 - Li'l Darlin'.mp3")));
        Assert.All(browser.Rows, r => Assert.Equal("On this laptop", r.HereText));
        Assert.False(browser.StatusProblem);
        var chosen = Assert.Single(app.Database.GetServerChoices(card.Server.Id));
        Assert.Equal((ServerChoiceKind.Album, "al-cute"), (chosen.Kind, chosen.RemoteId));
        Assert.Contains("1 album", card.KeptText);
    }

    [AvaloniaFact]
    public async Task Downloading_selected_songs_keeps_just_those()
    {
        var (app, _, card) = await WithServer();
        using var _ = app;
        var browser = Browser(app, card);
        Search(browser, "blues");
        BrowseRow song = Assert.Single(browser.Rows, r => r.Kind == BrowseRowKind.Song);

        Finish(browser.DownloadAsync([song]));

        Assert.True(song.IsHere);
        Assert.Equal("Lucky Rock Blues (Ma Rainey)", Assert.Single(app.Database.GetServerChoices(card.Server.Id)).Name);
        Assert.Single(app.Database.GetServerSongs(card.Server.Id));
    }

    [AvaloniaFact]
    public async Task A_song_not_on_the_laptop_previews_in_the_headphones_without_joining_the_library()
    {
        var (app, fake, card) = await WithServer();
        using var _ = app;
        string mp3 = Path.Combine(app.WorkDirectory, "real.mp3");
        TestMp3.Write(mp3, new TestMp3.Tags(Title: "Basie Boogie", Artist: "Count Basie"));
        fake.Files["s1"] = File.ReadAllBytes(mp3);
        var browser = Browser(app, card);
        Search(browser, "boogie");
        BrowseRow song = Assert.Single(browser.Rows);

        Finish(browser.PreviewAsync(song));

        var preview = app.ViewModel.Preview!;
        Assert.NotNull(preview.Track);
        Assert.Equal("Basie Boogie", preview.Track.Title);
        Assert.StartsWith(app.ViewModel.Servers.PreviewFolder, preview.Track.Path);
        Assert.StartsWith("In the headphones", browser.Status);
        Assert.Empty(app.Database.GetServerSongs(card.Server.Id));   // not downloaded to keep
        Assert.False(Directory.Exists(app.Settings.ServerDownloadsFolder));
    }

    [AvaloniaFact]
    public async Task A_song_on_the_laptop_previews_from_its_copy_here()
    {
        var (app, fake, card) = await WithServer();
        using var _ = app;
        string mp3 = Path.Combine(app.WorkDirectory, "real.mp3");
        TestMp3.Write(mp3, new TestMp3.Tags(Title: "Basie Boogie", Artist: "Count Basie"));
        fake.Files["s1"] = File.ReadAllBytes(mp3);
        var browser = Browser(app, card);
        Search(browser, "boogie");
        Finish(browser.DownloadAsync([browser.Rows[0]]));
        int downloads = fake.Downloads;

        Finish(browser.PreviewAsync(browser.Rows[0]));

        Assert.Equal(browser.Rows[0].LocalPath, app.ViewModel.Preview!.Track?.Path);
        Assert.Equal(downloads, fake.Downloads);
    }

    [AvaloniaFact]
    public async Task The_librarys_Server_link_shows_once_theres_a_server_and_opens_its_browser()
    {
        using (var plain = new AppHarness())
            Assert.False(plain.Window.FindControl<Button>("BrowseServerButton")!.IsVisible);

        var (app, _, card) = await WithServer();
        using var _ = app;
        AppHarness.Settle(50);
        Assert.True(app.Window.FindControl<Button>("BrowseServerButton")!.IsVisible);

        var window = ServerBrowserWindow.Show(app.Window, app.ViewModel.Servers, card);
        AppHarness.Settle(300);
        Assert.Same(window, ServerBrowserWindow.Show(app.Window, app.ViewModel.Servers, card));   // one per server
        Assert.Equal("GZJam · synology", window.Title);
        Assert.Equal(3, window.FindControl<ListBox>("RowList")!.ItemCount);
        window.Close();
    }
}
