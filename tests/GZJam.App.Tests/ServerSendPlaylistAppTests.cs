using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using GZJam.App.ViewModels;
using GZJam.Library;
using GZJam.Library.Servers;
using GZJam.Testing;

namespace GZJam.App.Tests;

/// <summary>A playlist of the DJ's sent to a server, and a link to it (#303).</summary>
public sealed class ServerSendPlaylistAppTests
{
    private sealed record Setup(AppHarness App, FakeNavidrome Server, ServerItemViewModel Card, Track First, Track Second, Track NotThere) : IDisposable
    {
        public ServersViewModel Servers => App.ViewModel.Servers;
        public void Dispose() => App.Dispose();
    }

    /// <summary>GZJam with synology, on which two of the library's songs are (s2 and s1) and a third isn't.</summary>
    private static async Task<Setup> WithServer()
    {
        var app = new AppHarness();
        var fake = new FakeNavidrome();
        app.Settings.ServerDownloadsFolder = Path.Combine(app.WorkDirectory, "GZJam from servers");
        var servers = app.ViewModel.Servers;
        servers.PreviewFolder = Path.Combine(app.WorkDirectory, "previews");
        servers.CheckServer = (_, _, _, _) => Task.FromResult(new ServerCheck(ServerCheckOutcome.Connected, "navidrome", "0.64.2", true, true, 4));
        servers.ClientFor = (server, password) => new SubsonicClient(new HttpClient(fake), server.Address, server.UserName, password) { RetryAfter = TimeSpan.Zero };
        Assert.True((await servers.SaveAsync(null, new ServerEdit("synology", "synology:4533", "gzjam", "sesame"))).Saved);
        AppHarness.Settle(100);
        var card = Assert.Single(servers.Items);

        var songs = app.Library.Results.Take(3).ToList();
        app.Database.SaveServerSong(new ServerSongCopy(card.Server.Id, "s2", songs[0].Path, null, "/music/a.mp3", OwnCopy: true), DateTime.UtcNow);
        app.Database.SaveServerSong(new ServerSongCopy(card.Server.Id, "s1", songs[1].Path, null, "/music/b.mp3", OwnCopy: true), DateTime.UtcNow);
        app.ViewModel.ConfirmAsync = (_, _, _) => Task.FromResult(true);
        return new Setup(app, fake, card, songs[0], songs[1], songs[2]);
    }

    private static T Finish<T>(Task<T> task)
    {
        AppHarness.WaitFor(() => task.IsCompleted, TimeSpan.FromSeconds(20));
        return task.GetAwaiter().GetResult();
    }

    [AvaloniaFact]
    public async Task A_playlist_goes_up_in_its_order_and_sending_again_updates_the_same_one()
    {
        using var s = await WithServer();
        PlaylistInfo playlist = s.App.ViewModel.CreatePlaylist("Saturday", [s.Second, s.First])!;

        string id = Assert.IsType<string>(Finish(s.Servers.SendPlaylistAsync(s.Card, playlist)));

        Assert.Equal(["s1", "s2"], s.Server.Playlists[id]);
        Assert.Equal("Saturday", s.Server.PlaylistNames[id]);
        Assert.Contains(s.App.Database.GetServerPlaylists(s.Card.Server.Id), p => p.Id == id && p.Name == "Saturday");   // in the server's tree without waiting for a sync

        s.App.ViewModel.AddToPlaylist(playlist, [s.First]);
        AppHarness.Settle(100);
        int before = s.Server.Playlists.Count;
        Assert.Equal(id, Finish(s.Servers.SendPlaylistAsync(s.Card, playlist)));
        Assert.Equal(before, s.Server.Playlists.Count);
        Assert.Equal(3, s.Server.Playlists[id].Length);
    }

    [AvaloniaFact]
    public async Task Songs_the_server_doesnt_have_are_named_first_and_left_out()
    {
        using var s = await WithServer();
        PlaylistInfo playlist = s.App.ViewModel.CreatePlaylist("Saturday", [s.First, s.NotThere])!;
        string? asked = null;
        bool answer = false;
        s.App.ViewModel.ConfirmAsync = (_, message, _) => { asked = message; return Task.FromResult(answer); };

        Assert.Null(Finish(s.Servers.SendPlaylistAsync(s.Card, playlist)));   // said no: nothing goes
        Assert.Contains(s.NotThere.DisplayTitle, asked);
        Assert.DoesNotContain("Saturday", s.Server.PlaylistNames.Values);

        answer = true;
        string id = Assert.IsType<string>(Finish(s.Servers.SendPlaylistAsync(s.Card, playlist)));
        Assert.Equal(["s2"], s.Server.Playlists[id]);
    }

    [AvaloniaFact]
    public async Task A_playlist_deleted_on_the_server_is_made_again_and_says_so()
    {
        using var s = await WithServer();
        PlaylistInfo playlist = s.App.ViewModel.CreatePlaylist("Saturday", [s.First])!;
        string id = Assert.IsType<string>(Finish(s.Servers.SendPlaylistAsync(s.Card, playlist)));
        s.Server.Playlists.Remove(id);

        string again = Assert.IsType<string>(Finish(s.Servers.SendPlaylistAsync(s.Card, playlist)));

        Assert.NotEqual(id, again);
        Assert.Equal(["s2"], s.Server.Playlists[again]);
        Assert.Contains("deleted there", s.App.ViewModel.Notice);
    }

    [AvaloniaFact]
    public async Task Share_a_link_sends_the_playlist_if_needed_and_copies_the_link()
    {
        using var s = await WithServer();
        PlaylistInfo playlist = s.App.ViewModel.CreatePlaylist("Saturday", [s.First])!;
        string? copied = null;
        s.App.ViewModel.CopyTextAsync = text => { copied = text; return Task.CompletedTask; };
        s.App.ViewModel.PromptAsync = (_, _, _, _) => Task.FromResult<string?>("Hot Jam, Saturday");

        string link = Assert.IsType<string>(Finish(s.Servers.SharePlaylistAsync(s.Card, playlist)));

        Assert.Equal(link, copied);
        string id = Assert.Single(s.App.Database.GetServerPlaylists(s.Card.Server.Id), p => p.Name == "Saturday").Id;
        Assert.Equal((id, "Hot Jam, Saturday"), Assert.Single(s.Server.Shares));

        Assert.Equal(link, Finish(s.Servers.SharePlaylistAsync(s.Card, playlist)));
        Assert.Equal(1, s.Server.Calls["createPlaylist"]);   // there already: not sent again
    }

    [AvaloniaFact]
    public async Task With_sharing_off_it_says_how_to_turn_it_on()
    {
        using var s = await WithServer();
        s.Server.SharingOff = true;
        PlaylistInfo playlist = s.App.ViewModel.CreatePlaylist("Saturday", [s.First])!;
        s.App.ViewModel.CopyTextAsync = _ => throw new InvalidOperationException("nothing to copy");
        s.App.ViewModel.PromptAsync = (_, _, _, _) => Task.FromResult<string?>("Saturday");

        Assert.Null(Finish(s.Servers.SharePlaylistAsync(s.Card, playlist)));

        Assert.Contains("EnableSharing", s.App.ViewModel.Notice);
    }

    [AvaloniaFact]
    public async Task A_long_playlist_goes_up_a_part_at_a_time_in_its_order()
    {
        var fake = new FakeNavidrome();
        var client = new SubsonicClient(new HttpClient(fake), "http://synology:4533", "gzjam", "sesame") { RetryAfter = TimeSpan.Zero };
        string[] ids = Enumerable.Range(0, 250).Select(n => $"song{n}").ToArray();

        string id = await client.SendPlaylistAsync(null, "Long", ids);

        Assert.Equal(ids, fake.Playlists[id]);
        Assert.Equal(1, fake.Calls["createPlaylist"]);
        Assert.Equal(2, fake.Calls["updatePlaylist"]);
    }

    [AvaloniaFact]
    public async Task A_playlists_menu_offers_sending_to_the_server_but_not_for_one_kept_from_it()
    {
        using var s = await WithServer();
        s.App.ViewModel.CreatePlaylist("Saturday", [s.First]);
        AppHarness.Settle(150);
        s.App.Window.UpdateLayout();

        var row = s.App.Window.GetVisualDescendants().OfType<Grid>().First(g => g.DataContext is PlaylistInfo { Name: "Saturday" } && g.ContextMenu is not null);
        Point at = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), s.App.Window)!.Value;
        s.App.Window.MouseDown(at, MouseButton.Right, RawInputModifiers.None);
        s.App.Window.MouseUp(at, MouseButton.Right, RawInputModifiers.None);
        AppHarness.Settle(150);

        var items = row.ContextMenu!.Items.OfType<MenuItem>().ToList();
        MenuItem send = items.Single(i => i.Tag is "send");
        Assert.True(row.ContextMenu.IsOpen);
        Assert.True(send.IsVisible);
        Assert.Equal("Send to synology", send.Header);
        Assert.True(items.Single(i => i.Tag is "share").IsVisible);
        row.ContextMenu.Close();
    }
}
