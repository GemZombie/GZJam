using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using GZJam.App.ViewModels;
using GZJam.App.Views;
using GZJam.Library;
using GZJam.Library.Servers;
using GZJam.Testing;

namespace GZJam.App.Tests;

/// <summary>Add to my music, choosing the folder (#324): from a server and from Files.</summary>
public sealed class AddToMyMusicPlaceAppTests
{
    private sealed record Setup(AppHarness App, FakeNavidrome Server, ServerItemViewModel Card, string Lindy, string Stick) : IDisposable
    {
        public ServersViewModel Servers => App.ViewModel.Servers;
        public void Dispose() => App.Dispose();
    }

    /// <summary>GZJam with synology, a folder in My music to choose (Lindy), and a USB stick with a folder of two songs in a folder.</summary>
    private static async Task<Setup> WithPlaces()
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
        app.Settings.ServerKeepFolder = app.MusicPath;
        var servers = app.ViewModel.Servers;
        servers.PreviewFolder = Path.Combine(app.WorkDirectory, "previews");
        servers.CheckServer = (_, _, _, _) => Task.FromResult(new ServerCheck(ServerCheckOutcome.Connected, "navidrome", "0.64.2", true, true, 4));
        servers.ClientFor = (server, password) => new SubsonicClient(new HttpClient(fake), server.Address, server.UserName, password) { RetryAfter = TimeSpan.Zero };
        Assert.True((await servers.SaveAsync(null, new ServerEdit("synology", "synology:4533", "gzjam", "sesame"))).Saved);
        var card = Assert.Single(servers.Items);
        AppHarness.WaitFor(() => app.Database.GetServerCatalogCount(card.Server.Id) == 4, TimeSpan.FromSeconds(20));

        string lindy = Path.Combine(app.MusicPath, "Lindy");
        Directory.CreateDirectory(lindy);
        string stick = Path.Combine(app.WorkDirectory, "Stick");
        TestMp3.Write(Path.Combine(stick, "Gig", "Sets", "At Last.mp3"), new TestMp3.Tags(Title: "At Last"));
        AppHarness.Settle(100);
        return new Setup(app, fake, card, lindy, stick);
    }

    private static void Finish(Task task)
    {
        AppHarness.WaitFor(() => task.IsCompleted, TimeSpan.FromSeconds(20));
        if (task.Exception?.InnerException is { } failure)
            throw failure;
        AppHarness.Settle(150);
    }

    /// <summary>The dialog, answered by <paramref name="answer"/>: what it was opened with is kept for looking at.</summary>
    private static Func<AddPlaceViewModel> Answer(Setup s, Func<AddPlaceViewModel, AddPlace?> answer)
    {
        AddPlaceViewModel? asked = null;
        s.App.ViewModel.ChooseAddPlaceAsync = model =>
        {
            asked = model;
            return Task.FromResult(answer(model));
        };
        return () => asked!;
    }

    private static AddFolderRow Row(AddPlaceViewModel model, string name)
    {
        IEnumerable<AddFolderRow> All(IEnumerable<AddFolderRow> rows) => rows.SelectMany(r => new[] { r }.Concat(All(r.Children)));
        return All(model.Roots).First(r => r.Name == name);
    }

    private static AddPlace Choose(AddPlaceViewModel model, string name, bool keepFolders)
    {
        model.SelectedRow = Row(model, name);
        model.KeepFolders = keepFolders;
        return model.Result();
    }

    private static string Slash(params string[] parts) => Path.Combine(parts);

    [AvaloniaFact]
    public async Task The_usual_place_accepted_is_where_songs_have_always_gone()
    {
        using var s = await WithPlaces();
        var asked = Answer(s, model => model.Result());

        Finish(s.Servers.KeepFolderCopyAsync(s.Card.Server.Id, "Jazz & Swing/Count Basie", "Count Basie"));

        Assert.True(asked().IsUsual);
        Assert.True(File.Exists(Slash(s.App.MusicPath, "Jazz & Swing", "Count Basie", "Cute", "03 - Basie Boogie.mp3")));
        Assert.Null(s.App.Settings.AddToMyMusicLastFolder);
    }

    [AvaloniaFact]
    public async Task A_servers_songs_go_straight_into_the_folder_chosen()
    {
        using var s = await WithPlaces();
        Answer(s, model => Choose(model, "Lindy", keepFolders: false));

        Finish(s.Servers.KeepFolderCopyAsync(s.Card.Server.Id, "Jazz & Swing/Count Basie", "Count Basie"));

        string song = Slash(s.Lindy, "03 - Basie Boogie.mp3");
        Assert.True(File.Exists(song));
        Assert.False(Directory.Exists(Slash(s.App.MusicPath, "Jazz & Swing")));
        AppHarness.WaitFor(() => s.App.Database.FindTrack(song) is not null);
        Assert.Equal(s.Lindy, s.App.Settings.AddToMyMusicLastFolder);
        Assert.Equal(s.App.MusicPath, s.App.Settings.ServerKeepFolder);   // the usual place stays what it was
    }

    [AvaloniaFact]
    public async Task A_servers_songs_can_bring_their_folders_into_the_folder_chosen()
    {
        using var s = await WithPlaces();
        Answer(s, model => Choose(model, "Lindy", keepFolders: true));

        Finish(s.Servers.KeepFolderCopyAsync(s.Card.Server.Id, "Jazz & Swing/Count Basie", "Count Basie"));

        Assert.True(File.Exists(Slash(s.Lindy, "Jazz & Swing", "Count Basie", "Cute", "03 - Basie Boogie.mp3")));
    }

    [AvaloniaFact]
    public async Task Canceling_adds_nothing()
    {
        using var s = await WithPlaces();
        Answer(s, _ => null);

        Finish(s.Servers.KeepFolderCopyAsync(s.Card.Server.Id, "Jazz & Swing/Count Basie", "Count Basie"));

        Assert.Empty(Directory.GetFiles(s.Lindy));
        Assert.False(Directory.Exists(Slash(s.App.MusicPath, "Jazz & Swing")));
    }

    [AvaloniaFact]
    public async Task A_folder_made_on_the_spot_takes_the_songs()
    {
        using var s = await WithPlaces();
        Answer(s, model =>
        {
            model.SelectedRow = Row(model, "Lindy");
            model.MakeFolder("Slow");
            Assert.Equal("Slow", model.SelectedRow!.Name);
            Assert.EndsWith("Lindy › Slow", model.IntoText);
            return model.Result();
        });

        Finish(s.Servers.KeepTracksAsync([NotHere(s, "s4")]));

        Assert.True(File.Exists(Slash(s.Lindy, "Slow", "05 - Li'l Darlin'.mp3")));
    }

    private static Track NotHere(Setup s, string id) =>
        new ServerTrack(s.Card.Server.Id, "synology", s.App.Database.GetServerCatalogSongs(s.Card.Server.Id, [id]).Single()).ToTrack();

    [AvaloniaFact]
    public async Task A_folder_from_a_stick_goes_where_its_chosen_with_what_the_DJ_gave_its_songs()
    {
        using var s = await WithPlaces();
        var files = s.App.ViewModel.Files;
        string song = Slash(s.Stick, "Gig", "Sets", "At Last.mp3");
        Track track = s.App.Database.TrackForFile(song);
        s.App.ViewModel.SetRating([track], Rating.Loved);
        Answer(s, model => Choose(model, "Lindy", keepFolders: true));

        Finish(files.AddToMyMusicAsync([(track, "Sets")], "Gig"));

        string copy = Slash(s.Lindy, "Sets", "At Last.mp3");
        Assert.True(File.Exists(copy));
        Assert.True(File.Exists(song));   // the original stays
        Assert.False(Directory.Exists(Slash(s.App.MusicPath, "Added")));
        Assert.Equal(Rating.Loved, s.App.Database.GetMarks(copy).Rating);
    }

    [AvaloniaFact]
    public async Task Files_usual_place_is_under_Added_and_songs_with_no_folders_have_none_to_keep()
    {
        using var s = await WithPlaces();
        var asked = Answer(s, model => model.Result());
        Track track = s.App.Database.TrackForFile(Slash(s.Stick, "Gig", "Sets", "At Last.mp3"));

        Finish(s.App.ViewModel.Files.AddToMyMusicAsync([(track, "")], "Gig"));

        Assert.False(asked().CanKeepFolders);
        Assert.True(File.Exists(Slash(s.App.MusicPath, "Added", "Gig", "At Last.mp3")));
    }

    [AvaloniaFact]
    public async Task It_opens_on_the_last_folder_chosen_unless_its_gone()
    {
        using var s = await WithPlaces();
        s.App.Settings.AddToMyMusicLastFolder = s.Lindy;
        var asked = Answer(s, _ => null);

        Finish(s.Servers.KeepTracksAsync([NotHere(s, "s4")]));
        Assert.False(asked().IsUsual);
        Assert.Equal("Lindy", asked().SelectedRow!.Name);
        asked().UseUsualPlace();
        Assert.True(asked().IsUsual);

        Directory.Delete(s.Lindy);
        Finish(s.Servers.KeepTracksAsync([NotHere(s, "s4")]));
        Assert.True(asked().IsUsual);
    }

    [AvaloniaFact]
    public async Task The_servers_downloads_inside_my_music_cant_be_chosen()
    {
        using var s = await WithPlaces();
        string downloads = Slash(s.App.MusicPath, "GZJam from servers");
        Directory.CreateDirectory(downloads);
        s.App.Settings.ServerDownloadsFolder = downloads;
        s.App.Settings.AddToMyMusicLastFolder = downloads;
        var asked = Answer(s, _ => null);

        Finish(s.Servers.KeepTracksAsync([NotHere(s, "s4")]));

        Assert.True(asked().IsUsual);
        Assert.Contains(asked().Roots.Single(r => r.Path == s.App.MusicPath).Children, r => r.Name == "Lindy");
        Assert.DoesNotContain(asked().Roots.Single(r => r.Path == s.App.MusicPath).Children, r => r.Name == "GZJam from servers");
    }

    [AvaloniaFact]
    public async Task The_dialog_shows_the_folders_of_My_music()
    {
        using var s = await WithPlaces();
        var model = new AddPlaceViewModel("Add 3 songs to My music?", "They're your own songs from then on: GZJam never removes them.",
            s.App.MusicPath, [s.App.MusicPath], s.Lindy, keepFolders: false, canKeepFolders: true);
        var dialog = new AddToMyMusicDialog(model);
        dialog.Show();
        AppHarness.Settle(200);

        Assert.Contains("Lindy", dialog.FindControl<Avalonia.Controls.TextBlock>("IntoText")!.Text);
        Assert.True(dialog.FindControl<Avalonia.Controls.CheckBox>("KeepFoldersBox")!.IsVisible);
        Directory.CreateDirectory(AppHarness.ScreenshotDirectory);
        dialog.CaptureRenderedFrame()?.Save(Path.Combine(AppHarness.ScreenshotDirectory, "add-to-my-music.png"), new PngBitmapEncoderOptions());
        dialog.Close();
    }

    [AvaloniaFact]
    public async Task From_Files_the_dialog_asks_move_or_copy_and_a_sticks_songs_only_copy()
    {
        using var s = await WithPlaces();
        AddToMyMusicDialog Show(MoveChoice move, string name)
        {
            var model = new AddPlaceViewModel("Add 2 songs to My music?", "In My music they're yours from then on, never removed by GZJam.",
                s.App.MusicPath, [s.App.MusicPath], s.Lindy, keepFolders: false, canKeepFolders: false, move: move);
            var dialog = new AddToMyMusicDialog(model);
            dialog.Show();
            AppHarness.Settle(200);
            Directory.CreateDirectory(AppHarness.ScreenshotDirectory);
            dialog.CaptureRenderedFrame()?.Save(Path.Combine(AppHarness.ScreenshotDirectory, name), new PngBitmapEncoderOptions());
            return dialog;
        }

        var files = Show(new MoveChoice(Remembered: true), "add-to-my-music-move.png");
        var box = files.FindControl<Avalonia.Controls.CheckBox>("MoveBox")!;
        Assert.True(box.IsVisible && box.IsEnabled);
        Assert.Equal(true, box.IsChecked);   // as last time
        files.Close();

        var stick = Show(new MoveChoice(Remembered: true, CantWhy: "Songs on a USB stick are copied"), "add-to-my-music-stick.png");
        box = stick.FindControl<Avalonia.Controls.CheckBox>("MoveBox")!;
        Assert.True(box.IsVisible);
        Assert.False(box.IsEnabled);
        Assert.Equal(false, box.IsChecked);
        stick.Close();

        // From a server: not asked at all.
        var server = new AddToMyMusicDialog(new AddPlaceViewModel("Add 2 songs to My music?", "", s.App.MusicPath, [s.App.MusicPath], null, false, true));
        server.Show();
        AppHarness.Settle(100);
        Assert.False(server.FindControl<Avalonia.Controls.CheckBox>("MoveBox")!.IsVisible);
        server.Close();
    }
}
