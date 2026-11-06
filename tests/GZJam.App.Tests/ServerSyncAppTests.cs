using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using GZJam.App.ViewModels;
using GZJam.App.Views;
using GZJam.Library;
using GZJam.Library.Servers;
using GZJam.Testing;

namespace GZJam.App.Tests;

/// <summary>Settings › Servers, choosing and syncing (#283): a server's playlists and starred songs, onto the laptop.</summary>
public sealed class ServerSyncAppTests
{
    /// <summary>GZJam with the fake Navidrome added as "synology", signed in, downloading into the test's own folder.</summary>
    private static async Task<(AppHarness App, FakeNavidrome Server, ServerItemViewModel Card)> WithServer(bool canDownload = true)
    {
        var app = new AppHarness();
        var fake = new FakeNavidrome();
        app.Settings.ServerDownloadsFolder = Path.Combine(app.WorkDirectory, "GZJam from servers");
        var servers = app.ViewModel.Servers;
        servers.CheckServer = (_, _, _, _) => Task.FromResult(new ServerCheck(ServerCheckOutcome.Connected, "navidrome", "0.64.2", true, canDownload, 4));
        servers.ClientFor = (server, password) => new SubsonicClient(new HttpClient(fake), server.Address, server.UserName, password);
        Assert.True((await servers.SaveAsync(null, new ServerEdit("synology", "synology:4533", "gzjam", "sesame"))).Saved);
        return (app, fake, Assert.Single(servers.Items));
    }

    /// <summary>Waits for the sync running on the thread pool to finish, keeping the UI going.</summary>
    private static void Finish(Task sync)
    {
        var until = DateTime.UtcNow.AddSeconds(20);
        while (!sync.IsCompleted && DateTime.UtcNow < until)
            AppHarness.Settle(30);
        Assert.True(sync.IsCompleted, "the sync didn't finish");
        AppHarness.Settle(200);
    }

    [AvaloniaFact]
    public async Task CHOOSE_lists_whats_starred_and_the_servers_playlists_and_keeps_what_is_ticked()
    {
        var (app, _, card) = await WithServer();
        using var _ = app;
        Assert.Equal("Nothing chosen to keep on this laptop yet: press CHOOSE.", card.KeptText);
        Assert.False(card.CanSync);

        var dialog = new ServerChoicesDialog(app.ViewModel.Servers, card);
        dialog.Show(app.Window);
        AppHarness.Settle(300);

        Assert.Equal(["What's starred", "Friday"], dialog.Choices.Select(c => c.Choice.Kind == ServerChoiceKind.Starred ? "What's starred" : c.Choice.Name));
        Assert.All(dialog.Choices, c => Assert.False(c.Box.IsChecked));
        dialog.Choices[1].Box.IsChecked = true;
        dialog.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        AppHarness.Settle(50);

        Assert.False(dialog.IsVisible);
        var chosen = Assert.Single(app.Database.GetServerChoices(card.Server.Id));
        Assert.Equal((ServerChoiceKind.Playlist, "pl1", "Friday"), (chosen.Kind, chosen.RemoteId, chosen.Name));
        Assert.Equal("Keeping on this laptop: Friday", card.KeptText);
        Assert.True(card.CanSync);
    }

    [AvaloniaFact]
    public async Task SYNC_downloads_into_a_music_folder_and_the_playlist_appears_in_the_library()
    {
        var (app, _, card) = await WithServer();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        servers.SaveChoices(card, [new ServerChoice(ServerChoiceKind.Playlist, "pl1", "Friday")]);

        Finish(card.SyncOrStopAsync());

        string folder = app.Settings.ServerDownloadsFolder!;
        Assert.True(File.Exists(Path.Combine(folder, "synology", "Jazz & Swing", "Count Basie", "Cute", "03 - Basie Boogie.mp3")));
        Assert.Contains(app.Database.GetRoots(), r => r.Path == folder);   // in the library, as a music folder
        PlaylistInfo friday = Assert.Single(app.Database.GetPlaylists(), p => p.Name == "Friday");
        Assert.Equal(3, app.Database.GetPlaylistEntries(friday.Id).Count);
        Assert.Contains(app.ViewModel.Library.Playlists, p => p.Name == "Friday");
        Assert.StartsWith("Synced", card.SyncText);
        Assert.Contains("3 songs here, 3 new", card.SyncText);
        Assert.False(card.SyncProblem);
        Assert.Contains("3 songs downloaded", card.KeptText);
        Assert.StartsWith("3 songs,", servers.DownloadsText);
        Assert.False(servers.CanChangeDownloadsFolder);   // songs are there now
        Assert.Null(servers.SyncStatus);
        Assert.False(card.IsSyncing);
    }

    [AvaloniaFact]
    public async Task A_preview_only_server_has_nothing_to_choose_or_sync()
    {
        var (app, _, card) = await WithServer(canDownload: false);
        using var _ = app;
        Assert.True(card.IsPreviewOnly);
        Assert.False(card.CanChoose);
        Assert.False(card.CanSync);
        Assert.Equal("", card.KeptText);
    }

    [AvaloniaFact]
    public async Task A_sync_that_cant_reach_the_server_says_so()
    {
        var (app, fake, card) = await WithServer();
        using var _ = app;
        app.ViewModel.Servers.SaveChoices(card, [new ServerChoice(ServerChoiceKind.Playlist, "pl1", "Friday")]);
        fake.Down = true;

        Finish(card.SyncOrStopAsync());

        Assert.True(card.SyncProblem);
        Assert.Contains("No connection", card.SyncText);
        Assert.Empty(app.Database.GetPlaylists());
    }

    [AvaloniaFact]
    public async Task Removing_a_server_says_its_downloaded_songs_stay()
    {
        var (app, _, card) = await WithServer();
        using var _ = app;
        var servers = app.ViewModel.Servers;
        servers.SaveChoices(card, [new ServerChoice(ServerChoiceKind.Playlist, "pl1", "Friday")]);
        Finish(card.SyncOrStopAsync());
        string? message = null;
        app.ViewModel.ConfirmAsync = (_, m, _) =>
        {
            message = m;
            return Task.FromResult(true);
        };

        await servers.RemoveAsync(card);

        Assert.Contains("The 3 songs downloaded from it stay on this computer", message);
        Assert.True(File.Exists(Path.Combine(app.Settings.ServerDownloadsFolder!, "synology", "Blues", "Ma Rainey", "Mother of the Blues", "Lucky Rock Blues.mp3")));
        Assert.Single(app.Database.GetPlaylists());   // Friday stays
    }
}
