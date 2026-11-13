using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GZJam.App.ViewModels;

namespace GZJam.App.Views;

/// <summary>
/// ↑ from the queue's first song goes on up to the deck that plays next (#404), shown in the
/// selection's blue, so its song can be compared with the queue's from the keyboard: Space
/// previews it in the headphones, as on a queued song, and ↓ goes back into the queue. Enter,
/// Delete and the other list keys do nothing there. The keyboard stays in the queue all the while;
/// a click anywhere, or the keys going elsewhere, leaves the deck.
/// </summary>
public partial class MainWindow
{
    /// <summary>The deck the keys are on, above the queue; null while they're in the queue itself.</summary>
    private DeckViewModel? _keyDeck;

    internal DeckViewModel? KeyDeck => _keyDeck;

    private void SetUpQueueDeckStop()
    {
        QueueList.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsKeyboardFocusWithinProperty && !QueueList.IsKeyboardFocusWithin)
                LeaveKeyDeck();
        };
        QueueList.SelectionChanged += (_, _) =>
        {
            if (QueueList.SelectedItems is { Count: > 0 })
                LeaveKeyDeck();
        };
        AddHandler(PointerPressedEvent, (_, _) => LeaveKeyDeck(), RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>↑ on the queue's first song: up to the deck that plays next, if it has a song.</summary>
    private bool TryKeysToDeck()
    {
        if (QueueList.SelectedIndex != 0 || QueueList.SelectedItems is not { Count: 1 }
            || Vm.AutoPlay?.DeckForNextSong() is not { HasTrack: true } deck)
            return false;
        _keyDeck = deck;
        deck.IsKeySelected = true;
        QueueList.SelectedItems.Clear();   // one selection shown: the deck's
        return true;
    }

    /// <summary>The keys while they're on the deck: true if handled.</summary>
    private bool OnKeyDeckKeyDown(KeyEventArgs e)
    {
        if (_keyDeck is not { } deck)
            return false;
        if (e.KeyModifiers != KeyModifiers.None)
            return false;   // Ctrl+number and the rest work as anywhere
        switch (e.Key)
        {
            case Key.Space:
                deck.PreviewSongCommand.Execute(null);
                return true;
            case Key.Down or Key.Escape:
                LeaveKeyDeck();
                if (Vm.Queue.Items.Count > 0)
                {
                    QueueList.SelectedIndex = 0;
                    if (QueueList.ContainerFromIndex(0) is ListBoxItem row)
                        row.Focus(NavigationMethod.Directional);
                }
                return true;
            case Key.Up or Key.Enter or Key.Delete or Key.Back or Key.Home or Key.End or Key.PageUp or Key.PageDown
                or Key.Left or Key.Right:
                return true;   // a deck isn't a list: nothing to move to, open or take out
            default:
                return false;
        }
    }

    private void LeaveKeyDeck()
    {
        if (_keyDeck is null)
            return;
        _keyDeck.IsKeySelected = false;
        _keyDeck = null;
    }
}
