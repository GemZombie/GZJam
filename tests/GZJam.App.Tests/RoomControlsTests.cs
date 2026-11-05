using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.App.Views;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>
/// The room's song from outside GZJam's window (#262): what the taskbar's buttons (Windows) and
/// the Dock icon's menu (Mac) show and do. The systems' own parts can't run headless; these are
/// the controls they follow.
/// </summary>
public sealed class RoomControlsTests
{
    [AvaloniaFact]
    public void With_nothing_loaded_there_is_nothing_to_play_or_fade()
    {
        using var app = new AppHarness();
        RoomControls room = app.ViewModel.Room;
        app.Play(0.05);

        Assert.False(room.Current.CanPlayPause);
        Assert.Equal("Nothing to play", room.Current.PlayPauseTip);
        Assert.False(room.Current.CanFadeOut);
        Assert.False(room.Current.CanToggleAutoPlay);
        Assert.Equal("Auto-play: add songs to the queue first", room.Current.AutoPlayTip);

        room.ToggleAutoPlay();   // says why, as Ctrl+P does
        Assert.False(app.ViewModel.AutoPlay!.IsOn);
        Assert.Equal("Nothing to auto-play: add songs to the queue first.", app.ViewModel.Notice);
    }

    [AvaloniaFact]
    public void Play_and_pause_act_on_the_song_the_room_hears_with_two_clicks_on_air()
    {
        using var app = new AppHarness();
        app.Settings.OnAirPause = OnAirPause.TapTwice;
        RoomControls room = app.ViewModel.Room;
        DeckViewModel deck = app.ViewModel.Deck1!;
        Track song = app.ToneTrack("Shiny Stockings", seconds: 30);
        Assert.True(AppHarness.Await(app.ViewModel.LoadToDeckAsync(deck, song)));
        int changes = 0;
        room.Changed += () => changes++;
        app.Play(0.05);

        Assert.True(room.Current.CanPlayPause);
        Assert.Equal("Play \"Shiny Stockings\"", room.Current.PlayPauseTip);

        room.PlayPause();
        app.Play(0.2);
        Assert.True(changes > 0);   // the buttons follow
        Assert.True(deck.IsOnAir);
        Assert.True(room.Current.IsPlaying);
        Assert.Equal("Pause \"Shiny Stockings\" (two clicks)", room.Current.PlayPauseTip);

        room.PlayPause();   // a stray click: armed, still playing
        app.Play(0.05);
        Assert.True(deck.IsPlaying);
        Assert.Equal("Click again to pause", room.Current.PlayPauseTip);

        room.PlayPause();
        app.Play(0.05);
        Assert.False(deck.IsPlaying);
        Assert.False(room.Current.IsPlaying);
        Assert.Equal("Play \"Shiny Stockings\"", room.Current.PlayPauseTip);
    }

    [AvaloniaFact]
    public void Fade_out_with_auto_play_on_is_the_next_song_and_auto_play_turns_on_and_off()
    {
        using var app = new AppHarness();
        RoomControls room = app.ViewModel.Room;
        AutoPlayViewModel autoPlay = app.ViewModel.AutoPlay!;
        Track[] songs = [app.ToneTrack("Topsy", seconds: 30), app.ToneTrack("Liza", seconds: 30)];
        app.ViewModel.Queue.Add(songs);
        app.Play(0.05);
        Assert.Equal("Turn auto-play on", room.Current.AutoPlayTip);

        room.ToggleAutoPlay();
        app.Play(0.3);
        Assert.True(autoPlay.IsOn);
        Assert.True(room.Current.IsAutoPlayOn);
        Assert.Equal("Auto-play is on: click to turn it off", room.Current.AutoPlayTip);
        DeckViewModel live = Assert.Single(app.ViewModel.ActiveDecks, d => d.IsOnAir);
        Assert.Equal("Fade out \"Topsy\"; then the next song", room.Current.FadeOutTip);

        room.FadeOut();   // one click, as the deck's FADE OUT with Settings' one tap
        app.Play(0.05);
        Assert.True(live.Deck.IsFading);

        room.ToggleAutoPlay();
        app.Play(0.05);
        Assert.False(autoPlay.IsOn);
    }

    [AvaloniaFact]
    public void The_taskbar_icons_are_drawn_at_any_size()
    {
        foreach (RoomButtonIcons.Icon icon in Enum.GetValues<RoomButtonIcons.Icon>())
        {
            foreach (int size in new[] { 16, 24, 32 })
            {
                byte[] png = RoomButtonIcons.Png(icon, size);
                Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png[..4]);
            }
            File.WriteAllBytes(Path.Combine(AppHarness.ScreenshotDirectory, $"taskbar-{icon}.png"), RoomButtonIcons.Png(icon, 64));
        }
    }
}
