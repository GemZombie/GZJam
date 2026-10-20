using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GZJam.Audio;
using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>One deck: what's loaded, transport, cue, tempo, and the big readout.</summary>
public sealed partial class DeckViewModel : ObservableObject
{
    /// <summary>How long the room must hear a song for it to count as played (goes into History).</summary>
    public static readonly TimeSpan CountsAsPlayed = TimeSpan.FromSeconds(30);

    /// <summary>How long a first tap on an on-air deck's pause waits for the second.</summary>
    private static readonly TimeSpan ArmedFor = TimeSpan.FromSeconds(2.5);

    private readonly MainWindowViewModel _owner;
    private readonly AppSettings _settings;
    private CancellationTokenSource? _armed;

    // How much of the current track the room has heard, for History. Measured in track time, so
    // pauses and listening in the headphones don't count, and seeks are excluded explicitly.
    private TimeSpan _heard;
    private TimeSpan _lastPosition;
    private DateTime? _firstOnAir;
    private bool _counted;

    /// <summary>
    /// Raised once per play when the room has heard the track for <see cref="CountsAsPlayed"/>
    /// (or all of it, if shorter), with the time it first went on air.
    /// </summary>
    public event Action<DeckViewModel, DateTime>? PlayedToRoom;

    public DeckViewModel(MainWindowViewModel owner, Deck deck, AppSettings settings)
    {
        _owner = owner;
        _settings = settings;
        Deck = deck;
        Readout = settings.DeckReadouts.GetValueOrDefault(Label, DeckReadout.Remaining);
    }

    public Deck Deck { get; }
    /// <summary>"1", "2".</summary>
    public string Label => Deck.Name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTrack), nameof(Title), nameof(Subtitle), nameof(TrackBpm))]
    public partial Track? Track { get; private set; }

    [ObservableProperty]
    public partial DeckReadout Readout { get; private set; }

    /// <summary>Shown briefly over the position bar, e.g. "Tap again to pause".</summary>
    [ObservableProperty]
    public partial string? Hint { get; private set; }

    /// <summary>The first of two taps needed to pause an on-air deck has happened.</summary>
    [ObservableProperty]
    public partial bool IsPauseArmed { get; private set; }

    public bool HasTrack => Track is not null;

    /// <summary>The track played to its end or was faded out; it's done for this set.</summary>
    public bool IsFinished { get; private set; }

    /// <summary>Loaded with a track that hasn't been played out yet.</summary>
    public bool IsReady => HasTrack && !IsPlaying && !IsFinished;
    public bool IsPlaying => Deck.IsPlaying;

    /// <summary>The crossfader lets the room hear this deck.</summary>
    public bool IsInRoom => HasTrack && Deck.IsInRoom;

    /// <summary>Playing and audible in the room: protected from stray loads and seeks.</summary>
    public bool IsOnAir => IsPlaying && IsInRoom;

    /// <summary>Playing, but the crossfader has it out of the room.</summary>
    public bool IsOffAir => IsPlaying && !IsInRoom;

    public string Status => IsOnAir ? "ON AIR" : IsOffAir ? "OFF AIR" : "";

    /// <summary>The cue button: this deck in the headphones. Independent of the crossfader.</summary>
    public bool IsCueOn
    {
        get => Deck.Cue;
        set
        {
            if (Deck.Cue == value)
                return;
            Deck.Cue = value;
            Log.Info($"Deck {Label}: cue {(value ? "on" : "off")}");
            OnPropertyChanged();
        }
    }

    public string Title => Track?.DisplayTitle ?? "No track loaded";

    public string Subtitle => Track is { } t
        ? string.Join(" · ", new[] { t.Artist, t.Album, t.Year?.ToString(CultureInfo.InvariantCulture) }.Where(s => !string.IsNullOrWhiteSpace(s)))
        : "Drag a track here, or right-click › Load to Deck " + Label;

    /// <summary>0..1 through the track.</summary>
    public double Progress => Deck.Length > TimeSpan.Zero ? Deck.Position / Deck.Length : 0;

    /// <summary>Jumping around in what the room is dancing to is blocked; fade it out of the room first.</summary>
    public bool CanSeek => HasTrack && !IsOnAir;

    public double? TrackBpm => Track?.Bpm;
    public double? CurrentBpm => TrackBpm * (1 + Deck.TempoPercent / 100);

    public double TempoPercent
    {
        get => Deck.TempoPercent;
        set
        {
            if (Math.Abs(Deck.TempoPercent - value) < 0.001)
                return;
            Deck.TempoPercent = Math.Round(value, 1);
            OnPropertyChanged();
            OnPropertyChanged(nameof(TempoText));
            RefreshReadout();
        }
    }

    public string TempoText => Deck.TempoPercent.ToString("+0.0'%';-0.0'%';'0.0%'", CultureInfo.CurrentCulture);

    public string BpmText => CurrentBpm is { } bpm ? Format.Bpm(Math.Round(bpm, 1)) : "—";

    public string BpmCaption => TrackBpm is { } original
        ? Deck.TempoPercent == 0 ? "BPM" : $"BPM · from {Format.Bpm(original)}"
        : "BPM unknown";

    public string ReadoutText => Readout switch
    {
        DeckReadout.Elapsed => Format.Duration(Deck.Position),
        DeckReadout.Bpm => BpmText,
        _ => HasTrack ? "-" + Format.Duration(Deck.Remaining) : "-0:00",
    };

    public string ReadoutCaption => Readout switch
    {
        DeckReadout.Elapsed => $"elapsed of {Format.Duration(Deck.Length)}",
        DeckReadout.Bpm => $"{BpmCaption} · {TempoText}",
        _ => $"{Format.Duration(Deck.Position)} / {Format.Duration(Deck.Length)}",
    };

    // ───────────── Commands ─────────────

    [RelayCommand]
    private void CycleReadout()
    {
        Readout = Readout switch
        {
            DeckReadout.Remaining => DeckReadout.Elapsed,
            DeckReadout.Elapsed => DeckReadout.Bpm,
            _ => DeckReadout.Remaining,
        };
        _settings.DeckReadouts[Label] = Readout;
        RefreshReadout();
    }

    [RelayCommand]
    private void ToggleCue() => IsCueOn = !IsCueOn;

    /// <summary>
    /// Whether pausing needs a second tap (or a press-and-hold): only for a deck the room is
    /// hearing, and only if chosen in Settings.
    /// </summary>
    public bool PauseNeedsConfirm => IsOnAir && _settings.OnAirPause == OnAirPause.TapTwice;

    /// <summary>A tap on play/pause.</summary>
    [RelayCommand]
    private void TogglePlay()
    {
        if (!HasTrack)
            return;
        if (!IsPlaying)
        {
            if (IsFinished)
                ResetListening();   // playing it again from the top: a new play
            IsFinished = false;
            Deck.Play();
            Log.Info($"Deck {Label}: play \"{Title}\" ({(IsInRoom ? "in the room" : "not in the room")}{(IsCueOn ? ", cue on" : "")})");
        }
        else if (PauseNeedsConfirm && !IsPauseArmed)
        {
            Arm();
            Log.Info($"Deck {Label}: pause armed (needs a second tap)");
            return;
        }
        else
        {
            Pause("tap");
        }
        Refresh();
    }

    /// <summary>A completed press-and-hold on play/pause: always pauses.</summary>
    public void HoldPause()
    {
        if (IsPlaying)
            Pause("hold");
        Refresh();
    }

    private void Pause(string how)
    {
        Disarm();
        Deck.Pause();
        Log.Info($"Deck {Label}: pause ({how}){(Deck.IsPlaying ? " -- STILL PLAYING" : "")}");
    }

    private async void Arm()
    {
        var armed = _armed = new CancellationTokenSource();
        IsPauseArmed = true;
        ShowHint("Tap again to pause");
        try
        {
            await Task.Delay(ArmedFor, armed.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        if (_armed == armed)
            Disarm();
    }

    private void Disarm()
    {
        _armed?.Cancel();
        _armed = null;
        IsPauseArmed = false;
        if (Hint == "Tap again to pause")
            Hint = null;
    }

    [RelayCommand]
    private void FadeOut()
    {
        Deck.FadeOut(TimeSpan.FromSeconds(_settings.FadeOutSeconds));
        Log.Info($"Deck {Label}: fade out over {_settings.FadeOutSeconds:0} s");
        Refresh();
    }

    [RelayCommand]
    private void Eject()
    {
        if (IsPlaying)
            return;
        Deck.Unload();
        Track = null;
        Log.Info($"Deck {Label}: eject");
        Refresh();
    }

    [RelayCommand]
    private void ResetTempo() => TempoPercent = 0;

    /// <summary>The position bar was pressed while locked: say why, and how to unlock it.</summary>
    public void ExplainSeekLock()
    {
        if (IsOnAir)
            ShowHint($"Move the crossfader off Deck {Label} to jump around in it");
    }

    public void SeekTo(double fraction)
    {
        if (!CanSeek)
            return;
        Deck.Seek(Deck.Length * Math.Clamp(fraction, 0, 1));
        _lastPosition = Deck.Position;   // a jump isn't listening
        Refresh();
    }

    /// <summary>Starts the deck for auto-play.</summary>
    internal void Start()
    {
        if (IsFinished)
            ResetListening();
        IsFinished = false;
        Deck.Play();
        Log.Info($"Deck {Label}: auto-play starts \"{Title}\"");
        Refresh();
    }

    internal void Rewind()
    {
        Deck.Seek(TimeSpan.Zero);
        _lastPosition = TimeSpan.Zero;
        Refresh();
    }

    internal void MarkFinished()
    {
        // A song shorter than the threshold counts if it played to its end in the room. A song
        // faded out early (a false start, the wrong track) doesn't.
        if (!_counted && _firstOnAir is { } since && Deck.Position >= Deck.Length - TimeSpan.FromSeconds(1))
            CountAsPlayed(since);
        IsFinished = true;
        Refresh();
    }

    /// <summary>Loads a track (the caller has already checked deck protection).</summary>
    internal void Load(Track track)
    {
        Disarm();
        IsFinished = false;
        ResetListening();
        Deck.Load(track.Path);
        Deck.TempoPercent = 0;
        Track = track;
        Log.Info($"Deck {Label}: load \"{track.DisplayTitle}\" ({track.Path})");
        OnPropertyChanged(nameof(TempoPercent));
        OnPropertyChanged(nameof(TempoText));
        Refresh();
    }

    private async void ShowHint(string text)
    {
        Hint = text;
        await Task.Delay(2500);
        if (Hint == text)
            Hint = null;
    }

    private void ResetListening()
    {
        _heard = TimeSpan.Zero;
        _lastPosition = TimeSpan.Zero;
        _firstOnAir = null;
        _counted = false;
    }

    /// <summary>Adds up how long the room has heard this track; counts it as played at the threshold.</summary>
    private void TrackListening()
    {
        if (!HasTrack)
            return;
        TimeSpan position = Deck.Position;
        if (IsOnAir)
        {
            TimeSpan step = position - _lastPosition;
            if (step > TimeSpan.Zero)
                _heard += step;
            _firstOnAir ??= DateTime.UtcNow;
            if (!_counted && _heard >= CountsAsPlayed)
                CountAsPlayed(_firstOnAir.Value);
        }
        _lastPosition = position;
    }

    private void CountAsPlayed(DateTime firstOnAir)
    {
        _counted = true;
        if (Track is not null)
            PlayedToRoom?.Invoke(this, firstOnAir);
    }

    /// <summary>Re-reads everything that changes as the track plays.</summary>
    public void Refresh()
    {
        TrackListening();
        if (IsPauseArmed && !PauseNeedsConfirm)
            Disarm();   // no longer on air (or the setting changed): a single tap pauses again
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(IsInRoom));
        OnPropertyChanged(nameof(IsOnAir));
        OnPropertyChanged(nameof(IsOffAir));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(CanSeek));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(PauseNeedsConfirm));
        OnPropertyChanged(nameof(IsCueOn));
        OnPropertyChanged(nameof(IsFinished));
        OnPropertyChanged(nameof(IsReady));
        RefreshReadout();
    }

    private void RefreshReadout()
    {
        OnPropertyChanged(nameof(CurrentBpm));
        OnPropertyChanged(nameof(BpmText));
        OnPropertyChanged(nameof(BpmCaption));
        OnPropertyChanged(nameof(ReadoutText));
        OnPropertyChanged(nameof(ReadoutCaption));
    }
}
