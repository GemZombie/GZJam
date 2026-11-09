using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using GZJam.App.Controls;
using GZJam.App.ViewModels;
using GZJam.App.Views;
using GZJam.Library;
using GZJam.Library.Servers;
using GZJam.Testing;

namespace GZJam.App.Tests;

/// <summary>Songs from the queue sent to a folder of My music (#335): asked first, and they end up there wherever they came from.</summary>
public sealed class QueueToFolderAppTests
{
    private sealed record Setup(AppHarness App, string Lindy, string Desktop, string Stick, List<FilePlace> Sticks) : IDisposable
    {
        public MainWindowViewModel Vm => App.ViewModel;
        public QueueViewModel Queue => App.ViewModel.Queue;
        public void Dispose() => App.Dispose();
    }

    /// <summary>An empty Lindy folder in My music, a Desktop with a song, and a stick with a song, not yet plugged in.</summary>
    private static Setup Make()
    {
        var app = new AppHarness();
        string lindy = Path.Combine(app.MusicPath, "Lindy");
        Directory.CreateDirectory(lindy);
        string desktop = Path.Combine(app.WorkDirectory, "Desktop");
        TestMp3.Write(Path.Combine(desktop, "Voice memo.mp3"), new TestMp3.Tags(Title: "Voice memo"));
        string stick = Path.Combine(app.WorkDirectory, "Stick");
        TestMp3.Write(Path.Combine(stick, "Gig", "At Last.mp3"), new TestMp3.Tags(Title: "At Last"));
        var sticks = new List<FilePlace>();
        var files = app.ViewModel.Files;
        files.Places = () => [new FilePlace("Desktop", desktop, FilesKind.Place)];
        files.Drives = () => [];
        files.Sticks = () => sticks.ToList();
        files.ReadInFolder = Path.Combine(app.WorkDirectory, "Read in");
        app.Settings.ServerKeepFolder = app.MusicPath;
        app.Settings.OrganizeMoveWarned = true;
        AppHarness.Settle(100);
        return new Setup(app, lindy, desktop, stick, sticks);
    }

    private static void Finish(Task task)
    {
        AppHarness.WaitFor(() => task.IsCompleted, TimeSpan.FromSeconds(20));
        if (task.Exception?.InnerException is { } failure)
            throw failure;
        AppHarness.Settle(150);
    }

    /// <summary>The chooser, answered by <paramref name="answer"/> (by default, yes): what it was opened with is kept for looking at.</summary>
    private static Func<AddPlaceViewModel?> Answer(Setup s, Func<AddPlaceViewModel, AddPlace?>? answer = null)
    {
        AddPlaceViewModel? asked = null;
        s.Vm.ChooseAddPlaceAsync = model =>
        {
            asked = model;
            return Task.FromResult(answer is null ? model.Result() : answer(model));
        };
        return () => asked;
    }

    private static Track Queued(Setup s, Track song)
    {
        s.Vm.AddToQueue([song]);
        AppHarness.WaitFor(() => s.Queue.Items.Count == 1);
        return s.Queue.Items[0].Track;
    }

    private static Point Center(Setup s, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), s.App.Window)!.Value;

    private static FolderRow MusicFolderRow(Setup s) =>
        s.App.Window.GetVisualDescendants().OfType<FolderRow>().First(r => r.DataContext is FolderNodeViewModel { IsRoot: true, IsFolder: true });

    private static ListBoxItem MyMusicRow(Setup s) =>
        s.App.Window.GetVisualDescendants().OfType<ListBoxItem>().First(i => i.DataContext is SourceViewModel { IsMyMusic: true });

    [AvaloniaFact]
    public void A_song_in_my_music_moves_with_everything_and_undo_puts_it_back()
    {
        using var s = Make();
        Track song = s.App.Library.Results[0];
        string from = song.Path, to = Path.Combine(s.Lindy, Path.GetFileName(from));
        s.Vm.SetRating([song], Rating.Loved);
        PlaylistInfo playlist = s.Vm.CreatePlaylist("Saturday", [song])!;
        Queued(s, song);
        var asked = Answer(s);

        Finish(s.Vm.SendToFolderAsync([song], s.Lindy));

        Assert.Equal("Move", asked()!.Action);
        Assert.StartsWith("Move \"", asked()!.Heading);
        Assert.Contains("stays in My music, in a new place", asked()!.Details);
        Assert.Equal(s.Lindy, asked()!.Folder);   // the folder it was dropped on
        Assert.False(asked()!.CanGoBackToUsual);
        Assert.True(File.Exists(to));
        Assert.False(File.Exists(from));
        AppHarness.WaitFor(() => s.Queue.Items.Single().Track.Path == to);
        AppHarness.WaitFor(() => s.App.Database.FindTrack(to) is not null);
        Assert.Equal(Rating.Loved, s.App.Database.GetMarks(to).Rating);
        Assert.Equal(to, Assert.Single(s.App.Database.GetPlaylistEntries(playlist.Id)).Path);

        s.Vm.UndoNotice();

        AppHarness.WaitFor(() => File.Exists(from) && !File.Exists(to));
        AppHarness.WaitFor(() => s.Queue.Items.Single().Track.Path == from);
        Assert.Equal(from, Assert.Single(s.App.Database.GetPlaylistEntries(playlist.Id)).Path);
    }

    [AvaloniaFact]
    public void A_song_elsewhere_on_the_computer_moves_there_and_joins_my_music()
    {
        using var s = Make();
        string from = Path.Combine(s.Desktop, "Voice memo.mp3"), to = Path.Combine(s.Lindy, "Voice memo.mp3");
        Track song = Queued(s, s.App.Database.TrackForFile(from));
        var asked = Answer(s);

        Finish(s.Vm.SendToFolderAsync([song], s.Lindy));

        Assert.Contains("joins My music", asked()!.Details);
        Assert.False(File.Exists(from));
        AppHarness.WaitFor(() => s.App.Database.FindTrack(to) is not null);
        AppHarness.WaitFor(() => s.Queue.Items.Single().Track.Path == to);
    }

    [AvaloniaFact]
    public void A_song_from_a_stick_is_copied_and_the_stick_keeps_it()
    {
        using var s = Make();
        string onStick = Path.Combine(s.Stick, "Gig", "At Last.mp3"), to = Path.Combine(s.Lindy, "At Last.mp3");
        s.Sticks.Add(new FilePlace("SANDISK (E:)", s.Stick, FilesKind.Stick));
        s.Vm.Files.CheckSticks();
        AppHarness.Settle(100);
        Track read = AppHarness.Await(s.Vm.Files.ReadInAsync([s.App.Database.TrackForFile(onStick)]))[0];
        s.Vm.SetRating([read], Rating.Loved);
        Track song = Queued(s, read);
        var asked = Answer(s);
        Assert.Equal("Add to my music…", s.Vm.ToFolderMenuText([song]));

        Finish(s.Vm.SendToFolderAsync([song], s.Lindy));

        Assert.Equal("Add", asked()!.Action);
        Assert.StartsWith("Add \"At Last\"", asked()!.Heading);
        Assert.Contains("the USB stick keeps its song", asked()!.Details);
        Assert.True(File.Exists(onStick));
        Assert.True(File.Exists(to));
        AppHarness.WaitFor(() => s.App.Database.FindTrack(to) is not null);
        AppHarness.WaitFor(() => s.Queue.Items.Single().Track.Path == to);
        Assert.Equal(Rating.Loved, s.App.Database.GetMarks(to).Rating);
    }

    private static async Task<(FakeNavidrome Fake, ServerItemViewModel Card)> WithServer(Setup s)
    {
        var fake = new FakeNavidrome();
        foreach (string id in (string[])["s1", "s2", "s3", "s4"])
        {
            string mp3 = Path.Combine(s.App.WorkDirectory, $"{id}.mp3");
            TestMp3.Write(mp3, new TestMp3.Tags(Title: id));
            fake.Files[id] = File.ReadAllBytes(mp3);
        }
        s.App.Settings.ServerDownloadsFolder = Path.Combine(s.App.WorkDirectory, "GZJam from servers");
        var servers = s.Vm.Servers;
        servers.PreviewFolder = Path.Combine(s.App.WorkDirectory, "previews");
        servers.CheckServer = (_, _, _, _) => Task.FromResult(new ServerCheck(ServerCheckOutcome.Connected, "navidrome", "0.64.2", true, true, 4));
        servers.ClientFor = (server, password) => new SubsonicClient(new HttpClient(fake), server.Address, server.UserName, password) { RetryAfter = TimeSpan.Zero };
        Assert.True((await servers.SaveAsync(null, new ServerEdit("synology", "synology:4533", "gzjam", "sesame"))).Saved);
        var card = Assert.Single(servers.Items);
        AppHarness.WaitFor(() => s.App.Database.GetServerCatalogCount(card.Server.Id) == 4, TimeSpan.FromSeconds(20));
        return (fake, card);
    }

    private static Track NotHere(Setup s, ServerItemViewModel card, string id) =>
        new ServerTrack(card.Server.Id, "synology", s.App.Database.GetServerCatalogSongs(card.Server.Id, [id]).Single()).ToTrack();

    [AvaloniaFact]
    public async Task A_servers_song_not_here_yet_comes_down_into_the_folder()
    {
        using var s = Make();
        var (_, card) = await WithServer(s);
        Track song = NotHere(s, card, "s4");
        var asked = Answer(s);

        Finish(s.Vm.SendToFolderAsync([song], s.Lindy));

        Assert.Equal("Add", asked()!.Action);
        Assert.Contains("the server's song isn't touched", asked()!.Details);
        string to = Path.Combine(s.Lindy, "05 - Li'l Darlin'.mp3");
        AppHarness.WaitFor(() => File.Exists(to), TimeSpan.FromSeconds(20));
        AppHarness.WaitFor(() => s.App.Database.FindTrack(to) is not null);
    }

    [AvaloniaFact]
    public async Task A_servers_song_in_the_queue_is_followed_there()
    {
        using var s = Make();
        var (_, card) = await WithServer(s);
        s.Vm.AddToQueue([NotHere(s, card, "s4")]);
        AppHarness.WaitFor(() => s.Queue.Items.Count == 1 && !s.Queue.Items[0].Track.IsOnServerOnly, TimeSpan.FromSeconds(20));
        Answer(s);

        Finish(s.Vm.SendToFolderAsync([s.Queue.Items[0].Track], s.Lindy));

        string to = Path.Combine(s.Lindy, "05 - Li'l Darlin'.mp3");
        AppHarness.WaitFor(() => File.Exists(to), TimeSpan.FromSeconds(20));
        AppHarness.WaitFor(() => s.Queue.Items.Single().Track.Path == to);
    }

    [AvaloniaFact]
    public void Cancel_changes_nothing()
    {
        using var s = Make();
        Track song = s.App.Library.Results[0];
        Queued(s, song);
        var asked = Answer(s, _ => null);

        Finish(s.Vm.SendToFolderAsync([song], s.Lindy));

        Assert.NotNull(asked());
        Assert.True(File.Exists(song.Path));
        Assert.Empty(Directory.GetFiles(s.Lindy));
        Assert.Equal(song.Path, s.Queue.Items.Single().Track.Path);
    }

    [AvaloniaFact]
    public async Task A_song_on_a_deck_stays_put()
    {
        using var s = Make();
        Track song = s.App.Library.Results[0];
        Assert.True(await s.Vm.LoadToDeckAsync(s.Vm.Decks[0], song));
        AppHarness.Settle(100);
        var asked = Answer(s);

        Finish(s.Vm.SendToFolderAsync([song], s.Lindy));

        Assert.Null(asked());
        Assert.True(File.Exists(song.Path));
        Assert.Empty(Directory.GetFiles(s.Lindy));
        Assert.Contains("off its deck", s.Vm.Notice);
        Assert.False(s.Vm.CanSendToFolder([song]));
    }

    [AvaloniaFact]
    public void A_song_already_in_the_folder_is_not_renamed()
    {
        using var s = Make();
        Track song = s.App.Library.Results[0];
        string folder = Path.GetDirectoryName(song.Path)!;
        int before = Directory.GetFiles(folder).Length;
        Answer(s);

        Finish(s.Vm.SendToFolderAsync([song], folder));

        Assert.True(File.Exists(song.Path));
        Assert.Equal(before, Directory.GetFiles(folder).Length);
        Assert.Contains("already", s.Vm.Notice);
    }

    [AvaloniaFact]
    public void The_first_move_warns_once_as_organize_does()
    {
        using var s = Make();
        s.App.Settings.OrganizeMoveWarned = false;
        int warned = 0;
        s.Vm.ConfirmAsync = (_, _, _) =>
        {
            warned++;
            return Task.FromResult(true);
        };
        Answer(s);
        var songs = s.App.Library.Results.Take(2).ToList();

        Finish(s.Vm.SendToFolderAsync([songs[0]], s.Lindy));
        Finish(s.Vm.SendToFolderAsync([songs[1]], s.Lindy));

        Assert.Equal(1, warned);
        Assert.Equal(2, Directory.GetFiles(s.Lindy).Length);
    }

    [AvaloniaFact]
    public void Several_songs_go_together()
    {
        using var s = Make();
        var songs = s.App.Library.Results.Take(3).ToList();
        var asked = Answer(s);

        Finish(s.Vm.SendToFolderAsync(songs, s.Lindy));

        Assert.Equal("Move 3 songs here?", asked()!.Heading);
        Assert.Equal(3, Directory.GetFiles(s.Lindy).Length);
    }

    [AvaloniaFact]
    public void Dropped_on_a_folder_in_the_tree_it_asks_with_that_folder_chosen_and_says_so_on_the_way()
    {
        using var s = Make();
        Track song = s.App.Library.Results[0];
        Track queued = Queued(s, song);
        var asked = Answer(s);
        FolderRow row = MusicFolderRow(s);
        var drag = new MainWindow.TrackDrag([queued], [s.Queue.Items[0]]);
        string to = Path.Combine(s.App.MusicPath, Path.GetFileName(song.Path));

        s.App.DragTracks(drag, Center(s, row), release: false);
        Assert.Equal($"Move to {s.App.Library.Roots.First(r => r.IsFolder).Name}…", s.App.Window.DragHint);
        Assert.Contains("drop-target", row.Classes);
        s.App.Window.EndDrag();
        Assert.DoesNotContain("drop-target", row.Classes);

        s.App.DragTracks(drag, Center(s, row));

        AppHarness.WaitFor(() => File.Exists(to));
        Assert.Equal(s.App.MusicPath, asked()!.Folder);
        AppHarness.WaitFor(() => s.Queue.Items.Single().Track.Path == to);
    }

    [AvaloniaFact]
    public void Dropped_on_my_music_it_opens_on_the_usual_place()
    {
        using var s = Make();
        Track queued = Queued(s, s.App.Library.Results[0]);
        var asked = Answer(s, _ => null);

        s.App.DragTracks(new MainWindow.TrackDrag([queued], [s.Queue.Items[0]]), Center(s, MyMusicRow(s)));

        AppHarness.WaitFor(() => asked() is not null);
        Assert.True(asked()!.IsUsual);
        Assert.Equal(Path.Combine(s.App.MusicPath, "Added"), asked()!.Folder);
    }

    [AvaloniaFact]
    public void A_drag_from_the_song_list_never_goes_to_a_folder()
    {
        using var s = Make();
        Track song = s.App.Library.Results[0];
        var asked = Answer(s);

        s.App.DragTracks(new MainWindow.TrackDrag([song], null), Center(s, MusicFolderRow(s)));

        Assert.Null(asked());
        Assert.True(File.Exists(song.Path));
    }
}
