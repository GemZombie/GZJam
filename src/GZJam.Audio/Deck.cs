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
public sealed unsafe class Deck : IDisposable
{
    private readonly MixEngine _mix;
    private int _tempo;        // decode stream (owns the file stream)
    private int _splitMain;    // -> main bus
    private int _splitCue;     // -> cue bus
    private int _splitBooth;   // -> booth bus
    private bool _playing;
    private bool _ended;
    private double _tempoPercent;
    private double _volume = 1;
    private SongMemory? _memory;   // the song file, read whole: what the deck plays from (#346)
    private double _gain = 1;   // loudness matching and the DJ's own gain, as a factor
    private double _mainGain;
    private bool _cue;
    private long _fadeStarted;
    private TimeSpan? _fadeLength;
    private TimeSpan _start;
    private TimeSpan? _end;
    private TimeSpan _lastPosition;   // at the last update, to see the playhead pass the auto fade
    private readonly SongSound _sound;           // EQ and evening out, on the tempo stream
    private readonly List<Jump> _jumps = [];
    // The audio thread's (native, #344): one per jump by place in the list, and the meter.
    // Freed with the deck, once BASS can't call with them.
    private readonly List<IntPtr> _jumpStates = [];
    private readonly NativeDsp.Meter* _meter;
    private bool _jumped;   // a jump happened, for Update to report
    private bool _disposed;

    internal Deck(MixEngine mix, int index)
    {
        _mix = mix;
        Index = index;
        _sound = new SongSound($"Deck {Name}");
        _meter = NativeDsp.NewMeter();
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

    /// <summary>Loads <paramref name="path"/> now, on this thread: <see cref="Open"/>, then <see cref="Load(OpenedSong)"/>.</summary>
    public void Load(string path) => Load(Open(path));

    /// <summary>
    /// The slow part of loading a song (#361): read into memory (or found read ahead), opened, and
    /// decoded through its tempo stream. Safe on any thread, so the window needn't wait for a slow
    /// disk; the deck isn't touched. Hand the result to <see cref="Load(OpenedSong)"/>, or dispose of it.
    /// Throws an <see cref="AudioException"/>, saying why, if the song can't be played.
    /// </summary>
    public OpenedSong Open(string path)
    {
        _mix.Engine.UseNoSoundDevice();   // this thread's: decoding never touches a real output

        // The whole file in memory, so the music never waits for the disk (#346).
        SongMemory? memory = _mix.Songs.Take(path);
        int source;
        try
        {
            source = SongFile.Open(path, memory);
        }
        catch
        {
            memory?.Release();
            throw;
        }

        int tempo = BassFx.TempoCreate(source, BassFlags.Decode | BassFlags.FxFreeSource);
        if (tempo == 0)
        {
            Errors error = Bass.LastError;
            Bass.StreamFree(source);
            memory?.Release();
            throw new AudioException($"Could not create tempo stream for '{path}'", error);
        }
        return new OpenedSong(path, tempo, memory);
    }

    /// <summary>Puts a song opened by <see cref="Open"/> on the deck, in place of what's there. Quick: for the window's thread.</summary>
    public void Load(OpenedSong song)
    {
        Unload();
        _start = TimeSpan.Zero;
        _end = null;
        (_tempo, _memory) = song.Take();
        string path = song.Path;
        _sound.Attach(_tempo);
        SetJumpSyncs();
        _splitMain = CreateSplit();
        _splitCue = CreateSplit();
        _splitBooth = CreateSplit();
        NativeDsp.SetMeterDsp(_splitMain, _meter);
        TrackPath = path;
        _playing = false;
        _ended = false;
        _endFading = false;
        _lastPosition = TimeSpan.Zero;
        AutoFade = null;
        Attach();
        ApplyTempo();
        ApplyVolumes();
    }

    public void Unload()
    {
        CancelFade();
        TakeJumps();
        foreach (Jump jump in _jumps)
            jump.Forget();   // the sync is freed with the stream
        if (_tempo != 0)
        {
            Detach();
            Bass.StreamFree(_splitMain);
            Bass.StreamFree(_splitCue);
            Bass.StreamFree(_splitBooth);
            Bass.StreamFree(_tempo);
        }
        _memory?.Release();   // after its streams: BASS no longer reads it
        _memory = null;
        _tempo = _splitMain = _splitCue = _splitBooth = 0;
        _sound.Detach();   // freed with the stream
        _playing = false;
        _ended = false;
        _endFading = false;
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

    /// <summary>
    /// Real time left until the music ends, accounting for tempo and the jumps still armed ahead,
    /// the song's own <see cref="EndFade"/>, and an <see cref="AutoFade"/> still ahead: then it's
    /// until that fade is over.
    /// </summary>
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
            double speed = 1 + _tempoPercent / 100;
            if (_endFading && _fadeLength is { } fading)
                return Max(TimeSpan.Zero, fading - _mix.Time.GetElapsedTime(_fadeStarted));
            TimeSpan toEnd = Max(TimeSpan.Zero, left / speed) + (EndFade ?? TimeSpan.Zero);
            return FadesAt is { } at ? Min(toEnd, (at - position) / speed + AutoFade!.Value.Length) : toEnd;
        }
    }

    /// <summary>
    /// Fade out by itself when the playhead reaches <c>At</c> (a place in the song), over
    /// <c>Length</c>: auto-play's limit on each song, for practice nights and classes (#98). Only
    /// when it's passed while playing: a song already beyond it, or moved past it, plays on.
    /// </summary>
    public (TimeSpan At, TimeSpan Length)? AutoFade { get; set; }

    /// <summary>
    /// The song fades out over this long from <see cref="End"/>, rather than stopping there: the
    /// DJ's *Start fade-out here*, for a long or repetitive outro (#267). The music goes on past
    /// <see cref="End"/> while it fades; the song has ended when the fade is over. Null: it stops.
    /// </summary>
    public TimeSpan? EndFade { get; set; }

    /// <summary>Where the song's own <see cref="EndFade"/> will start, if that's still ahead.</summary>
    public TimeSpan? EndFadesAt => EndFade is not null && IsLoaded && !_ended && !_endFading && _end is not null
        ? End : null;

    /// <summary>The fade under way is the song's <see cref="EndFade"/>: when it's over, the song has played out.</summary>
    private bool _endFading;

    /// <summary>Where <see cref="AutoFade"/> will fade the song, if that's still ahead and before the music ends.</summary>
    public TimeSpan? FadesAt => AutoFade is { } fade && IsLoaded && !_ended && _fadeLength is null
        && fade.At > Position && fade.At < End ? fade.At : null;

    /// <summary>The playhead passes further than this in one update only when moved, not by playing.</summary>
    private static readonly TimeSpan PlayedStep = TimeSpan.FromSeconds(1);

    // ───────────── Jumps ─────────────

    /// <summary>
    /// A seamless jump from the end of one phrase to another: forward to skip a part, backward to
    /// play one again. When armed it happens once, at the exact sample, as the music reaches
    /// <see cref="From"/>; then it disarms.
    /// </summary>
    public sealed unsafe class Jump(TimeSpan from, TimeSpan to)
    {
        private bool _armed;

        /// <summary>What the audio thread jumps with, while the sync is set (the deck's).</summary>
        private NativeDsp.Jump* _state;

        public TimeSpan From { get; } = from;
        public TimeSpan To { get; } = to;

        /// <summary>Waiting for the music to reach <see cref="From"/>: false once it has jumped.</summary>
        public bool Armed
        {
            get => _armed && !HasJumped;
            internal set => _armed = value;
        }

        internal int Sync { get; private set; }

        /// <summary>The audio thread has jumped (native/gzjam_dsp.c), and the deck hasn't noticed yet.</summary>
        internal bool HasJumped => Sync != 0 && Volatile.Read(ref _state->Jumped) != 0;

        internal void Set(int sync, NativeDsp.Jump* state)
        {
            Sync = sync;
            _state = sync == 0 ? null : state;
        }

        /// <summary>The sync is gone (removed, or freed with its stream).</summary>
        internal void Forget() => Set(0, null);
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
        for (int i = 0; i < _jumps.Count; i++)
        {
            Jump jump = _jumps[i];
            if (!jump.Armed || jump.Sync != 0)
                continue;
            while (_jumpStates.Count <= i)
                _jumpStates.Add((IntPtr)NativeDsp.NewJump());
            var state = (NativeDsp.Jump*)_jumpStates[i];
            state->To = (ulong)Bass.ChannelSeconds2Bytes(_tempo, jump.To.TotalSeconds);
            Volatile.Write(ref state->Jumped, 0);
            // Mixtime: called as the audio is decoded, so moving the position there is seamless.
            // Native (#344): the jump can't be held up by .NET tidying its memory.
            long at = Bass.ChannelSeconds2Bytes(_tempo, jump.From.TotalSeconds);
            jump.Set(NativeDsp.SetJumpSync(_tempo, at, state), state);
        }
    }

    private void RemoveJumpSyncs()
    {
        TakeJumps();   // one that's just happened is still reported
        foreach (Jump jump in _jumps.Where(j => j.Sync != 0))
        {
            if (IsLoaded)
                Bass.ChannelRemoveSync(_tempo, jump.Sync);
            jump.Forget();
        }
    }

    /// <summary>Jumps the audio thread has made since the last look: disarmed, and reported by <see cref="Update"/>.</summary>
    private void TakeJumps()
    {
        foreach (Jump jump in _jumps)
        {
            if (!jump.HasJumped)
                continue;
            jump.Armed = false;
            jump.Forget();   // a one-time sync: BASS has removed it
            _jumped = true;
        }
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

    // EQ: shelves below 150 Hz and above 5 kHz (where record hiss lives), a bell around 1 kHz
    // unless the DJ moves it.
    /// <summary>EQ in dB (0 = flat), heard in the room and the headphones alike. Kept across loads.</summary>
    public (double Low, double Mid, double High) Eq => _sound.Eq;

    /// <summary>Where the mid band sits, in Hz.</summary>
    public double MidHz => _sound.MidHz;

    public void SetEq(double low, double mid, double high, double midHz = 1000) => _sound.SetEq(low, mid, high, midHz);

    /// <summary>
    /// How a recording is evened out: <paramref name="Amount"/> from 0 (off) to 1, for a song whose
    /// integrated loudness is <paramref name="Lufs"/> (before any gain), so the compressor works
    /// around where the song's music actually is, however loud the file.
    /// </summary>
    public readonly record struct Evening(double Amount, double Lufs)
    {
        /// <summary>
        /// The music's typical level, as the compressor hears it: an average a few dB below the
        /// song's integrated loudness (which weights and sums the channels).
        /// </summary>
        private double TypicalDb => Lufs - 3;

        /// <summary>
        /// Where compression starts: just under the song's typical level, down to 17 dB under it
        /// at the most, so its loud passages come down and its quiet ones are left alone.
        /// </summary>
        public double ThresholdDb => Math.Clamp(TypicalDb - 17 * Amount, -60, 0);

        /// <summary>Up to 4:1 at the most, enough to even out a recording without squashing it.</summary>
        public double Ratio => 1 + 3 * Amount;

        /// <summary>
        /// Gain after the compressor, giving back most of what it takes from the song's typical
        /// level: so the quiet passages come up, and loudness matching (measured on the song as it
        /// is) still roughly holds.
        /// </summary>
        public double MakeUpDb => Math.Max(0, TypicalDb - ThresholdDb) * (1 - 1 / Ratio) * 0.6;
    }

    /// <summary>Evens out the recording (a compressor after the EQ); amount 0 takes it out altogether.</summary>
    public Evening Even
    {
        get => _sound.Even;
        set => _sound.Even = value;
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
    /// <remarks>Measured natively (#344) as the speakers' mix takes the deck's audio, before its volume is applied.</remarks>
    public float TakePeak() => _disposed ? 0 : Interlocked.Exchange(ref _meter->Peak, 0) * (float)_gain;

    internal void Update()
    {
        if (!IsLoaded)
            return;
        TakeJumps();
        if (_jumped)
        {
            _jumped = false;
            Jumped?.Invoke(this, EventArgs.Empty);
        }

        if (_fadeLength is { } length)
        {
            if (_mix.Time.GetElapsedTime(_fadeStarted) >= length)
            {
                bool playedOut = _endFading;
                Pause();   // also ends the fade and restores volume for the next play
                if (playedOut)
                    _ended = true;   // as at the end of the music: played again, it starts from the top
                Finished?.Invoke(this, EventArgs.Empty);
                return;
            }
            ApplyVolumes();
        }

        // Auto-play's limit on each song: passed while playing, not jumped or moved over.
        TimeSpan position = Position, last = _lastPosition;
        _lastPosition = position;
        if (AutoFade is { } fade && _playing && !_ended && _fadeLength is null
            && last < fade.At && position >= fade.At && position - last < PlayedStep && fade.At < End)
        {
            FadeOut(fade.Length);
            return;
        }

        // The song's own fade at its end, rather than stopping there.
        if (EndFade is { } endFade && _playing && !_ended && _fadeLength is null && _end is not null && position >= End)
        {
            FadeOut(endFade);
            _endFading = true;
            return;
        }

        // The end of the music (trailing silence is trimmed), or the end of the file.
        if (_playing && !_ended && (
                (_end is not null && Position >= End && !_endFading) ||
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
            !BassMix.MixerAddChannel(_mix.Cue.Handle, _splitCue, flags) ||
            !BassMix.MixerAddChannel(_mix.Booth.Handle, _splitBooth, flags))
            throw new AudioException($"Could not route Deck {Name}", Bass.LastError);
        SetPaused(!_playing);
        ApplyVolumes();
    }

    /// <summary>Takes this deck's headphone or booth feed off that output, before it's opened again.</summary>
    internal void Detach(TableOutput output)
    {
        if (IsLoaded)
            BassMix.MixerRemoveChannel(output == TableOutput.Headphones ? _splitCue : _splitBooth);
    }

    /// <summary>
    /// Puts this deck's headphone or booth feed on that output's new mixer (it was opened again
    /// after being unplugged). The speakers' feed isn't touched.
    /// </summary>
    internal void Attach(TableOutput output)
    {
        if (!IsLoaded)
            return;
        var (split, bus) = output == TableOutput.Headphones ? (_splitCue, _mix.Cue) : (_splitBooth, _mix.Booth);
        var flags = BassFlags.MixerChanDownMix | (_playing ? 0 : BassFlags.MixerChanPause);
        if (!BassMix.MixerAddChannel(bus.Handle, split, flags))
            throw new AudioException($"Could not route Deck {Name}", Bass.LastError);
        ApplyVolumes();
    }

    internal void Detach()
    {
        if (!IsLoaded)
            return;
        BassMix.MixerRemoveChannel(_splitMain);
        BassMix.MixerRemoveChannel(_splitCue);
        BassMix.MixerRemoveChannel(_splitBooth);
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
        foreach (int split in new[] { _splitMain, _splitCue, _splitBooth })
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
        _endFading = false;
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
        Bass.ChannelSetAttribute(_splitBooth, ChannelAttribute.Volume, _mainGain * level);   // the booth hears what the room hears
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
    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Unload();
        // Their streams are freed: BASS can't call with them any more.
        foreach (IntPtr state in _jumpStates)
            NativeDsp.Free((void*)state);
        _jumpStates.Clear();
        NativeDsp.Free(_meter);
    }
}

/// <summary>
/// A song opened for a deck by <see cref="Deck.Open"/>, not on it yet (#361). Disposed of
/// without being loaded (another song was chosen meanwhile), it lets go of its stream and memory.
/// </summary>
public sealed class OpenedSong : IDisposable
{
    private int _tempo;
    private SongMemory? _memory;

    internal OpenedSong(string path, int tempo, SongMemory? memory)
    {
        Path = path;
        _tempo = tempo;
        _memory = memory;
    }

    public string Path { get; }

    /// <summary>The deck takes the stream and memory: they're its to free from now on.</summary>
    internal (int Tempo, SongMemory? Memory) Take()
    {
        if (_tempo == 0)
            throw new InvalidOperationException("This song has already been loaded or let go of.");
        var taken = (_tempo, _memory);
        (_tempo, _memory) = (0, null);
        return taken;
    }

    public void Dispose()
    {
        if (_tempo != 0)
            Bass.StreamFree(_tempo);   // frees the file's stream with it (FxFreeSource)
        _memory?.Release();
        (_tempo, _memory) = (0, null);
    }
}
