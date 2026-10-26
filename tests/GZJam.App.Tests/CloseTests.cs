using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using GZJam.App.ViewModels;
using GZJam.App.Views;

namespace GZJam.App.Tests;

/// <summary>Closing GZJam asks first while the room is hearing music (issue #28).</summary>
public sealed class CloseTests
{
    /// <summary>Plays a song on Deck 1, with the crossfader on its side (in the room) or not.</summary>
    private static async Task<DeckViewModel> PlayOnDeck1(AppHarness app, bool inTheRoom)
    {
        DeckViewModel deck = app.ViewModel.Deck1!;
        await app.ViewModel.LoadToDeckAsync(deck, app.ToneTrack("Jump Steady"));
        app.ViewModel.Mixer!.Crossfader = inTheRoom ? 0 : 1;
        deck.TogglePlayCommand.Execute(null);
        app.Play(0.2);
        return deck;
    }

    /// <summary>Stands in for the dialog: answers, and remembers what it was told.</summary>
    private static List<string> Answer(AppHarness app, bool close)
    {
        var asked = new List<string>();
        app.ViewModel.ConfirmCloseAsync = playing =>
        {
            asked.Add(playing);
            return Task.FromResult(close);
        };
        return asked;
    }

    [AvaloniaFact]
    public async Task While_the_room_hears_a_deck_closing_asks_and_keep_playing_keeps_it_open()
    {
        using var app = new AppHarness();
        DeckViewModel deck = await PlayOnDeck1(app, inTheRoom: true);
        var asked = Answer(app, close: false);

        app.Window.Close();
        AppHarness.Settle(100);

        Assert.Equal("Deck 1 is playing “Jump Steady” to the room.", Assert.Single(asked));
        Assert.True(app.Window.IsVisible);
        Assert.True(deck.IsPlaying);
    }

    [AvaloniaFact]
    public async Task Saying_close_closes()
    {
        using var app = new AppHarness();
        await PlayOnDeck1(app, inTheRoom: true);
        var asked = Answer(app, close: true);

        app.Window.Close();
        AppHarness.Settle(100);

        Assert.Single(asked);
        Assert.False(app.Window.IsVisible);
    }

    [AvaloniaFact]
    public async Task Music_only_in_the_headphones_or_nothing_playing_closes_straight_away()
    {
        using (var app = new AppHarness())
        {
            var asked = Answer(app, close: false);
            app.Window.Close();
            Assert.Empty(asked);
            Assert.False(app.Window.IsVisible);
        }

        using (var app = new AppHarness())
        {
            DeckViewModel deck = await PlayOnDeck1(app, inTheRoom: false);
            Assert.True(deck.IsPlaying);
            Assert.False(deck.IsOnAir);
            var asked = Answer(app, close: false);
            app.Window.Close();
            Assert.Empty(asked);
            Assert.False(app.Window.IsVisible);
        }
    }

    /// <summary>Some Linux desktops draw no title bar, so Settings has a way to close GZJam of its own (#55).</summary>
    [AvaloniaFact]
    public async Task Quit_in_Settings_closes_and_still_asks_while_the_room_hears_a_deck()
    {
        using (var app = new AppHarness())
        {
            var asked = Answer(app, close: false);
            app.Window.OpenSettings(SettingsSection.Library);
            Quit(app);
            Assert.Empty(asked);
            Assert.False(app.Window.IsVisible);
        }

        using (var app = new AppHarness())
        {
            DeckViewModel deck = await PlayOnDeck1(app, inTheRoom: true);
            var asked = Answer(app, close: false);
            app.Window.OpenSettings();
            Quit(app);
            Assert.Single(asked);
            Assert.True(app.Window.IsVisible);
            Assert.False(app.Window.IsSettingsOpen);   // back to the decks, still playing
            Assert.True(deck.IsPlaying);
        }

        static void Quit(AppHarness app)
        {
            var quit = app.Window.FindControl<SettingsPanel>("SettingsPanel")!.FindControl<Button>("QuitButton")!;
            Assert.True(quit.IsEffectivelyVisible);   // on every section
            quit.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            AppHarness.Settle(100);
        }
    }

    [AvaloniaFact]
    public async Task In_the_question_enter_and_escape_keep_playing()
    {
        using var app = new AppHarness();
        foreach (Key key in new[] { Key.Enter, Key.Escape })
        {
            var dialog = new ConfirmDialog("Stop the music and close GZJam?", "Deck 1 is playing “Jump Steady” to the room.",
                "Close GZJam", cancelText: "Keep playing", cancelIsDefault: true);
            Task<bool> answer = dialog.ShowDialog<bool>(app.Window);
            AppHarness.Settle(100);
            if (key == Key.Enter)
            {
                using var frame = dialog.CaptureRenderedFrame();
                frame?.Save(Path.Combine(AppHarness.ScreenshotDirectory, "close-question.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
            dialog.KeyPress(key, RawInputModifiers.None, key == Key.Enter ? PhysicalKey.Enter : PhysicalKey.Escape, null);
            AppHarness.Settle(100);
            Assert.True(answer.IsCompleted, $"{key} should answer");
            Assert.False(await answer);
        }
    }
}
