using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using GZJam.App.ViewModels;
using GZJam.App.Views;

namespace GZJam.App.Tests;

/// <summary>
/// The song on a deck in the headphones, without touching the deck (#378): to compare the next song
/// with another, the DJ no longer cues and plays it, then drags it back to the start.
/// </summary>
public sealed class DeckPreviewTests
{
    private static readonly RawInputModifiers Command = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    private static Button PreviewButton(AppHarness app, int deck) =>
        app.Window.FindControl<DeckView>($"Deck{deck}View")!.FindControl<Button>("PreviewButton")!;

    private static void Click(AppHarness app, Control control)
    {
        Point center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), app.Window)!.Value;
        app.Window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
        app.Window.MouseUp(center, MouseButton.Left, RawInputModifiers.None);
        AppHarness.Settle(100);
    }

    [AvaloniaFact]
    public async Task The_headphones_button_previews_the_deck_song_and_the_deck_stays_where_it_is()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var deck = vm.Deck2!;
        var button = PreviewButton(app, 2);
        Assert.False(button.IsEffectivelyEnabled);   // nothing loaded: nothing to hear (and not shown)

        await vm.LoadToDeckAsync(deck, app.ToneTrack("Next", 60));
        deck.SeekTo(0.25);
        app.Play(0.1);
        TimeSpan at = deck.Deck.Position;
        Assert.True(button.IsEffectivelyEnabled);
        Assert.Equal(1, button.Opacity);
        app.Screenshot("deck-preview-button");

        Click(app, button);
        Assert.Equal(deck.Track!.Path, vm.Preview!.Track?.Path);
        app.Play(0.5);
        Assert.True(deck.IsPreviewed);
        Assert.Contains("active", button.Classes);   // lit while it's the one in the headphones
        Assert.False(deck.IsPlaying);
        Assert.Equal(at, deck.Deck.Position);   // the preview player isn't the deck
        Assert.False(deck.IsCueOn);

        Click(app, button);   // again: stopped, as Space does
        Assert.False(vm.Preview.IsActive);
        app.Play(0.1);
        Assert.False(deck.IsPreviewed);
        Assert.Equal(at, deck.Deck.Position);
    }

    [AvaloniaFact]
    public async Task Ctrl_Alt_Shift_number_previews_and_Space_switches_to_a_library_song_and_back()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var deck = vm.Deck1!;
        await vm.LoadToDeckAsync(deck, app.ToneTrack("Next", 60));
        var grid = app.Window.FindControl<DataGrid>("TrackGrid")!;
        var other = app.Library.Results[3];
        grid.SelectedItem = other;
        grid.Focus();

        app.Window.KeyPress(Key.D1, Command | RawInputModifiers.Alt | RawInputModifiers.Shift, PhysicalKey.Digit1, null);
        Assert.Equal(deck.Track!.Path, vm.Preview!.Track?.Path);

        app.Window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Assert.Equal(other.Path, vm.Preview.Track?.Path);   // the other song, to compare
        app.Play(0.1);
        Assert.False(deck.IsPreviewed);

        app.Window.KeyPress(Key.D1, Command | RawInputModifiers.Alt | RawInputModifiers.Shift, PhysicalKey.Digit1, null);
        Assert.Equal(deck.Track.Path, vm.Preview.Track?.Path);   // and back
        app.Window.KeyPress(Key.D1, Command | RawInputModifiers.Alt | RawInputModifiers.Shift, PhysicalKey.Digit1, null);
        Assert.False(vm.Preview.IsActive);

        Assert.Equal(TimeSpan.Zero, deck.Deck.Position);
        Assert.False(deck.IsPlaying);
    }

    [AvaloniaFact]
    public async Task A_playing_deck_offers_no_preview_CUE_is_the_way_to_hear_it()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var deck = vm.Deck1!;
        await vm.LoadToDeckAsync(deck, app.ToneTrack("Playing", 60));
        deck.TogglePlayCommand.Execute(null);
        app.Play(0.1);

        Assert.False(PreviewButton(app, 1).IsEffectivelyEnabled);
        Assert.Equal(0, PreviewButton(app, 1).Opacity);   // its place kept, so nothing moves
        app.Window.FindControl<DataGrid>("TrackGrid")!.Focus();
        app.Window.KeyPress(Key.D1, Command | RawInputModifiers.Alt | RawInputModifiers.Shift, PhysicalKey.Digit1, null);
        Assert.False(vm.Preview!.IsActive);
        Assert.Equal("Playing: use CUE to hear it", deck.Hint);
        Assert.True(deck.IsPlaying);
    }
}
