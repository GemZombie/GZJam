using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GZJam.Audio;
using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>
/// Auto-play: when the song the room is hearing ends (or is faded out), wait the gap, start the
/// next deck and move the crossfader to it, then load the next queued track onto the deck after
/// that. Decks go in number order: 1, 2, 1, 2... or with four, 1, 2, 3, 4, 1... which switches
/// sides of the crossfader every time. Tracks leave the queue as they're loaded.
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

    /// <summary>The deck the room heard last, so the next is the one after it.</summary>
    private DeckViewModel? _current;

    /// <summary>With <see cref="WaitAfterEach"/>: the deck lined up and waiting for the DJ to press play.</summary>
    private DeckViewModel? _waiting;

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
            if (_waiting is { } waiting)
                return $"Deck {waiting.Label} is ready: press play";
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

    /// <summary>The shortest and longest limit on each song, and the step between (#98).</summary>
    internal const double FadeAfterMin = 60, FadeAfterMax = 300, FadeAfterStep = 15;

    /// <summary>"Off", or how long each song plays before auto-play fades it out, e.g. "1:30".</summary>
    public string FadeAfterText => _settings.AutoFadeAfterSeconds > 0
        ? Format.Duration(TimeSpan.FromSeconds(_settings.AutoFadeAfterSeconds)) : "Off";

    public bool IsFadeAfterOn => _settings.AutoFadeAfterSeconds > 0;

    [RelayCommand]
    private void FadeAfterDown() => SetFadeAfter(_settings.AutoFadeAfterSeconds <= FadeAfterMin ? 0 : _settings.AutoFadeAfterSeconds - FadeAfterStep);

    [RelayCommand]
    private void FadeAfterUp() => SetFadeAfter(_settings.AutoFadeAfterSeconds <= 0 ? FadeAfterMin : _settings.AutoFadeAfterSeconds + FadeAfterStep);

    private void SetFadeAfter(double seconds)
    {
        _settings.AutoFadeAfterSeconds = seconds <= 0 ? 0 : Math.Clamp(seconds, FadeAfterMin, FadeAfterMax);
        Log.Info(_settings.AutoFadeAfterSeconds > 0 ? $"Auto-play: fade each song out after {FadeAfterText}" : "Auto-play: songs play to their end");
        OnPropertyChanged(nameof(FadeAfterText));
        OnPropertyChanged(nameof(IsFadeAfterOn));
        SetAutoFades();
        Changed();
    }

    /// <summary>
    /// For classes, where the teacher talks between songs (#100): when a song ends, the next is
    /// lined up (the crossfader moves to it) but waits for the DJ or teacher to press play. Not
    /// remembered: auto-play that waits, at a dance next week, would be a silence nobody expected.
    /// </summary>
    [ObservableProperty]
    public partial bool WaitAfterEach { get; set; }

    /// <summary>
    /// Auto-play finishes the song playing at this time, then stops, so the night ends on time
    /// (#100). Null: it doesn't. Not remembered: it's for tonight.
    /// </summary>
    [ObservableProperty]
    public partial DateTime? StopAt { get; set; }

    /// <summary>The time of day, for <see cref="StopAt"/> (tests set it).</summary>
    internal Func<DateTime> Now { get; set; } = () => DateTime.Now;

    /// <summary>How far ahead <see cref="StopChoices"/> reaches, in quarter hours: 8 hours.</summary>
    private const int StopQuarters = 32;

    /// <summary>Off, then every quarter hour from the next one for 8 hours (and the time chosen, if earlier).</summary>
    public IReadOnlyList<Choice<DateTime?>> StopChoices { get; private set; } = [new(null, "Off")];

    public Choice<DateTime?> StopChoice
    {
        get => StopChoices.FirstOrDefault(c => c.Value == StopAt) ?? StopChoices[0];
        set
        {
            if (value is not null)   // null: the list was replaced, not a choice
                StopAt = value.Value;
        }
    }

    /// <summary>Brings the stop times up to date with the clock: called as the options open.</summary>
    public void RefreshStopChoices()
    {
        DateTime now = Now();
        DateTime first = now.Date.AddMinutes((Math.Floor(now.TimeOfDay.TotalMinutes / 15) + 1) * 15);
        var times = Enumerable.Range(0, StopQuarters).Select(i => first.AddMinutes(15 * i)).ToList();
        if (StopAt is { } chosen && !times.Contains(chosen))
            times.Insert(0, chosen);
        StopChoices = [new(null, "Off"), .. times.Select(t => new Choice<DateTime?>(t, ClockTime(t)))];
        OnPropertyChanged(nameof(StopChoices));
        OnPropertyChanged(nameof(StopChoice));
    }

    private static string ClockTime(DateTime time) => time.ToString("t", System.Globalization.CultureInfo.CurrentCulture);

    partial void OnWaitAfterEachChanged(bool value)
    {
        Log.Info(value ? "Auto-play: waits for you after each song" : "Auto-play: starts each song after the gap");
        if (!value && _waiting is { } waiting)
        {
            _waiting = null;
            if (IsOn && !waiting.IsPlaying)
                Start(waiting);   // no longer waiting: go on as if the gap were over
        }
        Changed();
    }

    partial void OnStopAtChanged(DateTime? value)
    {
        Log.Info(value is { } at ? $"Auto-play: stops after the song playing at {ClockTime(at)}" : "Auto-play: no stop time");
        OnPropertyChanged(nameof(StopChoice));
        Changed();
    }

    /// <summary>The options that are set, e.g. "Fade after 1:30 · Waits after each song · Stops at 11:00 PM".</summary>
    public string OptionsText => string.Join(" · ", new[]
    {
        IsFadeAfterOn ? $"Fade after {FadeAfterText}" : null,
        WaitAfterEach ? "Waits after each song" : null,
        StopAt is { } at ? $"Stops at {ClockTime(at)}" : null,
    }.OfType<string>());

    /// <summary>
    /// The deck the room hears fades out by itself after the set time while auto-play is on.
    /// Off, or the limit off, and every song plays to its end again.
    /// </summary>
    private void SetAutoFades()
    {
        double limit = _settings.AutoFadeAfterSeconds;
        foreach (DeckViewModel deck in _owner.Decks)
        {
            deck.Deck.AutoFade = IsOn && limit > 0 && deck.IsInRoom && deck.HasTrack
                ? (deck.Deck.Start + TimeSpan.FromSeconds(limit), TimeSpan.FromSeconds(_settings.FadeOutSeconds))
                : null;
        }
    }

    /// <summary>The decks on the side of the crossfader the room hears.</summary>
    private List<DeckViewModel> LiveSide =>
        _owner.ActiveDecks.Where(d => Crossfade.Side(d.Deck.Index) == _mix.LiveSide).ToList();

    /// <summary>
    /// The deck the room hears (or last heard): on the crossfader's side, the one playing, else
    /// the one heard last, else one with a song ready. With two decks, simply the crossfader's.
    /// </summary>
    private DeckViewModel LiveDeck
    {
        get
        {
            var side = LiveSide;
            return side.FirstOrDefault(d => d.IsPlaying) ?? side.FirstOrDefault(d => d == _current)
                ?? side.FirstOrDefault(d => d.IsReady) ?? side[0];
        }
    }

    /// <summary>The deck that plays after the live one, in number order.</summary>
    private DeckViewModel? NextDeck
    {
        get
        {
            var decks = _owner.ActiveDecks;
            return decks.Count < 2 ? null : decks[(LiveDeck.Deck.Index + 1) % decks.Count];
        }
    }

    partial void OnIsOnChanged(bool value)
    {
        Log.Info($"Auto-play {(value ? "on" : "off")}");
        if (value)
            Begin();
        else
        {
            if (_pending is { } pending)
            {
                pending.Deck.StartsIn = null;
                _pending = null;   // a song waiting out the gap doesn't start
            }
            _waiting = null;
        }
        SetAutoFades();
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
                live = LiveSide.FirstOrDefault(d => d.IsReady) ?? live;   // four decks: the side's other one may be ready
            if (!live.IsReady && !live.IsPlaying)
                LoadNextOnto(live);
            if (live.IsReady)
            {
                _current = live;
                live.Start();
            }
        }
        Refill();
    }

    /// <summary>A deck's music stopped: its song ended or was faded out.</summary>
    public void OnDeckFinished(DeckViewModel deck)
    {
        if (!IsOn || Crossfade.Side(deck.Deck.Index) != _mix.LiveSide || _pending is not null || _waiting is not null)
            return;   // only the song the room was hearing advances the set
        _current = deck;

        // The night's end, as set: the song playing then was the last.
        if (StopAt is { } stopAt && Now() >= stopAt)
        {
            IsOn = false;
            StopAt = null;
            _owner.ShowNotice($"Auto-play stopped: it's past {ClockTime(stopAt)}, as set.", 8);
            return;
        }

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
        if (WaitAfterEach)
        {
            // Lined up for the teacher: the crossfader goes over (the room is silent), and the
            // song waits for play. Pressing play on it carries on from there.
            _waiting = next;
            Log.Info($"Auto-play: Deck {next.Label} is ready, waiting for play");
            _owner.Mixer!.GlideTo(Crossfade.Side(next.Deck.Index), TimeSpan.FromSeconds(1));
            Changed();
            return;
        }
        _pending = (next, TimeProvider.System.GetTimestamp() + (long)(_settings.GapSeconds * TimeProvider.System.TimestampFrequency));
        Log.Info($"Auto-play: Deck {next.Label} starts in {_settings.GapSeconds:0.0} s");
        // The gap shouldn't look like nothing is happening: the crossfader slides over to the next
        // deck through it (the room is silent, so nobody hears it move), and that deck counts down.
        _owner.Mixer!.GlideTo(Crossfade.Side(next.Deck.Index), TimeSpan.FromSeconds(_settings.GapSeconds));
        next.StartsIn = _settings.GapSeconds;
        Changed();
    }

    /// <summary>Runs on every UI tick: starts a deck when its gap is up, keeps the next deck loaded.</summary>
    public void Tick()
    {
        if (!IsOn)
            return;
        SetAutoFades();   // follows the crossfader and each new song

        if (_waiting is { IsPlaying: true } started)
        {
            _waiting = null;   // the DJ pressed play: the set carries on from it
            _current = started;
        }
        if (_pending is { } pending)
        {
            double left = (pending.Due - TimeProvider.System.GetTimestamp()) / (double)TimeProvider.System.TimestampFrequency;
            pending.Deck.StartsIn = Math.Max(0, left);
            if (left <= 0)
            {
                _pending = null;
                pending.Deck.StartsIn = null;
                Start(pending.Deck);
            }
        }
        Refill();
        OnPropertyChanged(nameof(Status));
    }

    /// <summary>Starts the next song: the gap is over, or auto-play stopped waiting for the DJ.</summary>
    private void Start(DeckViewModel deck)
    {
        // Over at the end of the gap, unless the DJ took the crossfader meanwhile.
        double side = Crossfade.Side(deck.Deck.Index);
        if (_owner.Mixer!.IsGliding || Math.Abs(_owner.Mixer.Crossfader - side) < 0.05)
            _owner.Mixer.SetNow(side);
        _current = deck;
        deck.Start();
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
                _owner.ShowNotice($"Skipped {track.DisplayTitle}: {ex.Message}");
            }
        }
    }

    private void OnQueueChanged(object? sender, NotifyCollectionChangedEventArgs e) => Changed();

    private void Changed()
    {
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(OptionsText));
    }

    /// <summary>Called when decks load or eject, which can make the switch usable.</summary>
    public void Refresh() => Changed();
}
