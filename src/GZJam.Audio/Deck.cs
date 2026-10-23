using ManagedBass;
using ManagedBass.Fx;
using ManagedBass.Mix;

namespace GZJam.Audio;

/// <summary>
/// One track player. The file is decoded through a BASS_FX tempo stream (tempo changes keep the
/// pitch) and three EQ filters, then split so the same audio feeds both the main and the cue bus.
/// The crossfader sets the main split's volume; <see cref="Cue"/> switches the cue split on or off
/// (and <see cref="MixEngine.PhonesMix"/> lets the headphones hear the room too).
/// </summary>
public sealed class Deck : IDisposable
{
    private readonly MixEngine _mix;
    private int _tempo;        // decode stream (owns the file stream)
    private int _splitMain;    // -> main bus
    private int _splitCue;     // -> cue bus
    private bool _playing;
    private bool _ended;
    private double _tempoPercent;
    private double _volume = 1;
    private double _gain = 1;   // loudness matching and the DJ's own gain, as a factor
    private double _mainGain;
    private bool _cue;
    private long _fadeStarted;
    private TimeSpan? _fadeLength;
    private TimeSpan _start;
    private TimeSpan? _end;
    private readonly double[] _eqDb = new double[3];   // low, mid, high
    private readonly List<Jump> _jumps = [];
    private readonly SyncProcedure _jumpSync;   // kept alive: BASS holds only a pointer to it
    private volatile bool _jumped;               // set on the audio thread, reported by Update
    private readonly int[] _eqFx = new int[3];
    private readonly DSPProcedure _meterDsp;     // kept alive: BASS calls it from its mixing thread
    private float _peak;                         // since the last TakePeak, set on the audio thread

    internal Deck(MixEngine mix, int index)
    {
        _mix = mix;
        Index = index;
        _jumpSync = OnJumpSync;
        _meterDsp = Measure;
    }

    public int Index { get; }

    /// <summary>"1", "2", ...</summary>
    public string Name => (Index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string? TrackPath { get; private set; }
    public bool IsLoaded => _tempo != 0;
    public bool IsPlaying => _playing;
    public bool IsFading => _fadeLength is not null;

    /// <summary>
    /// Raised from <see cref="MixEngine.Update"/> when the track plays to its end or a fade-out
    /// finishes. Either way the music on this deck has stopped.
    /// </summary>
    public event EventHandler? Finished;

    /// <summary>Raised from <see cref="MixEngine.Update"/> after an armed jump has happened.</summary>
    public event EventHandler? Jumped;

    public void Load(string path)
    {
        Unload();
        _start = TimeSpan.Zero;
        _end = null;
        _mix.Engine.UseNoSoundDevice();

        // Prescan gives exact length and seeking for VBR MP3s.
        int source = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float | BassFlags.Prescan);
        if (source == 0)
            throw AudioException.CouldNotOpen(path);

        int tempo = BassFx.TempoCreate(source, BassFlags.Decode | BassFlags.FxFreeSource);
        if (tempo == 0)
        {
            Errors error = Bass.LastError;
            Bass.StreamFree(source);
            throw new AudioException($"Could not create tempo stream for '{path}'", error);
        }

        _tempo = tempo;
        CreateEq();
        SetJumpSyncs();
        _splitMain = CreateSplit();
        _splitCue = CreateSplit();
        Bass.ChannelSetDSP(_splitMain, _meterDsp);
        TrackPath = path;
        _playing = false;
        _ended = false;
        Attach();
        ApplyTempo();
        ApplyVolumes();
    }

    public void Unload()
    {
        CancelFade();
        foreach (Jump jump in _jumps)
            jump.Sync = 0;   // freed with the stream
        if (_tempo != 0)
        {
            Detach();
            Bass.StreamFree(_splitMain);
            Bass.StreamFree(_splitCue);
            Bass.StreamFree(_tempo);
        }
        _tempo = _splitMain = _splitCue = 0;
        Array.Clear(_eqFx);   // freed with the stream
        _playing = false;
        _ended = false;
        TrackPath = null;
    }

    public void Play()
    {
        if (!IsLoaded)
            return;
        if (_ended)
            Seek(_start);
        _playing = true;
        SetPaused(false);
    }

    public void Pause()
    {
        CancelFade();
        _playing = false;
        SetPaused(true);
    }

    /// <summary>Fades the deck out over <paramref name="length"/>, then pauses it and raises <see cref="Finished"/>.</summary>
    public void FadeOut(TimeSpan length)
    {
        if (!_playing)
            return;
        _fadeStarted = _mix.Time.GetTimestamp();
        _fadeLength = length > TimeSpan.Zero ? length : TimeSpan.FromMilliseconds(1);
    }

    public void Seek(TimeSpan position)
    {
        if (!IsLoaded)
            return;

        if (_ended)
        {
            // A finished source can't be rewound inside the mixer; reload it cleanly instead.
            string path = TrackPath!;
            bool wasPlaying = _playing;
            var (start, end) = (_start, _end);
            Load(path);
            (_start, _end) = (start, end);
            _playing = wasPlaying;
        }

        long bytes = Bass.ChannelSeconds2Bytes(_tempo, Math.Clamp(position.TotalSeconds, 0, Length.TotalSeconds));
        Bass.ChannelSetPosition(_tempo, bytes);
        BassMix.SplitStreamReset(_tempo);   // drop audio the splitters buffered from the old position
        SetPaused(!_playing);
    }

    /// <summary>Position in the track (not affected by tempo).</summary>
    public TimeSpan Position => !IsLoaded
        ? TimeSpan.Zero
        : TimeSpan.FromSeconds(Bass.ChannelBytes2Seconds(_tempo, Bass.ChannelGetPosition(_tempo)));

    public TimeSpan Length => !IsLoaded
        ? TimeSpan.Zero
        : TimeSpan.FromSeconds(Bass.ChannelBytes2Seconds(_tempo, Bass.ChannelGetLength(_tempo)));

    /// <summary>Where the music starts: playing from the top starts here, skipping silence.</summary>
    public TimeSpan Start => _start;

    /// <summary>Where the music ends: the track counts as finished here, so trailing silence isn't played.</summary>
    public TimeSpan End => _end is { } end && end < Length ? end : Length;

    /// <summary>
    /// Sets where the music starts and ends (from analysis). Doesn't move the playhead; a track
    /// that's still at the top can be moved to <paramref name="start"/> with <see cref="Seek"/>.
    /// </summary>
    public void SetTrim(TimeSpan start, TimeSpan end)
    {
        _start = start < TimeSpan.Zero ? TimeSpan.Zero : start;
        _end = end > _start ? end : null;
    }

    /// <summary>Real time left until the music ends, accounting for tempo and the jumps still armed ahead.</summary>
    public TimeSpan Remaining
    {
        get
        {
            TimeSpan position = Position, left = End - position;
            foreach (Jump jump in _jumps)
            {
                if (jump.Armed && jump.From > position)
                    left -= jump.To - jump.From;   // forward: skipped; backward: played again
            }
            return Max(TimeSpan.Zero, left / (1 + _tempoPercent / 100));
        }
    }

    // ───────────── Jumps ─────────────

    /// <summary>
    /// A seamless jump from the end of one phrase to another: forward to skip a part, backward to
    /// play one again. When armed it happens once, at the exact sample, as the music reaches
    /// <see cref="From"/>; then it disarms.
    /// </summary>
    public sealed class Jump(TimeSpan from, TimeSpan to)
    {
        public TimeSpan From { get; } = from;
        public TimeSpan To { get; } = to;
        public bool Armed { get; internal set; }
        internal int Sync { get; set; }
    }

    /// <summary>The track's jumps, in the order they were set.</summary>
    public IReadOnlyList<Jump> Jumps => _jumps;

    /// <summary>Replaces the jumps (each armed or not). Kept across loads until replaced.</summary>
    public void SetJumps(IEnumerable<(TimeSpan From, TimeSpan To, bool Armed)> jumps)
    {
        RemoveJumpSyncs();
        _jumps.Clear();
        foreach (var (from, to, armed) in jumps)
            _jumps.Add(new Jump(from, to) { Armed = armed });
        SetJumpSyncs();
    }

    /// <summary>Arms or disarms every jump (the deck's JUMP key).</summary>
    public void ArmJumps(bool armed)
    {
        RemoveJumpSyncs();
        foreach (Jump jump in _jumps)
            jump.Armed = armed;
        SetJumpSyncs();
    }

    private void SetJumpSyncs()
    {
        if (!IsLoaded)
            return;
        foreach (Jump jump in _jumps.Where(j => j.Armed && j.Sync == 0))
        {
            long at = Bass.ChannelSeconds2Bytes(_tempo, jump.From.TotalSeconds);
            // Mixtime: called as the audio is decoded, so moving the position there is seamless.
            jump.Sync = Bass.ChannelSetSync(_tempo, SyncFlags.Position | SyncFlags.Mixtime | SyncFlags.Onetime, at, _jumpSync, (IntPtr)_jumps.IndexOf(jump));
        }
    }

    private void RemoveJumpSyncs()
    {
        foreach (Jump jump in _jumps.Where(j => j.Sync != 0))
        {
            if (IsLoaded)
                Bass.ChannelRemoveSync(_tempo, jump.Sync);
            jump.Sync = 0;
        }
    }

    /// <summary>On the audio thread, as the music reaches a jump: carry on from where it lands.</summary>
    private void OnJumpSync(int handle, int channel, int data, IntPtr user)
    {
        int index = (int)user;
        if (index < 0 || index >= _jumps.Count || _jumps[index] is not { Armed: true } jump)
            return;
        jump.Armed = false;
        jump.Sync = 0;
        Bass.ChannelSetPosition(channel, Bass.ChannelSeconds2Bytes(channel, jump.To.TotalSeconds));
        _jumped = true;
    }

    /// <summary>Tempo change in percent; -8 plays 8% slower. Pitch is preserved.</summary>
    public double TempoPercent
    {
        get => _tempoPercent;
        set
        {
            _tempoPercent = Math.Clamp(value, -50, 50);
            ApplyTempo();
        }
    }

    // EQ: shelves below 150 Hz and above 5 kHz (where record hiss lives), a bell around 1 kHz.
    private static readonly (BQFType Type, float Hz)[] EqBands =
        [(BQFType.LowShelf, 150), (BQFType.PeakingEQ, 1000), (BQFType.HighShelf, 5000)];

    /// <summary>EQ in dB (0 = flat), heard in the room and the headphones alike. Kept across loads.</summary>
    public (double Low, double Mid, double High) Eq => (_eqDb[0], _eqDb[1], _eqDb[2]);

    public void SetEq(double low, double mid, double high)
    {
        (_eqDb[0], _eqDb[1], _eqDb[2]) = (low, mid, high);
        for (int band = 0; band < EqBands.Length; band++)
            ApplyEq(band);
    }

    private void CreateEq()
    {
        for (int band = 0; band < EqBands.Length; band++)
        {
            _eqFx[band] = Bass.ChannelSetFX(_tempo, EffectType.BQF, band);
            if (_eqFx[band] == 0)
                throw new AudioException($"Could not add EQ to Deck {Name}", Bass.LastError);
            ApplyEq(band);
        }
    }

    private void ApplyEq(int band)
    {
        if (_eqFx[band] == 0)
            return;
        var (type, hz) = EqBands[band];
        bool shelf = type != BQFType.PeakingEQ;
        // BASS takes a shelf's slope or a bell's width (Q), and refuses a filter given both.
        var parameters = new BQFParameters
        {
            lFilter = type,
            fCenter = hz,
            fGain = (float)_eqDb[band],
            fBandwidth = 0,
            fQ = shelf ? 0 : 0.7f,   // the bell: about two octaves wide
            fS = shelf ? 0.8f : 0,   // the shelves: a gentle slope
            lChannel = FXChannelFlags.All,
        };
        if (!Bass.FXSetParameters(_eqFx[band], parameters))
            throw new AudioException($"Could not set Deck {Name}'s EQ", Bass.LastError);
    }

    /// <summary>
    /// Gain in dB on top of everything else (loudness matching plus the DJ's adjustment), heard in
    /// the room and the headphones alike. Kept across loads.
    /// </summary>
    public double GainDb
    {
        get => 20 * Math.Log10(_gain);
        set
        {
            _gain = Math.Pow(10, Math.Clamp(value, -30, 20) / 20);
            ApplyVolumes();
        }
    }

    /// <summary>Deck volume, 0.0 to 1.0, on top of the crossfader.</summary>
    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            ApplyVolumes();
        }
    }

    /// <summary>How much of this deck the speakers get (0..1), from the crossfader.</summary>
    public double MainGain => _mainGain;

    /// <summary>Whether the room can hear this deck (when it's playing).</summary>
    /// <remarks>Above about -20 dB: a deck the crossfader has left at a whisper doesn't count.</remarks>
    public bool IsInRoom => _mainGain >= 0.1;

    /// <summary>Send this deck to the headphones (the cue button). Independent of the crossfader.</summary>
    public bool Cue
    {
        get => _cue;
        set
        {
            _cue = value;
            ApplyVolumes();
        }
    }

    internal void SetMainGain(double mainGain)
    {
        _mainGain = mainGain;
        ApplyVolumes();
    }

    /// <summary>
    /// The deck's loudest sample since the last call (1 = full scale), after its gain and EQ but
    /// before the crossfader, like a mixer's channel meter: a deck the room can't hear still shows
    /// its level. 0 while paused. For the deck's meter: call it regularly.
    /// </summary>
    public float TakePeak() => Interlocked.Exchange(ref _peak, 0);

    /// <summary>On the audio thread, as the speakers' mix takes the deck's audio (before its volume is applied).</summary>
    private unsafe void Measure(int handle, int channel, IntPtr buffer, int length, IntPtr user)
    {
        var samples = new ReadOnlySpan<float>((void*)buffer, length / sizeof(float));
        float peak = 0;
        foreach (float sample in samples)
            peak = Math.Max(peak, Math.Abs(sample));
        peak *= (float)_gain;
        float current;
        while (peak > (current = Volatile.Read(ref _peak)) && Interlocked.CompareExchange(ref _peak, peak, current) != current)
        {
        }
    }

    internal void Update()
    {
        if (!IsLoaded)
            return;
        if (_jumped)
        {
            _jumped = false;
            Jumped?.Invoke(this, EventArgs.Empty);
        }

        if (_fadeLength is { } length)
        {
            if (_mix.Time.GetElapsedTime(_fadeStarted) >= length)
            {
                Pause();   // also ends the fade and restores volume for the next play
                Finished?.Invoke(this, EventArgs.Empty);
                return;
            }
            ApplyVolumes();
        }

        // The end of the music (trailing silence is trimmed), or the end of the file.
        if (_playing && !_ended && (
                (_end is not null && Position >= End) ||
                (Bass.ChannelIsActive(_splitMain) == PlaybackState.Stopped && Position >= Length - TimeSpan.FromMilliseconds(250))))
        {
            _ended = true;
            _playing = false;
            SetPaused(true);
            Finished?.Invoke(this, EventArgs.Empty);
        }
    }

    internal void Attach()
    {
        if (!IsLoaded)
            return;
        // Added paused; DownMix folds surround files to stereo, mono is spread to both sides.
        var flags = BassFlags.MixerChanDownMix | BassFlags.MixerChanPause;
        if (!BassMix.MixerAddChannel(_mix.Main.Handle, _splitMain, flags) ||
            !BassMix.MixerAddChannel(_mix.Cue.Handle, _splitCue, flags))
            throw new AudioException($"Could not route Deck {Name}", Bass.LastError);
        SetPaused(!_playing);
        ApplyVolumes();
    }

    internal void Detach()
    {
        if (!IsLoaded)
            return;
        BassMix.MixerRemoveChannel(_splitMain);
        BassMix.MixerRemoveChannel(_splitCue);
    }

    private int CreateSplit()
    {
        int split = BassMix.CreateSplitStream(_tempo, BassFlags.Decode, null);
        if (split == 0)
            throw new AudioException($"Could not split Deck {Name}", Bass.LastError);
        return split;
    }

    private void SetPaused(bool paused)
    {
        if (!IsLoaded)
            return;
        foreach (int split in new[] { _splitMain, _splitCue })
        {
            if (paused)
                BassMix.ChannelAddFlag(split, BassFlags.MixerChanPause);
            else
                BassMix.ChannelRemoveFlag(split, BassFlags.MixerChanPause);
        }
    }

    private void CancelFade()
    {
        if (_fadeLength is null)
            return;
        _fadeLength = null;
        ApplyVolumes();
    }

    private double FadeFactor => _fadeLength is { } length
        ? Math.Max(0, 1 - _mix.Time.GetElapsedTime(_fadeStarted) / length)
        : 1;

    internal void ApplyVolumes()
    {
        if (!IsLoaded)
            return;
        double level = _volume * _gain * FadeFactor;
        Bass.ChannelSetAttribute(_splitMain, ChannelAttribute.Volume, _mainGain * level);
        // The headphones: this deck if it's cued, and as much as the room hears of it, by the
        // phones mix. Cued and in the room, it's the louder of the two, not both added up.
        double phones = Math.Max(_cue ? _mix.PhonesCueLevel : 0, _mainGain * _mix.PhonesRoomLevel);
        Bass.ChannelSetAttribute(_splitCue, ChannelAttribute.Volume, phones * level);
    }

    private void ApplyTempo()
    {
        if (IsLoaded)
            Bass.ChannelSetAttribute(_tempo, ChannelAttribute.Tempo, _tempoPercent);
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    public void Dispose() => Unload();
}
