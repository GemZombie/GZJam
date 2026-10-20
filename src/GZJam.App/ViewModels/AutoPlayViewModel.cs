using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GZJam.Audio;
using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>
/// Auto-play: when the song the room is hearing ends (or is faded out), wait the gap, start the
/// other deck and move the crossfader to it, then load the next queued track onto the deck that
/// just finished. Decks alternate 1, 2, 1, 2... Tracks leave the queue as they're loaded.
/// Turning auto-play off never touches the current song; it just won't start another.
/// </summary>
public sealed partial class AutoPlayViewModel : ObservableObject
{
    private readonly MainWindowViewModel _owner;
    private readonly MixEngine _mix;
    private readonly QueueViewModel _queue;
    private readonly AppSettings _settings;

    /// <summary>The deck waiting out the gap, and when it starts.</summary>
    private (DeckViewModel Deck, long Due)? _pending;

    public AutoPlayViewModel(MainWindowViewModel owner, MixEngine mix, QueueViewModel queue, AppSettings settings)
    {
        _owner = owner;
        _mix = mix;
        _queue = queue;
        _settings = settings;
        _queue.Items.CollectionChanged += OnQueueChanged;
    }

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    /// <summary>The switch works whenever there's something to play: queued tracks or a loaded deck.</summary>
    public bool CanToggle => IsOn || _queue.Items.Count > 0 || _owner.Decks.Any(d => d.HasTrack);

    public double GapSeconds => _settings.GapSeconds;
    public string GapText => $"{_settings.GapSeconds:0.0} s";

    /// <summary>What auto-play is about to do, e.g. "Deck 2 in 0:03".</summary>
    public string Status
    {
        get
        {
            if (!IsOn)
                return "";
            if (_pending is { } pending)
            {
                double secondsLeft = (pending.Due - TimeProvider.System.GetTimestamp()) / (double)TimeProvider.System.TimestampFrequency;
                return $"Deck {pending.Deck.Label} in {Format.Duration(TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, secondsLeft))))}";
            }
            if (NextDeck is { IsReady: true } next)
                return $"Next: Deck {next.Label}";
            return _queue.Items.Count == 0 ? "Queue is empty" : "";
        }
    }

    [RelayCommand]
    private void GapDown() => SetGap(_settings.GapSeconds - 0.5);

    [RelayCommand]
    private void GapUp() => SetGap(_settings.GapSeconds + 0.5);

    private void SetGap(double seconds)
    {
        _settings.GapSeconds = Math.Clamp(seconds, 0, 15);
        OnPropertyChanged(nameof(GapSeconds));
        OnPropertyChanged(nameof(GapText));
    }

    /// <summary>The deck the crossfader is on: the one the room hears.</summary>
    private DeckViewModel LiveDeck => _owner.Decks[_mix.LiveDeck.Index];

    /// <summary>The deck that plays after the live one.</summary>
    private DeckViewModel? NextDeck => _owner.Decks.Count < 2 ? null : _owner.Decks[(_mix.LiveDeck.Index + 1) % 2];

    partial void OnIsOnChanged(bool value)
    {
        Log.Info($"Auto-play {(value ? "on" : "off")}");
        if (value)
            Begin();
        else
            _pending = null;   // a song waiting out the gap doesn't start
        Changed();
    }

    /// <summary>
    /// Turned on: if nothing is playing to the room, start the live deck (loading the top of the
    /// queue onto it if it's empty). Then make sure the other deck is loaded and ready.
    /// </summary>
    private void Begin()
    {
        if (!_owner.Decks.Any(d => d.IsOnAir))
        {
            DeckViewModel live = LiveDeck;
            if (!live.IsReady && !live.IsPlaying)
                LoadNextOnto(live);
            if (live.IsReady)
                live.Start();
        }
        Refill();
    }

    /// <summary>A deck's music stopped: its song ended or was faded out.</summary>
    public void OnDeckFinished(DeckViewModel deck)
    {
        if (!IsOn || deck.Deck != _mix.LiveDeck || _pending is not null)
            return;   // only the song the room was hearing advances the set

        DeckViewModel? next = NextDeck;
        if (next is null || next.IsPlaying)
            return;
        if (!next.IsReady)
            LoadNextOnto(next);
        if (!next.IsReady)
        {
            IsOn = false;
            _owner.ShowNotice("Auto-play stopped: the queue is empty.");
            return;
        }
        _pending = (next, TimeProvider.System.GetTimestamp() + (long)(_settings.GapSeconds * TimeProvider.System.TimestampFrequency));
        Log.Info($"Auto-play: Deck {next.Label} starts in {_settings.GapSeconds:0.0} s");
        Changed();
    }

    /// <summary>Runs on every UI tick: starts a deck when its gap is up, keeps the next deck loaded.</summary>
    public void Tick()
    {
        if (!IsOn)
            return;

        if (_pending is { } pending && TimeProvider.System.GetTimestamp() >= pending.Due)
        {
            _pending = null;
            // The room is silent during the gap, so the crossfader can jump straight over.
            _owner.Mixer!.SetNow(pending.Deck.Deck.Index == 0 ? 0 : 1);
            pending.Deck.Start();
        }
        Refill();
        OnPropertyChanged(nameof(Status));
    }

    /// <summary>Keeps the deck after the live one loaded with the next queued track.</summary>
    private void Refill()
    {
        if (_pending is not null || NextDeck is not { } next || next.IsPlaying || next.IsReady)
            return;
        LoadNextOnto(next);
    }

    /// <summary>Loads the top of the queue onto a deck, skipping tracks that won't open.</summary>
    private void LoadNextOnto(DeckViewModel deck)
    {
        while (_queue.TakeNext() is Track track)
        {
            try
            {
                deck.Load(track);
                return;
            }
            catch (AudioException ex)
            {
                Log.Error($"Auto-play couldn't load {track.Path}", ex);
                _owner.ShowNotice($"Skipped {track.DisplayTitle}: it couldn't be opened.");
            }
        }
    }

    private void OnQueueChanged(object? sender, NotifyCollectionChangedEventArgs e) => Changed();

    private void Changed()
    {
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(Status));
    }

    /// <summary>Called when decks load or eject, which can make the switch usable.</summary>
    public void Refresh() => Changed();
}
