using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GZJam.App.ViewModels;

namespace GZJam.App.Views;

/// <summary>
/// The keys that work from anywhere (#132), each the DJ's to change in Settings › Keys (#135; see
/// <see cref="KeyMap"/>). One handler, tunneling from the window, so it's ahead of the lists and
/// the search box, which would take Ctrl+arrows for themselves. Every key has Ctrl (Cmd on a Mac)
/// or is a function key, so none can fire while typing; the few that mean something in a text
/// box (Ctrl+Alt, Ctrl+← →) are left to it there.
/// </summary>
public partial class MainWindow
{
    /// <summary>One press of a volume key, in the sliders' percent: small, so a held key can't blast the room.</summary>
    internal const double VolumeKeyStep = 2;

    private void InitializeKeys()
    {
        AddHandler(KeyDownEvent, OnShortcutKeyDown, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not MainWindowViewModel vm)
                return;
            ShowSongInfoKey();
            vm.Keys.Changed += (_, _) =>
            {
                ShowSongInfoKey();
                ShowQueueWideTip();
            };
        };
    }

    private void OnShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        if (SettingsPanel.IsChoosingKey || DataContext is not MainWindowViewModel vm
            || KeyChord.From(e.Key, e.KeyModifiers) is not { } chord || vm.Keys.ActionFor(chord) is not { } action)
            return;
        if (e.Source is TextBox && !chord.WorksWhileTyping)
            return;
        if (Do(action))
            e.Handled = true;
    }

    /// <summary>Does what a key is for; false if it can't now (its deck isn't on screen), leaving the key to others.</summary>
    private bool Do(KeyAction action)
    {
        if (KeyAction.IsDeckCommand(action.Command))
        {
            if (action.Deck > Vm.ActiveDecks.Count || DeckAt(action.Deck - 1) is not { } deck)
                return false;
            switch (action.Command)
            {
                case KeyCommand.PlayPause:
                    deck.TogglePlayCommand.Execute(null);   // the two-tap rule on air, as its button
                    break;
                case KeyCommand.Cue:
                    deck.IsCueOn = !deck.IsCueOn;
                    break;
                case KeyCommand.Load:
                    LoadSelectedOnto(deck);
                    break;
                case KeyCommand.Preview:
                    deck.PreviewSongCommand.Execute(null);
                    break;
                case KeyCommand.FadeOut when deck.IsPlaying:
                    deck.FadeOutCommand.Execute(null);   // two taps on air too
                    break;
            }
            return true;
        }

        switch (action.Command)
        {
            case KeyCommand.CrossfadeTo1 or KeyCommand.CrossfadeTo2 when Vm.Mixer is { } mixer:
                (action.Command == KeyCommand.CrossfadeTo1 ? mixer.SnapTo1Command : mixer.SnapTo2Command).Execute(null);
                return true;
            case KeyCommand.AutoPlay when Vm.AutoPlay is not null:
                Vm.Room.ToggleAutoPlay();
                return true;
            case KeyCommand.MainUp or KeyCommand.MainDown when Vm.Mixer is { } mixer:
                mixer.MainVolume += action.Command == KeyCommand.MainUp ? VolumeKeyStep : -VolumeKeyStep;
                return true;
            case KeyCommand.PhonesUp or KeyCommand.PhonesDown when Vm.Mixer is { } mixer:
                mixer.CueVolume += action.Command == KeyCommand.PhonesUp ? VolumeKeyStep : -VolumeKeyStep;
                return true;
            case KeyCommand.Search when Vm.IsOrganizeOpen:
                _organize?.FocusFind();
                return true;
            case KeyCommand.Search:
                SearchBox.Focus();
                SearchBox.SelectAll();
                return true;
            case KeyCommand.SongInfo:
                return !Vm.IsOrganizeOpen && ShowSelectedSongInfo();   // Ctrl+I, like the Mac's Get Info (#129)
            case KeyCommand.CompactDecks:
                Vm.ToggleCompactDecksCommand.Execute(null);   // or back from hidden (#164)
                return true;
            case KeyCommand.HideDecks:
                Vm.ToggleHideDecksCommand.Execute(null);
                return true;
            case KeyCommand.QueueWide when !Vm.IsOrganizeOpen:
                ToggleQueueWide();   // #178
                return true;
            case KeyCommand.FullScreen:
                ToggleFullScreen();   // #155
                return true;
            case KeyCommand.Settings:
                if (IsSettingsOpen)
                    CloseSettings();
                else
                    OpenSettings();
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// The load key: the song selected in the queue if that's where you are (it leaves the queue,
    /// as from its menu), otherwise the one selected in the library. Loading over a playing deck is
    /// protected as always.
    /// </summary>
    private async void LoadSelectedOnto(DeckViewModel deck)
    {
        if (QueueList.IsKeyboardFocusWithin)
        {
            if (SelectedQueueItems() is [var item])
            {
                if (await Vm.LoadToDeckAsync(deck, item.Track))
                    Vm.Queue.Loaded([item]);
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
