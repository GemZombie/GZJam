using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using GZJam.App.Controls;
using GZJam.Library;
using GZJam.Testing;

namespace GZJam.App.Tests;

/// <summary>
/// Where each song is from (#318), in the lists that mix sources: the queue, History and
/// playlists, a row each with its source's icon, always.
/// </summary>
public sealed class SourceIconsAppTests
{
    /// <summary>A song on the Desktop (the computer's files), outside the music folders.</summary>
    private static Track DesktopSong(AppHarness app)
    {
        string path = Path.Combine(app.WorkDirectory, "Desktop", "First dance.mp3");
        TestMp3.Write(path, new TestMp3.Tags(Title: "First dance"));
        return app.Database.TrackForFile(path);
    }

    [AvaloniaFact]
    public void Each_song_says_where_its_from()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        Track mine = app.Library.Results[0];
        Assert.Equal(SongSource.MyMusic, vm.SourceOf(mine).Source);
        Assert.Equal(SongSource.Files, vm.SourceOf(DesktopSong(app)).Source);

        string read = Path.Combine(vm.Files.ReadInFolder, "SANDISK (E_)", "Request.mp3");
        TestMp3.Write(read, new TestMp3.Tags(Title: "Request"));
        Assert.Equal(SongSource.Stick, vm.SourceOf(app.Database.TrackForFile(read)).Source);

        // A server's song downloaded for the nights: the server's, even inside the DJ's Music.
        app.Settings.ServerDownloadsFolder = Path.Combine(app.MusicPath, "GZJam from servers");
        string synced = Path.Combine(app.Settings.ServerDownloadsFolder, "synology", "Boogie.mp3");
        TestMp3.Write(synced, new TestMp3.Tags(Title: "Boogie"));
        Assert.Equal(SongSource.MyMusic, vm.SourceOf(app.Database.TrackForFile(synced)).Source);   // no server: plain music
        app.Database.AddServer("synology", "http://synology:4533", "gzjam");
        vm.Servers.Refresh();
        var (source, tip) = vm.SourceOf(app.Database.TrackForFile(synced));
        Assert.Equal((SongSource.Server, "From synology, on this computer"), (source, tip));
    }

    [AvaloniaFact]
    public void The_queue_shows_each_songs_source()
    {
        using var app = new AppHarness();
        app.ViewModel.AddToQueue([app.Library.Results[0], DesktopSong(app)]);
        AppHarness.Settle(200);
        app.Window.UpdateLayout();

        var icons = app.Window.FindControl<ListBox>("QueueList")!.GetVisualDescendants().OfType<SourceIcon>()
            .Where(i => i.IsEffectivelyVisible).OrderBy(i => ((Track)i.Track!).Title == "First dance").ToList();
        Assert.Equal([SongSource.MyMusic, SongSource.Files], icons.Select(i => i.Source!.Value));
        Assert.Equal("From My music", ToolTip.GetTip(icons[0]));
        app.Screenshot("queue-sources");
    }

    [AvaloniaFact]
    public void A_playlist_shows_each_songs_source_and_a_folder_doesnt()
    {
        using var app = new AppHarness();
        var grid = app.Window.FindControl<DataGrid>("TrackGrid")!;
        int Shown() => grid.GetVisualDescendants().OfType<SourceIcon>().Count(i => i.IsEffectivelyVisible);
        app.Window.UpdateLayout();
        Assert.Equal(0, Shown());   // My music: all of it is

        PlaylistInfo playlist = app.ViewModel.CreatePlaylist("Wedding", [app.Library.Results[0], DesktopSong(app)])!;
        app.Library.OpenPlaylist(playlist.Id);
        AppHarness.WaitFor(() => app.Library.Results.Count == 2);
        AppHarness.Settle(200);
        app.Window.UpdateLayout();

        var icons = grid.GetVisualDescendants().OfType<SourceIcon>().Where(i => i.IsEffectivelyVisible).ToList();
        Assert.Equal(2, icons.Count);
        Assert.Equal([SongSource.MyMusic, SongSource.Files], icons.OrderBy(i => i.Track!.Title == "First dance").Select(i => i.Source!.Value));
        app.Screenshot("playlist-sources");
    }
}
