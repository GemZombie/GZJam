using ManagedBass;
using ManagedBass.Fx;
using ManagedBass.Mix;

namespace GZJam.Audio;

/// <summary>
/// One track player. The file is decoded through a BASS_FX tempo stream (tempo changes keep the
/// pitch), then split so the same audio feeds both the main and the cue bus. The crossfader sets
/// the main split's volume; <see cref="Cue"/> switches the cue split on or off.
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
    private double _mainGain;
    private bool _cue;
    private long _fadeStarted;
    private TimeSpan? _fadeLength;

    internal Deck(MixEngine mix, int index)
    {
        _mix = mix;
        Index = index;
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

    public void Load(string path)
    {
        Unload();
        _mix.Engine.UseNoSoundDevice();

        // Prescan gives exact length and seeking for VBR MP3s.
        int source = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float | BassFlags.Prescan);
        if (source == 0)
            throw new AudioException($"Could not open '{path}'", Bass.LastError);

        int tempo = BassFx.TempoCreate(source, BassFlags.Decode | BassFlags.FxFreeSource);
        if (tempo == 0)
        {
            Errors error = Bass.LastError;
            Bass.StreamFree(source);
            throw new AudioException($"Could not create tempo stream for '{path}'", error);
        }

        _tempo = tempo;
        _splitMain = CreateSplit();
        _splitCue = CreateSplit();
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
        if (_tempo != 0)
        {
            Detach();
            Bass.StreamFree(_splitMain);
            Bass.StreamFree(_splitCue);
            Bass.StreamFree(_tempo);
        }
        _tempo = _splitMain = _splitCue = 0;
        _playing = false;
        _ended = false;
        TrackPath = null;
    }

    public void Play()
    {
        if (!IsLoaded)
            return;
        if (_ended)
            Seek(TimeSpan.Zero);
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
            Load(path);
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

    /// <summary>Real time left to play, accounting for tempo.</summary>
    public TimeSpan Remaining => Max(TimeSpan.Zero, (Length - Position) / (1 + _tempoPercent / 100));

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

    internal void Update()
    {
        if (!IsLoaded)
            return;

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

        if (_playing && !_ended && Bass.ChannelIsActive(_splitMain) == PlaybackState.Stopped && Position >= Length - TimeSpan.FromMilliseconds(250))
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

    private void ApplyVolumes()
    {
        if (!IsLoaded)
            return;
        double level = _volume * FadeFactor;
        Bass.ChannelSetAttribute(_splitMain, ChannelAttribute.Volume, _mainGain * level);
        Bass.ChannelSetAttribute(_splitCue, ChannelAttribute.Volume, _cue ? level : 0);
    }

    private void ApplyTempo()
    {
        if (IsLoaded)
            Bass.ChannelSetAttribute(_tempo, ChannelAttribute.Tempo, _tempoPercent);
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    public void Dispose() => Unload();
}
