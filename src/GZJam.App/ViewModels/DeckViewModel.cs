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
        deck.Jumped += (_, _) =>
        {
            Log.Info($"Deck {Label}: jumped (\"{Title}\")");
            _lastPosition = Deck.Position;   // a jump isn't listening
            OnJumpsChanged();
        };
    }

    public Deck Deck { get; }
    internal MainWindowViewModel Owner => _owner;
    /// <summary>"1", "2", "3", "4".</summary>
    public string Label => Deck.Name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTrack), nameof(Title), nameof(Subtitle), nameof(TrackBpm))]
    public partial Track? Track { get; private set; }

    [ObservableProperty]
    public partial DeckReadout Readout { get; private set; }

    /// <summary>The track's overview waveform, once analyzed.</summary>
    [ObservableProperty]
    public partial Waveform? Waveform { get; private set; }

    /// <summary>The track is being analyzed (its waveform isn't ready yet).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LevelText))]
    public partial bool IsAnalyzing { get; private set; }

    /// <summary>Where the music starts and ends, as fractions of the file (silence either side is skipped).</summary>
    public double TrimStart => Deck.Length > TimeSpan.Zero ? Deck.Start / Deck.Length : 0;
    public double TrimEnd => Deck.Length > TimeSpan.Zero ? Deck.End / Deck.Length : 1;

    // Where the song starts and ends: set by the DJ, else found by analysis, else the whole file.
    private TimeSpan? _autoStart, _autoEnd;
    private ManualTrim _manualTrim;

    /// <summary>The DJ set where this song starts (rather than silence trimming finding it).</summary>
    public bool IsStartManual => _manualTrim.Start is not null;
    public bool IsEndManual => _manualTrim.End is not null;

    /// <summary>Where silence trimming says the music starts and ends.</summary>
    public TimeSpan AutomaticStart => _autoStart ?? TimeSpan.Zero;
    public TimeSpan AutomaticEnd => _autoEnd ?? Deck.Length;

    // ───────────── EQ ─────────────

    private TrackEq _eq;

    /// <summary>EQ in dB, 0 = flat. Saved for the song as it's changed, and heard in the headphones too.</summary>
    public double EqLow
    {
        get => _eq.Low;
        set => SetEq(_eq with { Low = value });
    }

    public double EqMid
    {
        get => _eq.Mid;
        set => SetEq(_eq with { Mid = value });
    }

    public double EqHigh
    {
        get => _eq.High;
        set => SetEq(_eq with { High = value });
    }

    /// <summary>The DJ's own gain for this song, on top of loudness matching (the same range as the bands).</summary>
    public double EqGain
    {
        get => _eq.Gain;
        set => SetEq(_eq with { Gain = value });
    }

    public string EqGainText => Db(_eq.Gain);

    public string EqLowText => Db(_eq.Low);
    public string EqMidText => Db(_eq.Mid);
    public string EqHighText => Db(_eq.High);

    /// <summary>The song has EQ set (the deck's EQ button lights up).</summary>
    public bool IsEqSet => !_eq.IsFlat;

    public double EqMinDb => TrackEq.MinDb;
    public double EqMaxDb => TrackEq.MaxDb;

    private static string Db(double db) =>
        db == 0 ? "0 dB" : db.ToString("+0.#' dB';−0.#' dB'", CultureInfo.CurrentCulture);

    [RelayCommand]
    private void FlatEq() => SetEq(TrackEq.Flat);

    /// <summary>A starting point for a hissy transfer: the treble down 6 dB.</summary>
    [RelayCommand]
    private void LessHiss() => SetEq(_eq with { High = -6 });

    /// <summary>Tapping a band's value puts that band back to flat.</summary>
    [RelayCommand]
    private void FlatBand(string band) => SetEq(band switch
    {
        "low" => _eq with { Low = 0 },
        "mid" => _eq with { Mid = 0 },
        "gain" => _eq with { Gain = 0 },
        _ => _eq with { High = 0 },
    });

    /// <summary>Applies EQ to the deck (in half-dB steps, within range) and saves it for the song.</summary>
    private void SetEq(TrackEq eq, bool save = true)
    {
        static double Step(double db, double min, double max) => Math.Round(Math.Clamp(db, min, max) * 2) / 2;
        eq = new TrackEq(Step(eq.Low, TrackEq.MinDb, TrackEq.MaxDb), Step(eq.Mid, TrackEq.MinDb, TrackEq.MaxDb),
            Step(eq.High, TrackEq.MinDb, TrackEq.MaxDb), Step(eq.Gain, TrackEq.MinDb, TrackEq.MaxDb));
        bool changed = eq != _eq;
        _eq = eq;
        Deck.SetEq(eq.Low, eq.Mid, eq.High);
        ApplyGain();
        if (!changed)
            return;
        if (save && Track is not null)
            _owner.SaveEq(Track.Path, eq);
        foreach (string name in new[] { nameof(EqLow), nameof(EqMid), nameof(EqHigh), nameof(EqGain), nameof(EqLowText), nameof(EqMidText),
                     nameof(EqHighText), nameof(EqGainText), nameof(IsEqSet), nameof(LevelText) })
            OnPropertyChanged(name);
    }

    // ───────────── Loudness matching ─────────────

    /// <summary>How fast matching may change the level of a song the room is hearing (dB per second).</summary>
    private const double OnAirRampDbPerSecond = 3;

    private double? _lufs, _peakDb;
    private double _matchDb;         // what matching applies now (eases towards the target on air)
    private long _matchMovedAt;

    /// <summary>What loudness matching would apply to this song: 0 if it's off or not measured yet.</summary>
    private double MatchTargetDb => _settings.MatchLoudness && _lufs is { } lufs ? LoudnessMatch.GainDb(lufs, _peakDb) : 0;

    /// <summary>The song's measured loudness and what matching does to it, for the EQ panel.</summary>
    public string LevelText
    {
        get
        {
            if (Track is null)
                return "";
            if (_lufs is not { } lufs)
                return IsAnalyzing ? "Measuring loudness…" : "Loudness not measured";
            string measured = string.Create(CultureInfo.CurrentCulture, $"{lufs:0.0} LUFS").Replace('-', '−');
            return _settings.MatchLoudness
                ? $"{measured} · matching {Db(MatchTargetDb)}"
                : $"{measured} · loudness matching off";
        }
    }

    /// <summary>Loudness matching was turned on or off in Settings.</summary>
    internal void LoudnessSettingChanged()
    {
        UpdateMatch(force: !IsOnAir);
        OnPropertyChanged(nameof(LevelText));
    }

    /// <summary>
    /// Moves matching towards its target: at once if the room isn't hearing this deck, gently if
    /// it is (e.g. the analysis arrived while the song was already on air), so nothing jumps.
    /// </summary>
    private void UpdateMatch(bool force = false)
    {
        double target = MatchTargetDb;
        long now = TimeProvider.System.GetTimestamp();
        double elapsed = _matchMovedAt == 0 ? 0 : TimeProvider.System.GetElapsedTime(_matchMovedAt, now).TotalSeconds;
        _matchMovedAt = now;
        if (Math.Abs(target - _matchDb) < 0.01)
            return;
        if (force || !IsOnAir)
        {
            _matchDb = target;
        }
        else
        {
            double step = OnAirRampDbPerSecond * Math.Min(elapsed, 0.1);
            _matchDb += Math.Clamp(target - _matchDb, -step, step);
        }
        ApplyGain();
    }

    private void ApplyGain() => Deck.GainDb = _matchDb + _eq.Gain;

    // ───────────── Jumps ─────────────

    /// <summary>The shortest jump worth making (and the least distance from a jump to the song's ends).</summary>
    private static readonly TimeSpan ShortestJump = TimeSpan.FromSeconds(2);

    private List<SongJump> _jumps = [];
    private TimeSpan? _jumpFrom;   // a jump being set: where it leaves, waiting for where it lands

    /// <summary>The song has jumps (the JUMP key shows).</summary>
    public bool HasJumps => _jumps.Count > 0;

    /// <summary>A jump is armed: it will happen when the music gets there (the JUMP key's light).</summary>
    public bool IsJumpArmed => Deck.Jumps.Any(j => j.Armed);

    /// <summary>The jumps as fractions of the file, for the waveform.</summary>
    public IReadOnlyList<Controls.JumpMarker> JumpMarkers => Deck.Length <= TimeSpan.Zero
        ? []
        : Deck.Jumps.Select(j => new Controls.JumpMarker(j.From / Deck.Length, j.To / Deck.Length, j.Armed)).ToList();

    /// <summary>Where a jump being set leaves from, as a fraction (null if none is being set).</summary>
    public double? PendingJumpFrom => _jumpFrom is { } from && Deck.Length > TimeSpan.Zero ? from / Deck.Length : null;

    /// <summary>A jump is waiting for where it lands.</summary>
    public bool IsSettingJump => _jumpFrom is not null;

    public bool CanAddJump => _jumps.Count < LibraryDatabase.MaxJumps;

    internal IReadOnlyList<SongJump> SongJumps => _jumps;

    /// <summary>The JUMP key: arms every jump, or (if any is armed) cancels them for this play.</summary>
    [RelayCommand]
    private void ToggleJump()
    {
        if (!HasJumps)
            return;
        bool arm = !IsJumpArmed;
        Deck.ArmJumps(arm);
        Log.Info($"Deck {Label}: jumps {(arm ? "armed" : "cancelled")}");
        OnJumpsChanged();
    }

    /// <summary>First half of setting a jump: where it leaves (the end of a phrase).</summary>
    public bool StartJump(TimeSpan from)
    {
        if (Track is null)
            return false;
        if (!CanAddJump)
        {
            ShowHint("Two jumps already: remove one first");
            return false;
        }
        if (from < Deck.Start + ShortestJump || from > Deck.End - ShortestJump)
        {
            ShowHint("A jump can't leave that close to the start or end");
            return false;
        }
        _jumpFrom = from;
        ShowHint("Now right-click where it lands");
        OnJumpsChanged();
        return true;
    }

    /// <summary>Second half: where the jump lands. Saved for the song and armed.</summary>
    public bool LandJump(TimeSpan to)
    {
        if (Track is null || _jumpFrom is not { } from)
            return false;
        if ((to - from).Duration() < ShortestJump)
        {
            ShowHint("Land the jump further away");
            return false;
        }
        if (to < Deck.Start || to > Deck.End - ShortestJump)
        {
            ShowHint("A jump can't land that close to the end");
            return false;
        }
        _jumpFrom = null;
        if (Hint == "Now right-click where it lands")
            Hint = null;
        _jumps = [.. _jumps, new SongJump(from, to)];
        _owner.SaveJumps(Track.Path, _jumps);
        Log.Info(string.Create(CultureInfo.InvariantCulture,
            $"Deck {Label}: jump from {from.TotalSeconds:0.0} s to {to.TotalSeconds:0.0} s in \"{Title}\""));
        // Keep the others as they are; the new one is armed.
        Deck.SetJumps(Deck.Jumps.Select(j => (j.From, j.To, j.Armed)).Append((from, to, true)).ToList());
        OnJumpsChanged();
        return true;
    }

    /// <summary>Forgets a jump (or the one being set, at <paramref name="index"/> -1).</summary>
    public void RemoveJump(int index)
    {
        if (index < 0)
        {
            _jumpFrom = null;
            OnJumpsChanged();
            return;
        }
        if (Track is null || index >= _jumps.Count)
            return;
        _jumps = _jumps.Where((_, i) => i != index).ToList();
        _owner.SaveJumps(Track.Path, _jumps);
        Deck.SetJumps(Deck.Jumps.Where((_, i) => i != index).Select(j => (j.From, j.To, j.Armed)).ToList());
        Log.Info($"Deck {Label}: removed jump {index + 1} from \"{Title}\"");
        OnJumpsChanged();
    }

    private void OnJumpsChanged()
    {
        OnPropertyChanged(nameof(HasJumps));
        OnPropertyChanged(nameof(IsJumpArmed));
        OnPropertyChanged(nameof(JumpMarkers));
        OnPropertyChanged(nameof(PendingJumpFrom));
        OnPropertyChanged(nameof(IsSettingJump));
        OnPropertyChanged(nameof(CanAddJump));
        RefreshReadout();
    }

    /// <summary>A song can't be trimmed shorter than this.</summary>
    private static readonly TimeSpan ShortestSong = TimeSpan.FromSeconds(5);

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
            if (IsInRoom && _owner.OnAirBeside(this) is { } other)
            {
                // Two songs at once in the room, with no fader between them.
                ShowHint($"Deck {other.Label} is playing. Fade it out first");
                Log.Info($"Deck {Label}: play refused (Deck {other.Label} is on air on the same side)");
                return;
            }
            if (IsFinished)
            {
                ResetListening();   // playing it again from the top: a new play
                Deck.ArmJumps(true);
            }
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
        Waveform = null;
        _autoStart = _autoEnd = null;
        _manualTrim = default;
        OnTrimChanged();
        _lufs = _peakDb = null;
        _matchDb = 0;
        SetEq(TrackEq.Flat, save: false);
        _jumpFrom = null;
        _jumps = [];
        Deck.SetJumps([]);
        OnJumpsChanged();
        IsAnalyzing = false;
        OnPropertyChanged(nameof(LevelText));
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
        Deck.Seek(Deck.Start);
        _lastPosition = Deck.Position;
        Refresh();
    }

    internal void MarkFinished()
    {
        // A song shorter than the threshold counts if it played to its end in the room. A song
        // faded out early (a false start, the wrong track) doesn't.
        if (!_counted && _firstOnAir is { } since && Deck.Position >= Deck.End - TimeSpan.FromSeconds(1))
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
        Waveform = null;
        Log.Info($"Deck {Label}: load \"{track.DisplayTitle}\" ({track.Path})");
        OnPropertyChanged(nameof(TempoPercent));
        OnPropertyChanged(nameof(TempoText));
        // A start set by hand applies straight away; silence trimming follows with the analysis.
        _autoStart = _autoEnd = null;
        _manualTrim = _owner.GetTrim(track.Path);
        ApplyTrim();
        _lufs = _peakDb = null;
        _matchDb = 0;
        SetEq(_owner.GetEq(track.Path), save: false);
        _jumpFrom = null;
        _jumps = _owner.GetJumps(track.Path).ToList();
        Deck.SetJumps(_jumps.Select(j => (j.From, j.To, true)).ToList());
        OnJumpsChanged();
        if (IsEqSet)
            Log.Info($"Deck {Label}: EQ for this song: low {EqLowText}, mid {EqMidText}, high {EqHighText}");
        Refresh();
        _ = AnalyzeAsync(track);
    }

    /// <summary>
    /// Fetches the track's analysis (instant if it's been analyzed before), then shows the
    /// waveform and trims the silence at either end.
    /// </summary>
    private async Task AnalyzeAsync(Track track)
    {
        if (_owner.Analysis is not { } analysis)
            return;
        IsAnalyzing = true;
        TrackAnalysis? result = await analysis.GetAsync(track.Path);
        if (Track != track)
            return;   // something else was loaded meanwhile
        IsAnalyzing = false;
        if (result is not null)
            Apply(result);
    }

    private void Apply(TrackAnalysis analysis)
    {
        Waveform = analysis.Waveform;
        Track!.DetectedBpm = analysis.Bpm;
        _autoStart = analysis.AudibleStart;
        _autoEnd = analysis.AudibleEnd;
        (_lufs, _peakDb) = (analysis.Lufs, analysis.PeakDb);
        UpdateMatch();
        OnPropertyChanged(nameof(LevelText));
        if (_lufs is { } lufs)
            Log.Info(string.Create(CultureInfo.InvariantCulture,
                $"Deck {Label}: loudness {lufs:0.0} LUFS, peak {_peakDb:0.0} dB; matching {MatchTargetDb:+0.0;-0.0;0} dB"));
        ApplyTrim();
        Log.Info(string.Create(CultureInfo.InvariantCulture,
            $"Deck {Label}: music from {analysis.AudibleStart.TotalSeconds:0.00} s to {analysis.AudibleEnd.TotalSeconds:0.00} s of {analysis.Length.TotalSeconds:0.00} s, BPM {analysis.Bpm?.ToString() ?? "unknown"}"));
        OnPropertyChanged(nameof(TrackBpm));
        OnTrimChanged();
        Refresh();
    }

    private void OnTrimChanged()
    {
        OnPropertyChanged(nameof(TrimStart));
        OnPropertyChanged(nameof(TrimEnd));
        OnPropertyChanged(nameof(IsStartManual));
        OnPropertyChanged(nameof(IsEndManual));
    }

    /// <summary>
    /// Sets the deck's start and end: the DJ's, else the automatic ones. A deck still at the top
    /// moves to the start, skipping the silence (or applause) before it; mid-track, it stays put.
    /// </summary>
    private void ApplyTrim()
    {
        TimeSpan start = _manualTrim.Start ?? AutomaticStart, end = _manualTrim.End ?? AutomaticEnd;
        Deck.SetTrim(start, end);
        if (!IsPlaying && !IsFinished && Deck.Position < start)
        {
            Deck.Seek(start);
            _lastPosition = Deck.Position;   // a jump isn't listening
        }
        OnTrimChanged();
        Refresh();
    }

    /// <summary>
    /// Starts the song at <paramref name="at"/> from now on (saved for the file), or (null) where
    /// silence trimming says. Returns false, with a hint, if that's not possible.
    /// </summary>
    public bool SetStart(TimeSpan? at)
    {
        if (Track is null)
            return false;
        if (at is { } start && start > Deck.End - ShortestSong)
        {
            ShowHint("The start must come before the end");
            return false;
        }
        _manualTrim = _manualTrim with { Start = at };
        _owner.SaveTrim(Track.Path, _manualTrim);
        Log.Info(string.Create(CultureInfo.InvariantCulture,
            $"Deck {Label}: start of \"{Title}\" {(at is { } t ? $"set to {t.TotalSeconds:0.0} s" : "automatic")}"));
        ApplyTrim();
        return true;
    }

    /// <summary>Ends the song at <paramref name="at"/> from now on (saved for the file), or (null) where silence trimming says.</summary>
    public bool SetEnd(TimeSpan? at)
    {
        if (Track is null)
            return false;
        TimeSpan end = at ?? AutomaticEnd;
        if (end < Deck.Start + ShortestSong)
        {
            ShowHint("The end must come after the start");
            return false;
        }
        if (IsOnAir && IsPlaying && end <= Deck.Position + TimeSpan.FromSeconds(1))
        {
            // The room would hear it stop dead.
            ShowHint("That part has already played");
            return false;
        }
        _manualTrim = _manualTrim with { End = at };
        _owner.SaveTrim(Track.Path, _manualTrim);
        Log.Info(string.Create(CultureInfo.InvariantCulture,
            $"Deck {Label}: end of \"{Title}\" {(at is { } t ? $"set to {t.TotalSeconds:0.0} s" : "automatic")}"));
        ApplyTrim();
        return true;
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
            if (step > TimeSpan.Zero && step < TimeSpan.FromSeconds(2))   // a jump or seek isn't listening
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
        UpdateMatch();
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
