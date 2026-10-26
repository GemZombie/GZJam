using Avalonia.Controls;
using Avalonia.Input;
using GZJam.App.ViewModels;

namespace GZJam.App.Views;

/// <summary>
/// Keys for the decks, the crossfader and auto-play (#132), all with Ctrl (Cmd on a Mac), so none
/// can fire while typing:
/// <list type="bullet">
/// <item>Ctrl+1..4: play or pause that deck (with the two-tap rule on air, as its button)</item>
/// <item>Ctrl+Shift+1..4: its CUE</item>
/// <item>Ctrl+Alt+1..4: load the selected song onto it (from the queue if that's where you are)</item>
/// <item>Ctrl+← / →: the crossfader glides to Deck 1 / 2</item>
/// <item>Ctrl+P: auto-play on or off</item>
/// </list>
/// Fade out has no key: one keypress ending the song the room hears is too easy to hit.
/// </summary>
public partial class MainWindow
{
    private void OnDeckKeyDown(object? sender, KeyEventArgs e)
    {
        KeyModifiers modifiers = e.KeyModifiers;
        if (!modifiers.HasFlag(CommandModifier))
            return;
        KeyModifiers extra = modifiers & ~CommandModifier;
        bool typing = e.Source is TextBox;

        if (DeckNumber(e.Key) is { } number)
        {
            if (number > Vm.ActiveDecks.Count || DeckAt(number - 1) is not { } deck)
                return;
            if (extra == KeyModifiers.None)
                deck.TogglePlayCommand.Execute(null);
            else if (extra == KeyModifiers.Shift)
                deck.IsCueOn = !deck.IsCueOn;
            else if (extra == KeyModifiers.Alt && !typing)   // Ctrl+Alt is AltGr in a text box: a character
                LoadSelectedOnto(deck);
            else
                return;
            e.Handled = true;
            return;
        }

        if (extra != KeyModifiers.None)
            return;
        switch (e.Key)
        {
            case Key.Left or Key.Right when !typing && Vm.Mixer is { } mixer:   // in a text box: by word
                (e.Key == Key.Left ? mixer.SnapTo1Command : mixer.SnapTo2Command).Execute(null);
                e.Handled = true;
                break;
            case Key.P when Vm.AutoPlay is { } autoPlay:
                if (autoPlay.CanToggle)
                    autoPlay.IsOn = !autoPlay.IsOn;
                else
                    Vm.ShowNotice("Nothing to auto-play: add songs to the queue first.");
                e.Handled = true;
                break;
        }
    }

    /// <summary>1 to 4 on the number row or the keypad.</summary>
    private static int? DeckNumber(Key key) => key switch
    {
        Key.D1 or Key.NumPad1 => 1,
        Key.D2 or Key.NumPad2 => 2,
        Key.D3 or Key.NumPad3 => 3,
        Key.D4 or Key.NumPad4 => 4,
        _ => null,
    };

    /// <summary>
    /// Ctrl+Alt+number: the song selected in the queue if that's where you are (it leaves the
    /// queue, as from its menu), otherwise the one selected in the library. Loading over a playing
    /// deck is protected as always.
    /// </summary>
    private async void LoadSelectedOnto(DeckViewModel deck)
    {
        if (QueueList.IsKeyboardFocusWithin)
        {
            if (SelectedQueueItems() is [var item])
            {
                if (await Vm.LoadToDeckAsync(deck, item.Track))
                    Vm.Queue.Remove([item]);
            }
            else
            {
                Vm.ShowNotice("Select one song in the queue to load it.");
            }
            return;
        }
        if (SelectedTracks() is [var track])
            await Vm.LoadToDeckAsync(deck, track);
        else
            Vm.ShowNotice("Select one song to load it.");
    }
}
