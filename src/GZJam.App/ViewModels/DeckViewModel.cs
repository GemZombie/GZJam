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
    [NotifyPropertyChangedFor(nameof(CloseUpWaveform))]
    public partial Waveform? Waveform { get; private set; }

    private Waveform? _detail;
    private CancellationTokenSource? _detailStop;

    /// <summary>For the close-up: the song in detail (10 ms a slice) once it's worked out, the overview until then.</summary>
    public Waveform? CloseUpWaveform => _detail ?? Waveform;

    /// <summary>The song's length in seconds.</summary>
    public double LengthSeconds => Deck.Length.TotalSeconds;

    /// <summary>How many seconds the close-up shows (the same on every deck, and remembered).</summary>
    public double CloseUpSeconds
    {
        get => _settings.CloseUpSeconds;
        set
        {
            value = Math.Clamp(value, Controls.CloseUpWaveform.MinWindowSeconds, Controls.CloseUpWaveform.MaxWindowSeconds);
            if (value == _settings.CloseUpSeconds)
                return;
            _settings.CloseUpSeconds = value;
            foreach (DeckViewModel deck in _owner.Decks)
                deck.OnCloseUpChanged();
        }
    }

    /// <summary>How much of the song the close-up shows, as a fraction (for its box on the whole-song view).</summary>
    public double CloseUpFraction => LengthSeconds > 0 ? CloseUpSeconds / LengthSeconds : 0;

    private void OnCloseUpChanged()
    {
        OnPropertyChanged(nameof(CloseUpSeconds));
        OnPropertyChanged(nameof(CloseUpFraction));
    }

    /// <summary>Works out the loaded song's close-up waveform in the background.</summary>
    private async Task LoadDetailAsync(Track track)
    {
        if (_owner.Analysis is not { } analysis)
            return;
        var stop = _detailStop = new CancellationTokenSource();
        Waveform? detail = await analysis.DetailAsync(track.Path, stop.Token);
        if (Track != track || stop.IsCancellationRequested)
            return;   // something else was loaded meanwhile
        _detail = detail;
        OnPropertyChanged(nameof(CloseUpWaveform));
    }

    private void ForgetDetail()
    {
        _detailStop?.Cancel();
        _detailStop = null;
        _detail = null;
        OnPropertyChanged(nameof(CloseUpWaveform));
        OnPropertyChanged(nameof(LengthSeconds));
        OnCloseUpChanged();
    }

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

    /// <summary>How much the song is evened out (a compressor): 0 (off) to 100.</summary>
    public double EqEven
    {
        get => _eq.Even;
        set => SetEq(_eq with { Even = value });
    }

    public string EqEvenText => _eq.Even == 0 ? "Off" : _eq.Even.ToString("0", CultureInfo.CurrentCulture);

    public double EqMaxEven => TrackEq.MaxEven;

    /// <summary>Where the mid band sits, e.g. "1 kHz", "315 Hz".</summary>
    public string EqMidHzText => Hz(_eq.MidHzOrDefault);

    public bool IsMidMoved => !_eq.IsMidAtDefault;

    internal static string Hz(double hz) => hz >= 1000
        ? (hz / 1000).ToString("0.##", CultureInfo.CurrentCulture) + " kHz"
        : hz.ToString("0", CultureInfo.CurrentCulture) + " Hz";

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
        "even" => _eq with { Even = 0 },
        "midhz" => _eq with { MidHz = TrackEq.DefaultMidHz },
        _ => _eq with { High = 0 },
    });

    /// <summary>Moves the mid band one third of an octave down (-1) or up (+1).</summary>
    [RelayCommand]
    private void MoveMid(string direction)
    {
        var steps = TrackEq.MidFrequencies;
        int at = NearestMid(_eq.MidHzOrDefault);
        int to = Math.Clamp(at + (direction == "down" ? -1 : 1), 0, steps.Count - 1);
        SetEq(_eq with { MidHz = steps[to] });
    }

    private static int NearestMid(double hz)
    {
        var steps = TrackEq.MidFrequencies;
        int best = 0;
        for (int i = 1; i < steps.Count; i++)
        {
            if (Math.Abs(Math.Log(steps[i] / hz)) < Math.Abs(Math.Log(steps[best] / hz)))
                best = i;
        }
        return best;
    }

    /// <summary>Applies EQ to the deck (in half-dB steps, within range) and saves it for the song.</summary>
    private void SetEq(TrackEq eq, bool save = true)
    {
        static double Step(double db, double min, double max) => Math.Round(Math.Clamp(db, min, max) * 2) / 2;
        eq = new TrackEq(Step(eq.Low, TrackEq.MinDb, TrackEq.MaxDb), Step(eq.Mid, TrackEq.MinDb, TrackEq.MaxDb),
            Step(eq.High, TrackEq.MinDb, TrackEq.MaxDb), Step(eq.Gain, TrackEq.MinDb, TrackEq.MaxDb),
            TrackEq.MidFrequencies[NearestMid(eq.MidHzOrDefault)], Math.Round(Math.Clamp(eq.Even, 0, TrackEq.MaxEven) / 5) * 5);
        bool changed = eq != _eq;
        _eq = eq;
        Deck.SetEq(eq.Low, eq.Mid, eq.High, eq.MidHz);
        ApplyEven();
        ApplyGain();
        if (!changed)
            return;
        if (save && Track is not null)
            _owner.SaveEq(Track.Path, eq);
        foreach (string name in new[] { nameof(EqLow), nameof(EqMid), nameof(EqHigh), nameof(EqGain), nameof(EqLowText), nameof(EqMidText),
                     nameof(EqHighText), nameof(EqGainText), nameof(EqEven), nameof(EqEvenText), nameof(EqMidHzText), nameof(IsMidMoved),
                     nameof(IsEqSet), nameof(LevelText) })
            OnPropertyChanged(name);
    }

    // ───────────── Loudness matching ─────────────

    /// <summary>How fast matching may change the level of a song the room is hearing (dB per second).</summary>
    private const double OnAirRampDbPerSecond = 3;

    private double? _lufs, _peakDb;
    private double _matchDb;         // what matching applies now (eases toward the target on air)
    private long _matchMovedAt;

    /// <summary>What loudness matching would apply to this song: 0 if it's off or not measured yet.</summary>
    private double MatchTargetDb => _settings.MatchLoudness && _lufs is { } lufs ? LoudnessMatch.GainDb(lufs, _peakDb, _settings.LoudnessTarget) : 0;

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

    /// <summary>Loudness matching was turned on or off in Settings, or its target changed.</summary>
    internal void LoudnessSettingChanged()
    {
        UpdateMatch(force: !IsOnAir);
        OnPropertyChanged(nameof(LevelText));
    }

    /// <summary>
    /// Moves matching toward its target: at once if the room isn't hearing this deck, gently if
    /// it is (e.g. the analysis arrived while the song was already on air), so nothing jumps.
    /// </summary>
    private void UpdateMatch(bool force = false)
    {
        double target = MatchTargetDb;
        long now = TimeProvider.System.GetTimestamp();
        double elapsed = _matchMovedAt == 0 ? 0 : TimeProvider.System.GetElapsedTime(_matchMovedAt, now).TotalSeconds;
        _matchMovedAt = now;
        if (target == _matchDb)
            return;
        if (force || !IsOnAir || Math.Abs(target - _matchDb) < 0.01)
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

    /// <summary>
    /// A typical song's loudness, for evening out one whose loudness isn't measured yet: a guess,
    /// put right when analysis finishes.
    /// </summary>
    private const double UnmeasuredLufs = -16;

    /// <summary>Evens out the song around its own loudness (a compressor after the EQ).</summary>
    private void ApplyEven() => Deck.Even = new Deck.Evening(_eq.Even / TrackEq.MaxEven, _lufs ?? UnmeasuredLufs);

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
        Log.Info($"Deck {Label}: jumps {(arm ? "armed" : "canceled")}");
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
        if (LeavingProblem(from) is { } problem)
        {
            ShowHint(problem);
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
        if (LandingProblem(from, to) is { } problem)
        {
            ShowHint(problem);
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

    /// <summary>Why a jump can't leave from <paramref name="from"/>, if it can't.</summary>
    private string? LeavingProblem(TimeSpan from) =>
        from < Deck.Start + ShortestJump || from > Deck.End - ShortestJump ? "A jump can't leave that close to the start or end" : null;

    /// <summary>Why a jump from <paramref name="from"/> can't land at <paramref name="to"/>, if it can't.</summary>
    private string? LandingProblem(TimeSpan from, TimeSpan to) =>
        (to - from).Duration() < ShortestJump ? "Land the jump further away"
        : to < Deck.Start ? "A jump can't land before the song starts"
        : to > Deck.End - ShortestJump ? "A jump can't land that close to the end"
        : null;

    /// <summary>
    /// Moves where jump <paramref name="index"/> leaves or lands (dragged on the waveform). Saved
    /// for the song; armed or not, as it was.
    /// </summary>
    private bool MoveJump(int index, TimeSpan? from, TimeSpan? to)
    {
        if (Track is null || index < 0 || index >= _jumps.Count)
            return false;
        var moved = new SongJump(from ?? _jumps[index].From, to ?? _jumps[index].To);
        if ((LeavingProblem(moved.From) ?? LandingProblem(moved.From, moved.To)) is { } problem)
        {
            ShowHint(problem);
            return false;
        }
        _jumps = _jumps.Select((j, i) => i == index ? moved : j).ToList();
        _owner.SaveJumps(Track.Path, _jumps);
        Deck.SetJumps(Deck.Jumps.Select((j, i) => i == index ? (moved.From, moved.To, j.Armed) : (j.From, j.To, j.Armed)).ToList());
        Log.Info(string.Create(CultureInfo.InvariantCulture,
            $"Deck {Label}: jump {index + 1} moved: from {moved.From.TotalSeconds:0.0} s to {moved.To.TotalSeconds:0.0} s in \"{Title}\""));
        OnJumpsChanged();
        return true;
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

    // ───────────── Cues ─────────────

    /// <summary>Two cues closer than this are the same place.</summary>
    private static readonly TimeSpan CueSpacing = TimeSpan.FromSeconds(1);

    private List<SongCue> _cues = [];

    /// <summary>The song's cues, by number: a key for each over the waveform.</summary>
    public IReadOnlyList<SongCue> Cues => _cues;

    public bool HasCues => _cues.Count > 0;

    public bool CanAddCue => _cues.Count < LibraryDatabase.MaxCues;

    /// <summary>The number the next cue gets: the lowest one free.</summary>
    public int NextCueNumber => Enumerable.Range(1, LibraryDatabase.MaxCues).FirstOrDefault(n => _cues.All(c => c.Number != n));

    /// <summary>The cues as fractions of the file, for the waveform.</summary>
    public IReadOnlyList<Controls.CueMarker> CueMarkers => Deck.Length <= TimeSpan.Zero
        ? []
        : _cues.Select(c => new Controls.CueMarker(c.At / Deck.Length, c.Number)).ToList();

    /// <summary>Marks a cue, with the lowest free number, at a point in the song. Saved for the song.</summary>
    public bool AddCue(TimeSpan at)
    {
        if (Track is null || at < TimeSpan.Zero || at >= Deck.Length)
            return false;
        if (!CanAddCue)
        {
            ShowHint("Four cues already: remove one first");
            return false;
        }
        if (_cues.Where(c => (c.At - at).Duration() < CueSpacing).Select(c => c.Number).FirstOrDefault() is > 0 and int near)
        {
            ShowHint($"Cue {near} is already there");
            return false;
        }
        var cue = new SongCue(NextCueNumber, at);
        _cues = [.. _cues.Append(cue).OrderBy(c => c.Number)];
        _owner.SaveCues(Track.Path, _cues);
        Log.Info(string.Create(CultureInfo.InvariantCulture,
            $"Deck {Label}: cue {cue.Number} at {at.TotalSeconds:0.0} s in \"{Title}\""));
        OnCuesChanged();
        return true;
    }

    /// <summary>Forgets a cue; the others keep their numbers.</summary>
    public void RemoveCue(int number)
    {
        if (Track is null || _cues.All(c => c.Number != number))
            return;
        _cues = _cues.Where(c => c.Number != number).ToList();
        _owner.SaveCues(Track.Path, _cues);
        Log.Info($"Deck {Label}: removed cue {number} from \"{Title}\"");
        OnCuesChanged();
    }

    /// <summary>Moves a cue (dragged on the waveform); it keeps its number. Saved for the song.</summary>
    private bool MoveCue(int number, TimeSpan at)
    {
        if (Track is null || _cues.All(c => c.Number != number) || at < TimeSpan.Zero || at >= Deck.Length)
            return false;
        if (_cues.Where(c => c.Number != number && (c.At - at).Duration() < CueSpacing).Select(c => c.Number).FirstOrDefault() is > 0 and int near)
        {
            ShowHint($"Cue {near} is already there");
            return false;
        }
        _cues = _cues.Select(c => c.Number == number ? c with { At = at } : c).ToList();
        _owner.SaveCues(Track.Path, _cues);
        Log.Info(string.Create(CultureInfo.InvariantCulture,
            $"Deck {Label}: cue {number} moved to {at.TotalSeconds:0.0} s in \"{Title}\""));
        OnCuesChanged();
        return true;
    }

    // ───────────── Tap tempo ─────────────

    private readonly TapTempo _tap = new();
    private readonly System.Diagnostics.Stopwatch _tapClock = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>Tapping along to set the song's BPM: the TAP pad shows over the waveform.</summary>
    [ObservableProperty]
    public partial bool IsTapping { get; private set; }

    /// <summary>
    /// The song's BPM from the taps, once there are enough. The DJ taps what they hear, so the
    /// deck's tempo change is taken out: the song's own BPM, as at 0%.
    /// </summary>
    public double? TappedBpm => _tap.Bpm is { } heard ? Math.Round(heard / (1 + Deck.TempoPercent / 100), 1) : null;

    public string TapText => TappedBpm is { } bpm ? Format.Bpm(bpm) : "—";

    public string TapDetail => _tap.Count switch
    {
        0 => "Tap along to the beat",
        < TapTempo.TapsNeeded => $"{_tap.Count} {(_tap.Count == 1 ? "tap" : "taps")}: keep going",
        _ => $"{_tap.Count} taps · {(_tap.IsSteady ? "steady" : "keep tapping")}",
    };

    /// <summary>With the tempo changed, what the DJ is hearing (else nothing).</summary>
    public string? TapTempoNote => _tap.Bpm is { } heard && Deck.TempoPercent != 0
        ? $"Deck at {TempoText}: you're hearing {Format.Bpm(Math.Round(heard, 1))}"
        : null;

    /// <summary>BPM menu › Tap tempo: shows the TAP pad over the waveform.</summary>
    public void StartTapTempo()
    {
        if (Track is null)
            return;
        _tap.Reset();
        IsTapping = true;
        OnTapChanged();
    }

    /// <summary>The TAP pad (or Space on it).</summary>
    [RelayCommand]
    private void Tap() => Tap(_tapClock.Elapsed);

    internal void Tap(TimeSpan at)
    {
        if (!IsTapping)
            return;
        _tap.Tap(at);
        OnTapChanged();
    }

    /// <summary>SET: the tapped BPM becomes the song's, as if typed (in GZJam's database, never the file).</summary>
    [RelayCommand(CanExecute = nameof(CanSetTappedBpm))]
    private void SetTappedBpm()
    {
        if (Track is not { } track || TappedBpm is not { } bpm)
            return;
        Log.Info(string.Create(CultureInfo.InvariantCulture,
            $"Deck {Label}: tapped {bpm:0.0} BPM for \"{Title}\" ({_tap.Count} taps, tempo {TempoText})"));
        CancelTap();
        _owner.SetManualBpm([track], bpm);
    }

    private bool CanSetTappedBpm => TappedBpm is not null;

    [RelayCommand]
    private void CancelTap()
    {
        IsTapping = false;
        _tap.Reset();
        OnTapChanged();
    }

    private void OnTapChanged()
    {
        OnPropertyChanged(nameof(TappedBpm));
        OnPropertyChanged(nameof(TapText));
        OnPropertyChanged(nameof(TapDetail));
        OnPropertyChanged(nameof(TapTempoNote));
        SetTappedBpmCommand.NotifyCanExecuteChanged();
    }

    // ───────────── Dragging markers ─────────────

    /// <summary>
    /// A marker dragged on the waveform and let go at <paramref name="fraction"/> of the file: the
    /// start or end, either end of a jump (<paramref name="index"/>: which jump), or a cue (its
    /// number). Saved for the song; false, with a hint, if it can't go there (it goes back).
    /// Allowed on air: moving a point changes nothing the room hears until the music gets there,
    /// and the end can't be moved before what's playing.
    /// </summary>
    public bool MoveMarker(Controls.MarkerKind kind, int index, double fraction)
    {
        if (Track is null || Deck.Length <= TimeSpan.Zero)
            return false;
        // To the millisecond, as it's saved: the same when the song is loaded again.
        TimeSpan at = TimeSpan.FromMilliseconds(Math.Round((Deck.Length * Math.Clamp(fraction, 0, 1)).TotalMilliseconds));
        return kind switch
        {
            Controls.MarkerKind.Start => SetStart(at),
            Controls.MarkerKind.End => SetEnd(at),
            Controls.MarkerKind.JumpFrom => MoveJump(index, from: at, to: null),
            Controls.MarkerKind.JumpTo => MoveJump(index, from: null, to: at),
            Controls.MarkerKind.Cue => MoveCue(index, at),
            _ => false,
        };
    }

    /// <summary>
    /// A cue key: carry on from the cue, now. Allowed on air, unlike seeking: it's a deliberate
    /// tap on a key, made at the moment the DJ chooses (the end of a phrase, say).
    /// </summary>
    [RelayCommand]
    private void GoToCue(int number)
    {
        if (_cues.Where(c => c.Number == number).Select(c => (TimeSpan?)c.At).FirstOrDefault() is not { } at)
            return;
        Deck.Seek(at);
        _lastPosition = Deck.Position;   // a jump isn't listening
        Log.Info($"Deck {Label}: went to cue {number} (\"{Title}\"){(IsOnAir ? " on air" : "")}");
        Refresh();
    }

    private void OnCuesChanged()
    {
        OnPropertyChanged(nameof(Cues));
        OnPropertyChanged(nameof(HasCues));
        OnPropertyChanged(nameof(CanAddCue));
        OnPropertyChanged(nameof(NextCueNumber));
        OnPropertyChanged(nameof(CueMarkers));
    }

    /// <summary>A song can't be trimmed shorter than this.</summary>
    private static readonly TimeSpan ShortestSong = TimeSpan.FromSeconds(5);

    /// <summary>Shown briefly over the position bar, e.g. "Tap again to pause".</summary>
    [ObservableProperty]
    public partial string? Hint { get; private set; }

    /// <summary>The first of two taps needed to pause an on-air deck has happened.</summary>
    [ObservableProperty]
    public partial bool IsPauseArmed { get; private set; }

    /// <summary>The first of two taps needed to fade out an on-air deck has happened (#130).</summary>
    [ObservableProperty]
    public partial bool IsFadeArmed { get; private set; }

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

    public string Status => IsOnAir ? "ON AIR" : IsOffAir ? "OFF AIR"
        : StartsIn is { } left ? $"STARTS IN {Math.Max(1, Math.Ceiling(left)):0}" : "";

    /// <summary>
    /// Auto-play starts this deck after the gap, this many seconds from now; null if it isn't
    /// waiting. Shown where ON AIR goes, counting down, with the light blinking.
    /// </summary>
    public double? StartsIn
    {
        get;
        set
        {
            if (field == value)
                return;
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsStarting));
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(StatusLight));
        }
    }

    /// <summary>Waiting out auto-play's gap: the status line shows, as when playing.</summary>
    public bool IsStarting => StartsIn is not null;

    /// <summary>The status line's light: on air, or blinking (twice a second) while it counts down to starting.</summary>
    public bool StatusLight => IsOnAir || StartsIn is { } left && left % 0.5 >= 0.25;

    /// <summary>The status line shows: playing, or about to.</summary>
    public bool ShowsStatus => IsPlaying || IsStarting;

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

    /// <summary>
    /// How far into the song, 0..1, for the on-air bar (#165): from where the music starts to where
    /// it ends, in real time, as the time left counts it (after trimmed silence, with the tempo, armed
    /// jumps and an auto-play fade). So the bar is full just as the time left reaches 0:00.
    /// </summary>
    public double AirProgress
    {
        get
        {
            if (!HasTrack)
                return 0;
            double speed = 1 + Deck.TempoPercent / 100;
            double played = Math.Max(0, (Deck.Position - Deck.Start).TotalSeconds / speed);
            double total = played + Deck.Remaining.TotalSeconds;
            return total > 0 ? played / total : 1;
        }
    }

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
            if (IsTapping)
                OnTapChanged();   // the song's BPM from the same taps
        }
    }

    public string TempoText => Deck.TempoPercent.ToString("+0.0'%';-0.0'%';'0.0%'", CultureInfo.CurrentCulture);

    public string BpmText => CurrentBpm is { } bpm ? Format.Bpm(Math.Round(bpm, 1)) : "—";

    public string BpmCaption => TrackBpm is { } original
        ? Deck.TempoPercent == 0 ? "BPM" : $"BPM · from {Format.Bpm(original)}"
        : "BPM unknown";

    /// <summary>The play and CUE keys' tooltips, with their keyboard shortcuts (#132).</summary>
    public string PlayTip => $"Play / pause · key: {Shortcuts.Command}+{Label}";
    public string CueTip => $"Cue: send this deck to your headphones · key: {Shortcuts.Command}+Shift+{Label}";

    public string ReadoutText => Readout switch
    {
        DeckReadout.Elapsed => Format.Duration(Deck.Position),
        DeckReadout.Bpm => BpmText,
        _ => HasTrack ? "-" + Format.Duration(Deck.Remaining) : "-0:00",
    };

    /// <summary>How long before the music ends the readout starts blinking, as on CDJs.</summary>
    internal static readonly TimeSpan EndingWarning = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The song the room hears is in its last <see cref="EndingWarning"/> (to where its music
    /// ends: after trimmed silence, with the tempo and armed jumps counted): get the next one ready.
    /// </summary>
    public bool IsEnding => IsOnAir && Deck.Remaining is var left && left > TimeSpan.Zero && left <= EndingWarning;

    /// <summary>The readout blinks slowly while <see cref="IsEnding"/>: dimmed for the second half of each second left.</summary>
    public bool IsReadoutDimmed => IsEnding && Deck.Remaining.TotalSeconds % 1 < 0.5;

    public string ReadoutCaption => Readout switch
    {
        DeckReadout.Elapsed => $"elapsed of {Format.Duration(Deck.Length)}",
        DeckReadout.Bpm => $"{BpmCaption} · {TempoText}",
        _ => Deck.FadesAt is { } fadesAt
            ? $"{Format.Duration(Deck.Position)} / fade {Format.Duration(fadesAt)}"   // auto-play's limit (#98)
            : $"{Format.Duration(Deck.Position)} / {Format.Duration(Deck.Length)}",
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
            Arm(fade: false);
            Log.Info($"Deck {Label}: pause armed (needs a second tap)");
            return;
        }
        else
        {
            Pause("tap");
        }
        Refresh();
    }

    /// <summary>A completed press-and-hold on play/pause: always pauses. Returns whether it paused.</summary>
    public bool HoldPause()
    {
        bool paused = IsPlaying;
        if (paused)
            Pause("hold");
        Refresh();
        return paused;
    }

    /// <summary>
    /// The press-and-hold that just paused was a quick tap after all: the screen froze, and the
    /// hold's timer came due before the release was seen (#81). Plays on from where it paused.
    /// </summary>
    public void UndoHoldPause()
    {
        if (IsPlaying || !HasTrack)
            return;
        Deck.Play();
        Log.Info($"Deck {Label}: that was a tap during a screen freeze, not a hold: playing on");
        Refresh();
    }

    private void Pause(string how)
    {
        Disarm();
        Deck.Pause();
        Log.Info($"Deck {Label}: pause ({how}){(Deck.IsPlaying ? " -- STILL PLAYING" : "")}");
    }

    private async void Arm(bool fade = false)
    {
        Disarm();   // one key armed at a time: the second tap must be on the same key
        var armed = _armed = new CancellationTokenSource();
        if (fade)
            IsFadeArmed = true;
        else
            IsPauseArmed = true;
        ShowHint(fade ? FadeHint : PauseHint);
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

    private const string PauseHint = "Tap again to pause", FadeHint = "Tap again to fade out";

    private void Disarm()
    {
        _armed?.Cancel();
        _armed = null;
        IsPauseArmed = IsFadeArmed = false;
        if (Hint is PauseHint or FadeHint)
            Hint = null;
    }

    /// <summary>
    /// FADE OUT. On the deck the room hears, with two taps to pause (Settings), it takes two taps
    /// too: a fade ends the dance as surely as a pause (#130).
    /// </summary>
    [RelayCommand]
    private void FadeOut()
    {
        if (!IsPlaying)
            return;
        if (PauseNeedsConfirm && !IsFadeArmed)
        {
            Arm(fade: true);
            Log.Info($"Deck {Label}: fade out armed (needs a second tap)");
            return;
        }
        Disarm();
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
        CancelTap();
        Track = null;
        Waveform = null;
        ForgetDetail();
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
        _cues = [];
        OnCuesChanged();
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
        CancelTap();   // the taps were for the song before
        IsFinished = false;
        ResetListening();
        Deck.Load(track.Path);
        Deck.TempoPercent = 0;
        Track = track;
        Waveform = null;
        ForgetDetail();
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
        _cues = _owner.GetCues(track.Path).ToList();
        OnCuesChanged();
        if (IsEqSet)
            Log.Info($"Deck {Label}: EQ for this song: low {EqLowText}, mid {EqMidText} at {EqMidHzText}, high {EqHighText}, gain {EqGainText}, even {EqEvenText}");
        Refresh();
        _ = AnalyzeAsync(track);
        _ = LoadDetailAsync(track);
    }

    /// <summary>
    /// The song on this deck was renamed or moved on disk (#87): it's the same song at its new
    /// path, kept as it is (playing, paused, its marks), and loaded from there next time.
    /// </summary>
    internal void FollowMove(Track track)
    {
        Track = track;
        Log.Info($"Deck {Label}: \"{track.DisplayTitle}\" is now at {track.Path}");
    }

    /// <summary>
    /// Brings back a deck as it was before a crash or a close (#96, #117): the song, paused at
    /// <paramref name="position"/> (zero for its start), at its tempo, with its cue button. Never
    /// playing by itself.
    /// </summary>
    internal void Restore(Track track, TimeSpan position, double tempoPercent, bool cue)
    {
        Load(track);
        if (position > Deck.Start && position < Deck.End)
        {
            Deck.Seek(position);
            _lastPosition = Deck.Position;   // a jump isn't listening
        }
        TempoPercent = tempoPercent;
        IsCueOn = cue;
        Log.Info($"Deck {Label}: back at {Format.Duration(Deck.Position)}, tempo {TempoText}{(cue ? ", cue on" : "")}");
        Refresh();
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
        ApplyEven();
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
    /// moves to the start, skipping the silence (or applause) before it, and one waiting at its
    /// start follows it, earlier or later; mid-track, it stays put.
    /// </summary>
    private void ApplyTrim()
    {
        TimeSpan start = _manualTrim.Start ?? AutomaticStart, end = _manualTrim.End ?? AutomaticEnd;
        bool atStart = (Deck.Position - Deck.Start).Duration() < TimeSpan.FromMilliseconds(20);
        Deck.SetTrim(start, end);
        if (!IsPlaying && !IsFinished && (Deck.Position < start || atStart))
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

    /// <summary>
    /// The deck's level, 0..1 along its meter, falling back smoothly: after its gain and EQ, before
    /// the crossfader, so a deck the room can't hear shows its level too.
    /// </summary>
    [ObservableProperty]
    public partial double Level { get; private set; }

    /// <summary>Reads the deck's level for its meter; called on every playback tick.</summary>
    public void UpdateMeter() =>
        Level = Math.Max(Controls.LevelMeter.FromPeak(Deck.TakePeak()), Level - Controls.LevelMeter.Fall);

    /// <summary>Re-reads everything that changes as the track plays.</summary>
    public void Refresh()
    {
        TrackListening();
        UpdateMatch();
        if ((IsPauseArmed || IsFadeArmed) && !PauseNeedsConfirm)
            Disarm();   // no longer on air (or the setting changed): a single tap pauses or fades again
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(IsInRoom));
        OnPropertyChanged(nameof(IsOnAir));
        OnPropertyChanged(nameof(IsOffAir));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusLight));
        OnPropertyChanged(nameof(ShowsStatus));
        OnPropertyChanged(nameof(CanSeek));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(AirProgress));
        OnPropertyChanged(nameof(PauseNeedsConfirm));
        OnPropertyChanged(nameof(IsCueOn));
        OnPropertyChanged(nameof(IsFinished));
        OnPropertyChanged(nameof(IsReady));
        RefreshReadout();
    }

    /// <summary>The track's BPM changed (typed by the DJ, say): show it.</summary>
    internal void TrackBpmChanged()
    {
        OnPropertyChanged(nameof(TrackBpm));
        RefreshReadout();
    }

    private void RefreshReadout()
    {
        OnPropertyChanged(nameof(CurrentBpm));
        OnPropertyChanged(nameof(BpmText));
        OnPropertyChanged(nameof(BpmCaption));
        OnPropertyChanged(nameof(ReadoutText));
        OnPropertyChanged(nameof(ReadoutCaption));
        OnPropertyChanged(nameof(IsEnding));
        OnPropertyChanged(nameof(IsReadoutDimmed));
    }
}
