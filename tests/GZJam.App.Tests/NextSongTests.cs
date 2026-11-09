using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>
/// Choosing the next song the way a player would: a song bumped off the waiting deck goes back to
/// the queue (#261), Play a playlist (#259), and double-clicking a song (#260).
/// </summary>
public sealed class NextSongTests
{
    /// <summary>Auto-play on with tone songs queued: Deck 1 plays the first, Deck 2 has the second ready.</summary>
    private static (AppHarness App, AutoPlayViewModel Auto, List<Track> Tracks) Playing(int queued, RepeatMode repeat = RepeatMode.Off)
    {
        var app = new AppHarness();
        app.Settings.GapSeconds = 0;
        var tracks = Enumerable.Range(1, queued).Select(i => app.ToneTrack($"Song {i}")).ToList();
        app.ViewModel.Queue.Add(tracks);
        var auto = app.ViewModel.AutoPlay!;
        auto.Repeat = repeat;
        auto.IsOn = true;
        app.Play(0.1);
        Assert.True(app.ViewModel.Deck1!.IsPlaying);
        return (app, auto, tracks);
    }

    private static List<string> Queued(AppHarness app) => app.ViewModel.Queue.Items.Select(i => i.Track.Path).ToList();

    private static void Undo(AppHarness app)
    {
        AppHarness.Settle();
        var undo = app.Window.FindControl<Button>("NoticeUndoButton")!;
        Assert.True(undo.IsEffectivelyVisible);
        undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static PlaylistInfo Playlist(AppHarness app, IReadOnlyList<Track> songs, string name = "Class")
    {
        var playlist = app.ViewModel.CreatePlaylist(name, songs)!;
        AppHarness.Settle(150);
        return playlist;
    }

    // ---- #261: the waiting deck is the front of the queue ----

    [AvaloniaFact]
    public void A_song_replaced_on_the_waiting_deck_goes_back_to_the_top_of_the_queue()
    {
        var (app, _, tracks) = Playing(queued: 3);
        using var _app = app;
        var d2 = app.ViewModel.Deck2!;
        Assert.Equal(tracks[1], d2.Track);
        var other = app.ToneTrack("Other");

        Assert.True(AppHarness.Await(app.ViewModel.LoadToDeckAsync(d2, other)));

        Assert.Equal(other, d2.Track);
        Assert.Equal([tracks[1].Path, tracks[2].Path], Queued(app));
        Assert.Equal("\"Song 2\" went back to the top of the queue", app.ViewModel.Notice);
        Undo(app);
        Assert.Equal([tracks[2].Path], Queued(app));
    }

    [AvaloniaFact]
    public void A_song_from_the_queue_dropped_on_the_waiting_deck_swaps_places_with_the_one_there()
    {
        var (app, _, tracks) = Playing(queued: 4);
        using var _app = app;
        var d2 = app.ViewModel.Deck2!;
        var queue = app.ViewModel.Queue;
        QueueItem fourth = queue.Items[1];   // Song 4

        // As dragging it from the queue does: load, then it leaves the queue.
        Assert.True(AppHarness.Await(app.ViewModel.LoadToDeckAsync(d2, fourth.Track)));
        queue.Loaded([fourth]);

        Assert.Equal(tracks[3], d2.Track);
        Assert.Equal([tracks[1].Path, tracks[2].Path], Queued(app));
    }

    [AvaloniaFact]
    public void With_repeat_the_queue_the_replaced_song_moves_up_rather_than_being_queued_twice()
    {
        var (app, _, tracks) = Playing(queued: 2, RepeatMode.Queue);
        using var _app = app;
        Assert.Equal([tracks[0].Path, tracks[1].Path], Queued(app));   // both went round to the end

        Assert.True(AppHarness.Await(app.ViewModel.LoadToDeckAsync(app.ViewModel.Deck2!, app.ToneTrack("Other"))));

        Assert.Equal([tracks[1].Path, tracks[0].Path], Queued(app));
        Undo(app);
        Assert.Equal([tracks[0].Path, tracks[1].Path], Queued(app));
    }

    [AvaloniaFact]
    public void On_the_crossfaders_side_a_replaced_song_goes_back_with_the_other_deck_in_the_headphones()
    {
        var (app, _, tracks) = Playing(queued: 3);
        using var _app = app;
        var d2 = app.ViewModel.Deck2!;
        app.ViewModel.Mixer!.Crossfader = 1;   // Deck 1 goes on, out of the room
        app.Play(0.1);
        Assert.True(app.ViewModel.Deck1!.IsPlaying);
        Assert.True(d2.IsReady);

        Assert.True(AppHarness.Await(app.ViewModel.LoadToDeckAsync(d2, app.ToneTrack("Other"))));

        Assert.Equal([tracks[1].Path, tracks[2].Path], Queued(app));
        Assert.Equal("\"Song 2\" went back to the top of the queue", app.ViewModel.Notice);
        Undo(app);
        Assert.Equal([tracks[2].Path], Queued(app));
    }

    [AvaloniaFact]
    public void On_the_crossfaders_side_a_replaced_song_goes_back_with_nothing_playing()
    {
        var (app, _, tracks) = Playing(queued: 3);
        using var _app = app;
        var d1 = app.ViewModel.Deck1!;
        var d2 = app.ViewModel.Deck2!;
        app.ViewModel.Mixer!.Crossfader = 1;
        d1.TogglePlayCommand.Execute(null);
        app.Play(0.1);
        Assert.False(d1.IsPlaying);
        Assert.True(d2.IsReady);

        Assert.True(AppHarness.Await(app.ViewModel.LoadToDeckAsync(d2, app.ToneTrack("Other"))));

        Assert.Equal([tracks[1].Path, tracks[2].Path], Queued(app));
    }

    [AvaloniaFact]
    public void A_song_paused_partway_and_replaced_goes_back_to_the_top_of_the_queue()
    {
        var (app, _, tracks) = Playing(queued: 3);
        using var _app = app;
        var d1 = app.ViewModel.Deck1!;
        d1.TogglePlayCommand.Execute(null);
        app.Play(0.1);
        Assert.True(d1.IsReady);

        Assert.True(AppHarness.Await(app.ViewModel.LoadToDeckAsync(d1, app.ToneTrack("Other"))));

        Assert.Equal([tracks[0].Path, tracks[2].Path], Queued(app));
    }

    [AvaloniaFact]
    public void With_auto_play_off_a_song_loaded_over_another_just_replaces_it()
    {
        using var app = new AppHarness();
        var d2 = app.ViewModel.Deck2!;
        AppHarness.Await(app.ViewModel.LoadToDeckAsync(d2, app.ToneTrack("First")));

        AppHarness.Await(app.ViewModel.LoadToDeckAsync(d2, app.ToneTrack("Second")));

        Assert.Empty(app.ViewModel.Queue.Items);
        Assert.Null(app.ViewModel.Notice);
    }

    [AvaloniaFact]
    public void Loading_the_same_song_again_or_onto_the_deck_on_air_sends_nothing_to_the_queue()
    {
        var (app, _, tracks) = Playing(queued: 2);
        using var _app = app;
        app.Settings.LoadOntoPlayingDeck = PlayingDeckLoad.Block;

        AppHarness.Await(app.ViewModel.LoadToDeckAsync(app.ViewModel.Deck2!, tracks[1]));
        Assert.False(AppHarness.Await(app.ViewModel.LoadToDeckAsync(app.ViewModel.Deck1!, app.ToneTrack("Other"))));

        Assert.Empty(app.ViewModel.Queue.Items);
    }

    // ---- #259: Play a playlist ----

    [AvaloniaFact]
    public void Play_replaces_the_queue_and_auto_play_starts_the_playlist()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var songs = app.Library.Results.Take(4).ToList();
        var playlist = Playlist(app, songs);
        var before = new[] { app.ToneTrack("Old 1"), app.ToneTrack("Old 2") };
        vm.Queue.Add(before);

        vm.PlayPlaylist(playlist);

        Assert.True(vm.AutoPlay!.IsOn);
        Assert.Equal(songs[0].Path, vm.Deck1!.Track?.Path);
        Assert.True(vm.Deck1.IsPlaying);
        Assert.Equal(songs[1].Path, vm.Deck2!.Track?.Path);
        Assert.Equal(songs.Skip(2).Select(s => s.Path), Queued(app));
        Assert.Equal("Playing \"Class\": 4 songs", vm.Notice);

        Undo(app);
        Assert.Equal(before.Select(t => t.Path), Queued(app));
        Assert.False(vm.AutoPlay.IsOn);   // as it was
    }

    [AvaloniaFact]
    public void The_song_playing_finishes_and_the_song_ready_next_makes_way_for_the_playlist()
    {
        var (app, auto, tracks) = Playing(queued: 3);
        using var _app = app;
        var (d1, d2) = (app.ViewModel.Deck1!, app.ViewModel.Deck2!);
        var songs = app.Library.Results.Take(3).ToList();
        var playlist = Playlist(app, songs);

        app.ViewModel.PlayPlaylist(playlist);

        Assert.Equal(tracks[0], d1.Track);   // never touched
        Assert.True(d1.IsPlaying);
        Assert.Equal(songs[0].Path, d2.Track?.Path);
        Assert.Equal(songs.Skip(1).Select(s => s.Path), Queued(app));

        Undo(app);
        Assert.Equal(tracks[1], d2.Track);   // back on its deck
        Assert.Equal([tracks[2].Path], Queued(app));
        Assert.True(auto.IsOn);
    }

    [AvaloniaFact]
    public void Play_shuffled_plays_every_song_of_the_playlist_and_says_so()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var songs = app.Library.Results.Take(5).ToList();
        var playlist = Playlist(app, songs);

        vm.PlayPlaylist(playlist, shuffled: true);

        var lined = new[] { vm.Deck1!.Track!.Path, vm.Deck2!.Track!.Path }.Concat(Queued(app));
        Assert.Equal(songs.Select(s => s.Path).Order(), lined.Order());
        Assert.Equal("Playing \"Class\": 5 songs, shuffled", vm.Notice);
    }

    [AvaloniaFact]
    public void Playing_a_playlist_turns_off_repeat_this_song_but_keeps_repeat_the_queue()
    {
        var (app, auto, _) = Playing(queued: 2, RepeatMode.Song);
        using var _app = app;
        var playlist = Playlist(app, app.Library.Results.Take(3).ToList());

        app.ViewModel.PlayPlaylist(playlist);
        Assert.Equal(RepeatMode.Off, auto.Repeat);

        auto.Repeat = RepeatMode.Queue;
        app.ViewModel.PlayPlaylist(playlist);
        Assert.Equal(RepeatMode.Queue, auto.Repeat);
    }

    [AvaloniaFact]
    public void The_playlist_bar_and_menus_offer_play()
    {
        using var app = new AppHarness();
        var songs = app.Library.Results.Take(3).ToList();
        var playlist = Playlist(app, songs);
        app.Library.OpenPlaylist(playlist.Id);
        AppHarness.Settle();

        var play = app.Window.FindControl<Button>("PlaylistPlayButton")!;
        Assert.True(play.IsEffectivelyVisible);
        play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(app.ViewModel.AutoPlay!.IsOn);
        Assert.Equal(songs[0].Path, app.ViewModel.Deck1!.Track?.Path);

        app.Window.FindControl<MenuItem>("PlaylistPlayShuffledItem")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.EndsWith(", shuffled", app.ViewModel.Notice);
    }

    // ---- #260: double-click a song ----

    [AvaloniaFact]
    public void Double_clicking_with_nothing_playing_loads_the_deck_the_room_will_hear()
    {
        using var app = new AppHarness();
        var song = app.Library.Results[0];

        Assert.True(AppHarness.Await(app.ViewModel.PlaySongAsync(song)));

        Assert.Equal(song, app.ViewModel.Deck1!.Track);
        Assert.Null(app.ViewModel.Deck2!.Track);
    }

    [AvaloniaFact]
    public void Double_clicking_while_a_song_plays_to_the_room_loads_the_other_deck_never_the_one_on_air()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var playing = app.ToneTrack("Playing");
        AppHarness.Await(vm.LoadToDeckAsync(vm.Deck1!, playing));
        vm.Deck1!.Start();
        app.Play(0.1);
        Assert.True(vm.Deck1.IsOnAir);

        var song = app.Library.Results[0];
        Assert.True(AppHarness.Await(vm.PlaySongAsync(song)));

        Assert.Equal(playing, vm.Deck1.Track);
        Assert.Equal(song, vm.Deck2!.Track);
    }

    [AvaloniaFact]
    public void With_auto_play_on_a_double_clicked_song_goes_on_the_waiting_deck_and_the_one_there_back_to_the_queue()
    {
        var (app, _, tracks) = Playing(queued: 3);
        using var _app = app;
        var song = app.Library.Results[0];

        Assert.True(AppHarness.Await(app.ViewModel.PlaySongAsync(song)));

        Assert.Equal(tracks[0], app.ViewModel.Deck1!.Track);   // the room's song is never touched
        Assert.Equal(song, app.ViewModel.Deck2!.Track);   // really next
        Assert.Equal([tracks[1].Path, tracks[2].Path], Queued(app));
        Assert.Equal("\"Song 2\" went back to the top of the queue", app.ViewModel.Notice);
    }

    [AvaloniaFact]
    public void With_auto_play_on_and_the_waiting_deck_busy_a_double_clicked_song_is_first_in_the_queue()
    {
        var (app, _, tracks) = Playing(queued: 3);
        using var _app = app;
        var d2 = app.ViewModel.Deck2!;
        d2.Start();   // cued in the headphones, say
        var song = app.Library.Results[0];

        Assert.False(AppHarness.Await(app.ViewModel.PlaySongAsync(song)));

        Assert.Equal(tracks[1], d2.Track);
        Assert.Equal([song.Path, tracks[2].Path], Queued(app));
        Assert.Equal($"\"{song.DisplayTitle}\" is first in the queue", app.ViewModel.Notice);
    }

    [AvaloniaFact]
    public void A_queued_song_double_clicked_loads_and_leaves_the_queue()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var songs = app.Library.Results.Take(3).ToList();
        vm.Queue.Add(songs);

        Assert.True(AppHarness.Await(vm.PlaySongAsync(songs[1], vm.Queue.Items[1])));
        Assert.Equal(songs[1], vm.Deck1!.Track);
        Assert.Equal([songs[0].Path, songs[2].Path], Queued(app));
    }

    [AvaloniaFact]
    public void With_auto_play_on_a_queued_song_double_clicked_swaps_places_with_the_waiting_one()
    {
        var (app, _, tracks) = Playing(queued: 4);
        using var _app = app;
        var queue = app.ViewModel.Queue;

        Assert.True(AppHarness.Await(app.ViewModel.PlaySongAsync(tracks[3], queue.Items[1])));

        Assert.Equal(tracks[3], app.ViewModel.Deck2!.Track);
        Assert.Equal([tracks[1].Path, tracks[2].Path], Queued(app));
    }

    [AvaloniaFact]
    public void Double_clicking_a_row_in_the_song_list_loads_it()
    {
        using var app = new AppHarness();
        AppHarness.Settle();
        var cell = app.Window.FindControl<DataGrid>("TrackGrid")!.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.DataContext is Track && t.IsEffectivelyVisible && t.Bounds.Width > 20);
        var track = (Track)cell.DataContext!;
        var at = cell.TranslatePoint(new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2), app.Window)!.Value;

        for (int i = 0; i < 2; i++)
        {
            app.Window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
            app.Window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
        }
        AppHarness.Settle(150);

        Assert.Equal(track, app.ViewModel.Deck1!.Track);
    }
}
