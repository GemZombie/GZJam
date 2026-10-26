using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>A song added to the queue or a playlist it's in already: added, said, and Undo takes it back (#136).</summary>
public sealed class RepeatNoticeTests
{
    private static DataGrid Grid(AppHarness app) => app.Window.FindControl<DataGrid>("TrackGrid")!;

    private static List<string> Queued(AppHarness app) => app.ViewModel.Queue.Items.Select(i => i.Track.Path).ToList();

    private static void Undo(AppHarness app)
    {
        var undo = app.Window.FindControl<Button>("NoticeUndoButton")!;
        Assert.True(undo.IsEffectivelyVisible);
        undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    [AvaloniaFact]
    public void Enter_on_a_song_in_the_queue_adds_it_again_says_where_and_Undo_takes_it_back()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var songs = app.Library.Results.Take(5).ToList();
        vm.AddToQueue(songs.Take(5).ToList());
        Assert.Null(vm.Notice);   // nothing repeated, nothing said

        Grid(app).SelectedItem = songs[4];
        Grid(app).Focus();
        app.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

        Assert.Equal(6, vm.Queue.Items.Count);   // still added
        Assert.Equal($"\"{songs[4].DisplayTitle}\" is in the queue already (5th)", vm.Notice);
        AppHarness.Settle();
        Undo(app);
        Assert.Equal(songs.Select(s => s.Path), Queued(app));
        Assert.Null(vm.Notice);
    }

    [AvaloniaFact]
    public void Several_repeats_are_one_notice_and_Undo_only_takes_the_repeats()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var songs = app.Library.Results.Take(4).ToList();
        vm.AddToQueue([songs[0], songs[1]]);

        vm.AddToQueue([songs[0], songs[2], songs[1], songs[3]], index: 1);
        Assert.Equal("2 of these songs are in the queue already", vm.Notice);
        vm.UndoNotice();
        Assert.Equal(new[] { songs[0], songs[2], songs[3], songs[1] }.Select(s => s.Path), Queued(app));

        vm.AddToQueue([songs[2], songs[3]]);
        Assert.Equal("These 2 songs are in the queue already", vm.Notice);
    }

    [AvaloniaFact]
    public void A_playlist_added_to_the_queue_says_which_were_there()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var songs = app.Library.Results.Take(3).ToList();
        vm.AddToQueue([songs[1]]);
        var playlist = vm.CreatePlaylist("Set", songs)!;

        vm.AddPlaylistToQueue(playlist);
        Assert.Equal($"\"{songs[1].DisplayTitle}\" is in the queue already (1st)", vm.Notice);
        vm.UndoNotice();
        Assert.Equal(new[] { songs[1], songs[0], songs[2] }.Select(s => s.Path), Queued(app));
    }

    [AvaloniaFact]
    public void A_song_added_to_a_playlist_it_is_in_says_so_and_Undo_takes_the_new_copy_out()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var songs = app.Library.Results.Take(3).ToList();
        var playlist = vm.CreatePlaylist("Tuesday", songs)!;

        vm.AddToPlaylist(playlist, [songs[2]], index: 0);
        Assert.Equal($"\"{songs[2].DisplayTitle}\" is in Tuesday already (3rd)", vm.Notice);
        Assert.Equal(4, app.Database.GetPlaylistEntries(playlist.Id).Count);

        vm.UndoNotice();
        Assert.Equal(songs.Select(s => s.Path), app.Database.GetPlaylistEntries(playlist.Id).Select(e => e.Path));
    }

    [AvaloniaFact]
    public void A_later_notice_has_no_Undo()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var song = app.Library.Results[0];
        vm.AddToQueue([song]);
        vm.AddToQueue([song]);
        Assert.NotNull(vm.NoticeUndo);

        vm.ShowNotice("Something else");
        Assert.Null(vm.NoticeUndo);
        vm.UndoNotice();   // nothing to undo
        Assert.Equal(2, vm.Queue.Items.Count);
    }

    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(4, "4th")]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(13, "13th")]
    [InlineData(21, "21st")]
    [InlineData(102, "102nd")]
    [InlineData(111, "111th")]
    public void Ordinals(int n, string expected) => Assert.Equal(expected, Format.Ordinal(n));
}
