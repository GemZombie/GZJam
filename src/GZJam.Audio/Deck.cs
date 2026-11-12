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
    private TimeSpan? _fadeLength;   // a fade-out under way: asked for, or started by the song reaching a point
    private TimeSpan _lastPosition;   // at the last update: to see the playhead play past auto-play's limit
    private TimeSpan _start;
    private TimeSpan? _end;
    private readonly SongSound _sound;           // EQ and evening out, on the tempo stream
    private readonly List<Jump> _jumps = [];
    // The audio thread's (native, #344): one per jump by place in the list, and the meter.
    // Freed with the deck, once BASS can't call with them.
    private readonly List<IntPtr> _jumpStates = [];
    private readonly NativeDsp.Meter* _meter;
    // The fade-out on the deck's own sound (#359), and the points in the song that start one: the
    // end of the music and auto-play's limit. The audio thread's too; freed with the deck.
    private readonly NativeDsp.Fade* _fade;
    private readonly NativeDsp.FadeAt* _endAt;
    private readonly NativeDsp.FadeAt* _autoAt;
    private int _endSync, _autoSync;
    private int _rate = MixEngine.SampleRate;   // the song's frames a second, for fade lengths
    private (double Gain, long Until)? _glide;  // where the crossfader is taking this deck, and when it's there
    private bool _jumped;   // a jump happened, for Update to report
    private bool _disposed;

    internal Deck(MixEngine mix, int index)
    {
        _mix = mix;
        Index = index;
        _sound = new SongSound($"Deck {Name}");
        _meter = NativeDsp.NewMeter();
        _fade = NativeDsp.NewFade();
        _endAt = NativeDsp.NewFadeAt(_fade);
        _autoAt = NativeDsp.NewFadeAt(_fade);
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
        _length = TimeSpan.FromSeconds(Bass.ChannelBytes2Seconds(_tempo, Bass.ChannelGetLength(_tempo)));
        string path = song.Path;
        // The old song's stream is gone, so nothing else is using the fade: it starts at full.
        ChannelInfo info = Bass.ChannelGetInfo(_tempo);
        _rate = info.Frequency > 0 ? info.Frequency : MixEngine.SampleRate;
        *_fade = default;
        _fade->Gain = _fade->ShownGain = 1;
        _fade->Channels = Math.Max(1, info.Channels);
        NativeDsp.SetFadeDsp(_tempo, _fade);
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
        _autoFade = null;
        SetFadeSyncs();
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
        _endSync = _autoSync = 0;   // so are these
        _endSyncFor = _autoSyncFor = null;
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
        _length = TimeSpan.Zero;
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

    /// <summary>
    /// Fades the deck out over <paramref name="length"/>, then pauses it and raises <see cref="Finished"/>.
    /// The fade is the sound's own (#359): it carries on, and ends, however busy the app is.
    /// </summary>
    public void FadeOut(TimeSpan length)
    {
        if (!_playing)
            return;
        StartFade(length, Asked);
    }

    // Why a fade started, as the native fade has it: the strongest since it began wins.
    private const int Asked = 1;       // FADE OUT, or auto-play's limit
    private const int PlayedOut = 2;   // the end of the music: when it's silent, the song has ended

    /// <summary>The playhead passes further than this in one update only when moved, not by playing.</summary>
    private static readonly TimeSpan PlayedStep = TimeSpan.FromSeconds(1);

    /// <summary>A sync that hasn't come this long after its point isn't coming: well over a block of sound.</summary>
    private static readonly TimeSpan SyncMissedAfter = TimeSpan.FromSeconds(0.25);

    /// <summary>
    /// Auto-play's limit was played past without the sound starting its fade, so the app started
    /// it (a safety net, #359): raised with the deck's state, for the log. It shouldn't happen.
    /// </summary>
    public event EventHandler<string>? FadeStartedLate;

    /// <summary>How many times <see cref="FadeStartedLate"/> has been raised (tests).</summary>
    internal int FadesStartedLate { get; private set; }

    /// <summary>A stop at the end of the music, rather than a fade: just long enough not to click.</summary>
    private static readonly TimeSpan EndStop = TimeSpan.FromMilliseconds(5);

    private void StartFade(TimeSpan length, int reason)
    {
        if (_fadeLength is not { } now || length < now)
            _fadeLength = length > TimeSpan.Zero ? length : EndStop;
        _fade->Frames = Frames(length);
        _fade->Reason = reason;
        Interlocked.Exchange(ref _fade->Command, NativeDsp.FadeStart);   // after the frames and reason
    }

    private uint Frames(TimeSpan length) => (uint)Math.Clamp(length.TotalSeconds * _rate, 0, uint.MaxValue);

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
            SetFadeSyncs();
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

    /// <summary>
    /// Where the song is being heard (#417), rather than where it has been read to
    /// (<see cref="Position"/>): the outputs hold some of what's read before it's played, up to
    /// half a second or more with more protection against crackles, so the playhead and the
    /// times were that far ahead of the music. In the room, or in the headphones for a deck only
    /// there. BASSmix keeps track: after a pause it runs on to where the deck stopped, and a seek
    /// or jump shows as it's heard. Where it can't say (no output), where it's been read to.
    /// </summary>
    public TimeSpan HeardPosition
    {
        get
        {
            if (!IsLoaded)
                return TimeSpan.Zero;
            int split = !IsInRoom && Cue ? _splitCue : _splitMain;
            long bytes = BassMix.ChannelGetPosition(split, PositionFlags.Bytes);
            return bytes < 0 ? Position : TimeSpan.FromSeconds(Bass.ChannelBytes2Seconds(split, bytes));
        }
    }

    /// <summary>
    /// How long until the song is heard ending (#417), from where it's heard: <see cref="Remaining"/>
    /// and what's still on its way to the output. A seek shows only once heard: until then what's
    /// on its way is counted as at most the output's latency.
    /// </summary>
    public TimeSpan HeardRemaining(TimeSpan heard)
    {
        TimeSpan latency = !IsInRoom && Cue ? _mix.Cue.Latency : _mix.SpeakerLatency;
        TimeSpan coming = (Position - heard) / (1 + _tempoPercent / 100);
        return Remaining + (coming < TimeSpan.Zero ? TimeSpan.Zero : coming > latency ? latency : coming);
    }

    /// <summary>How long the deck's meter is ahead of the room (#417): it reads the speakers' feed as it's mixed.</summary>
    public TimeSpan MeterLatency => _mix.SpeakerLatency;

    /// <summary>The song's length: read once, when it's loaded (#365), as it's asked for many times a frame.</summary>
    public TimeSpan Length => _length;

    private TimeSpan _length;

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
        SetFadeSyncs();
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
                return FadeLeft(fading);
            TimeSpan toEnd = Max(TimeSpan.Zero, left / speed) + (EndFade ?? TimeSpan.Zero);
            return FadesAt is { } at ? Min(toEnd, (at - position) / speed + AutoFade!.Value.Length) : toEnd;
        }
    }

    /// <summary>
    /// Fade out by itself when the playhead reaches <c>At</c> (a place in the song), over
    /// <c>Length</c>: auto-play's limit on each song, for practice nights and classes (#98). Only
    /// when it's passed while playing: a song already beyond it, or moved past it, plays on.
    /// </summary>
    public (TimeSpan At, TimeSpan Length)? AutoFade
    {
        get => _autoFade;
        set
        {
            if (value == _autoFade)
                return;   // auto-play says so on every tick
            _autoFade = value;
            SetFadeSyncs();
        }
    }

    private (TimeSpan At, TimeSpan Length)? _autoFade;

    /// <summary>
    /// The song fades out over this long from <see cref="End"/>, rather than stopping there: the
    /// DJ's *Start fade-out here*, for a long or repetitive outro (#267). The music goes on past
    /// <see cref="End"/> while it fades; the song has ended when the fade is over. Null: it stops.
    /// </summary>
    public TimeSpan? EndFade
    {
        get => _endFade;
        set
        {
            if (value == _endFade)
                return;
            _endFade = value;
            SetFadeSyncs();
        }
    }

    private TimeSpan? _endFade;

    /// <summary>How long the fade under way has left, as the sound has it (<paramref name="asked"/> until it's begun).</summary>
    private TimeSpan FadeLeft(TimeSpan asked)
    {
        int state = Volatile.Read(ref _fade->State);
        float step = Volatile.Read(ref _fade->Step), gain = Volatile.Read(ref _fade->ShownGain);
        return state == 2 ? TimeSpan.Zero
            : state == 1 && step > 0 ? TimeSpan.FromSeconds(gain / step / _rate)
            : asked;
    }

    /// <summary>
    /// Sets the points where the song starts its own fade (#359), as it's decoded: the end of its
    /// music (fading over <see cref="EndFade"/>, or stopping), and <see cref="AutoFade"/>'s point.
    /// Called whenever either moves.
    /// </summary>
    private void SetFadeSyncs()
    {
        if (!IsLoaded)
            return;
        (long At, uint Frames)? end = _end is not null
            ? (Bass.ChannelSeconds2Bytes(_tempo, End.TotalSeconds), Frames(EndFade ?? EndStop))
            : null;
        (long At, uint Frames)? auto = AutoFade is { } fade && fade.At < End
            ? (Bass.ChannelSeconds2Bytes(_tempo, fade.At.TotalSeconds), Frames(fade.Length))
            : null;
        SetFadeSync(ref _endSync, ref _endSyncFor, end, _endAt, PlayedOut);
        SetFadeSync(ref _autoSync, ref _autoSyncFor, auto, _autoAt, Asked);
    }

    // Where each sync is set now, and the fade it starts.
    private (long At, uint Frames)? _endSyncFor, _autoSyncFor;

    /// <summary>
    /// Sets one fade point, or leaves it be if it's where it was: one set again just before its point
    /// can be missed, the sound having decoded a little past it already. The analysis coming in
    /// while a song was just before auto-play's limit did that (seen on Apple Silicon and Linux).
    /// </summary>
    private void SetFadeSync(ref int sync, ref (long At, uint Frames)? setFor, (long At, uint Frames)? wanted,
        NativeDsp.FadeAt* fadeAt, int reason)
    {
        if (sync != 0 && setFor == wanted)
            return;
        if (sync != 0)
            Bass.ChannelRemoveSync(_tempo, sync);
        sync = 0;
        setFor = null;
        if (wanted is not { } point)
            return;
        fadeAt->Frames = point.Frames;
        fadeAt->Reason = reason;
        sync = NativeDsp.SetFadeSync(_tempo, point.At, fadeAt);
        setFor = wanted;
    }

    /// <summary>Where the song's own <see cref="EndFade"/> will start, if that's still ahead.</summary>
    public TimeSpan? EndFadesAt => EndFade is not null && IsLoaded && !_ended && !_endFading && _end is not null
        ? End : null;

    /// <summary>The fade under way is the song's <see cref="EndFade"/>: when it's over, the song has played out.</summary>
    private bool _endFading;

    /// <summary>Where <see cref="AutoFade"/> will fade the song, if that's still ahead and before the music ends.</summary>
    public TimeSpan? FadesAt => AutoFade is { } fade && IsLoaded && !_ended && !IsFading
        && fade.At > Position && fade.At < End ? fade.At : null;

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

    /// <param name="toward">Where the crossfader is taking this deck, and how long it takes to get there.</param>
    internal void SetMainGain(double mainGain, (double Gain, TimeSpan Left)? toward = null)
    {
        _mainGain = mainGain;
        _glide = toward is { } t && t.Left > TimeSpan.Zero ? (t.Gain, Environment.TickCount64 + (long)t.Left.TotalMilliseconds) : null;
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

        // A fade-out, the sound's own (#359): one the song started itself, at the end of its music or
        // auto-play's limit, is seen here; once it's silent, the deck pauses. Not while a fade
        // that's over is still being undone (that's taken when the deck plays on).
        int state = Volatile.Read(ref _fade->State);
        if (_playing && state != 0 && Volatile.Read(ref _fade->Command) != NativeDsp.FadeReset)
        {
            _fadeLength ??= TimeSpan.FromSeconds(Volatile.Read(ref _fade->Frames) / (double)_rate);
            _endFading = Volatile.Read(ref _fade->Why) == PlayedOut;
            if (state == 2)
            {
                bool playedOut = _endFading;
                Pause();   // also ends the fade, for the next play
                if (playedOut)
                    _ended = true;   // as at the end of the music: played again, it starts from the top
                Finished?.Invoke(this, EventArgs.Empty);
                return;
            }
        }

        // The song's own fade at its end, if the sound hasn't begun it (the playhead moved past the end).
        TimeSpan position = Position, last = _lastPosition;
        _lastPosition = position;

        // Auto-play's limit, played past (not moved past) without the sound having started its fade:
        // the sound starts it at the exact point (#359), so this is only a safety net. Clearly past
        // it, by more than the sound decodes at a time: at the point itself, the sound's sync may
        // simply not have come yet.
        TimeSpan missedAt = AutoFade is { } limit ? limit.At + SyncMissedAfter : TimeSpan.MaxValue;
        if (AutoFade is { } fade && _playing && !_ended && _fadeLength is null && state == 0
            && Volatile.Read(ref _fade->Command) == 0
            && last < missedAt && position >= missedAt && position - last < PlayedStep && fade.At < End)
        {
            FadesStartedLate++;
            string why = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"limit {fade.At.TotalSeconds:0.000} s passed between {last.TotalSeconds:0.000} and {position.TotalSeconds:0.000} s; " +
                $"sync {_autoSync}, its point {_autoAt->Frames} frames, rate {_rate}, end {End.TotalSeconds:0.000} s");
            FadeStartedLate?.Invoke(this, why);
            StartFade(fade.Length, Asked);
            return;
        }
        if (EndFade is { } endFade && _playing && !_ended && _fadeLength is null && _end is not null && position >= End)
        {
            StartFade(endFade, PlayedOut);
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

    /// <summary>Ends a fade, or one that's over, so the next play is at full volume.</summary>
    private void CancelFade()
    {
        _fadeLength = null;
        _endFading = false;
        if (!_disposed && (Volatile.Read(ref _fade->State) != 0 || Volatile.Read(ref _fade->Command) != 0))
            Interlocked.Exchange(ref _fade->Command, NativeDsp.FadeReset);   // taken as the music plays on
    }

    internal void ApplyVolumes()
    {
        if (!IsLoaded)
            return;
        double level = _volume * _gain;
        // The crossfader on its way (#359): BASS slides the volumes on to where it's going by
        // itself, so the room isn't left between two decks while the app is busy.
        int left = _glide is { } g ? (int)Math.Max(0, g.Until - Environment.TickCount64) : 0;
        double? there = left > 0 ? _glide!.Value.Gain : null;
        SetVolume(_splitMain, _mainGain * level, there * level, left);
        SetVolume(_splitBooth, _mainGain * level, there * level, left);   // the booth hears what the room hears
        // The headphones: this deck if it's cued, and as much as the room hears of it, by the
        // phones mix. Cued and in the room, it's the louder of the two, not both added up.
        SetVolume(_splitCue, Phones(_mainGain) * level, there is { } gain ? Phones(gain) * level : null, left);
    }

    private double Phones(double mainGain) => Math.Max(_cue ? _mix.PhonesCueLevel : 0, mainGain * _mix.PhonesRoomLevel);

    /// <summary>A feed's volume now, sliding on to <paramref name="there"/> over <paramref name="ms"/> if given.</summary>
    private static void SetVolume(int split, double now, double? there, int ms)
    {
        Bass.ChannelSetAttribute(split, ChannelAttribute.Volume, now);   // stops a slide under way
        if (there is { } target && ms > 0)
            Bass.ChannelSlideAttribute(split, ChannelAttribute.Volume, (float)target, ms);
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
        NativeDsp.Free(_endAt);
        NativeDsp.Free(_autoAt);
        NativeDsp.Free(_fade);
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
