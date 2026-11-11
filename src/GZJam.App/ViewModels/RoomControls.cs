namespace GZJam.App.ViewModels;

/// <summary>
/// The night from outside GZJam's window (#262): play or pause the song the room hears, fade it
/// out (with auto-play on: the next song), and auto-play on or off. Windows puts these under the
/// taskbar button's preview, a Mac in the Dock icon's menu. They act as the deck's own keys do,
/// two clicks on air included when Settings asks for two taps, so a stray click can't stop the
/// room. There's no stop: pause and fade out are GZJam's stop.
/// </summary>
public sealed class RoomControls(MainWindowViewModel owner)
{
    /// <summary>What the buttons show: enabled, which icon, and the tooltip that says what a click does.</summary>
    public sealed record State(
        bool CanPlayPause, bool IsPlaying, string PlayPauseTip,
        bool CanFadeOut, string FadeOutTip,
        bool CanToggleAutoPlay, bool IsAutoPlayOn, string AutoPlayTip);

    public State Current { get; private set; } = new(false, false, "Nothing to play", false, "Nothing playing", false, false, "Auto-play");

    /// <summary>Raised on the UI thread when <see cref="Current"/> changes.</summary>
    public event Action? Changed;

    /// <summary>
    /// The deck these act on: the one the room is hearing, else one on the crossfader's side with a
    /// song ready to play.
    /// </summary>
    internal DeckViewModel? Deck =>
        owner.ActiveDecks.FirstOrDefault(d => d.IsOnAir) ?? owner.ActiveDecks.FirstOrDefault(d => d.IsInRoom && d.IsReady);

    /// <summary>Works out <see cref="Current"/> again (every UI tick), raising <see cref="Changed"/> if it's different.</summary>
    internal void Refresh()
    {
        // What the buttons depend on, compared first (#365): their tooltips are made only when it changes.
        DeckViewModel? deck = Deck;
        AutoPlayViewModel? autoPlay = owner.AutoPlay;
        var basis = (deck, deck?.Title, deck?.IsPlaying, deck?.IsPauseArmed, deck?.IsFadeArmed, deck?.PauseNeedsConfirm,
            autoPlay is null, autoPlay?.IsOn, autoPlay?.CanToggle);
        if (_basis is { } was && was.Equals(basis))
            return;
        _basis = basis;
        State state = Compute();
        if (state == Current)
            return;
        Current = state;
        Changed?.Invoke();
    }

    private (DeckViewModel?, string?, bool?, bool?, bool?, bool?, bool, bool?, bool?)? _basis;

    private State Compute()
    {
        DeckViewModel? deck = Deck;
        AutoPlayViewModel? autoPlay = owner.AutoPlay;
        bool playing = deck is { IsPlaying: true };
        string song = deck is null ? "" : $"\"{deck.Title}\"";

        string playTip = deck is null ? "Nothing to play"
            : !playing ? $"Play {song}"
            : deck.IsPauseArmed ? "Click again to pause"
            : deck.PauseNeedsConfirm ? $"Pause {song} (two clicks)"
            : $"Pause {song}";
        string fadeTip = !playing ? "Nothing playing to fade out"
            : deck!.IsFadeArmed ? "Click again to fade out"
            : (autoPlay?.IsOn == true ? $"Fade out {song}; then the next song" : $"Fade out {song}")
                + (deck.PauseNeedsConfirm ? " (two clicks)" : "");
        string autoTip = autoPlay is null ? "Auto-play"
            : autoPlay.IsOn ? "Auto-play is on: click to turn it off"
            : autoPlay.CanToggle ? "Turn auto-play on"
            : "Auto-play: add songs to the queue first";

        return new State(deck is not null, playing, playTip, playing, fadeTip,
            autoPlay is not null && (autoPlay.IsOn || autoPlay.CanToggle), autoPlay?.IsOn == true, autoTip);
    }

    /// <summary>Play or pause the song the room hears, as its deck's play key does.</summary>
    public void PlayPause()
    {
        if (Deck is { } deck)
        {
            Log.Info($"Room controls: play/pause on Deck {deck.Label}");
            deck.TogglePlayCommand.Execute(null);
        }
        Refresh();
    }

    /// <summary>Fade out the song the room hears, as its FADE OUT does; with auto-play on, the next song follows.</summary>
    public void FadeOut()
    {
        if (Deck is { IsPlaying: true } deck)
        {
            Log.Info($"Room controls: fade out on Deck {deck.Label}");
            deck.FadeOutCommand.Execute(null);
        }
        Refresh();
    }

    /// <summary>Auto-play on or off (as Ctrl+P), or a notice saying why it can't start.</summary>
    public void ToggleAutoPlay()
    {
        if (owner.AutoPlay is not { } autoPlay)
            return;
        Log.Info($"Room controls: auto-play {(autoPlay.CanToggle ? autoPlay.IsOn ? "off" : "on" : "(nothing to play)")}");
        if (autoPlay.CanToggle)
            autoPlay.IsOn = !autoPlay.IsOn;
        else
            owner.ShowNotice("Nothing to auto-play: add songs to the queue first.");
        Refresh();
    }
}
