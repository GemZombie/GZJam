using ManagedBass;
using ManagedBass.Mix;

namespace GZJam.Audio;

/// <summary>
/// Plays a track straight to the headphones, without loading a deck, so the DJ can audition
/// library and queue tracks. Never routed to the speakers.
/// </summary>
public sealed class PreviewPlayer : IDisposable
{
    private readonly MixEngine _mix;
    private readonly SongSound _sound = new("the headphone preview");
    private int _stream;
    private bool _playing;
    private double _gain = 1;

    internal PreviewPlayer(MixEngine mix) => _mix = mix;

    public string? TrackPath { get; private set; }
    public bool IsLoaded => _stream != 0;
    public bool IsPlaying => _playing;

    /// <summary>Raised from <see cref="MixEngine.Update"/> when the previewed track reaches its end.</summary>
    public event EventHandler? Finished;

    /// <summary>Starts previewing <paramref name="path"/>, replacing anything already previewing.</summary>
    public void Play(string path, TimeSpan start = default)
    {
        Stop();
        _mix.Engine.UseNoSoundDevice();
        int stream = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float | BassFlags.Prescan);
        if (stream == 0)
            throw AudioException.CouldNotOpen(path);

        _stream = stream;
        TrackPath = path;
        _sound.Attach(_stream);
        if (start > TimeSpan.Zero)
            Bass.ChannelSetPosition(_stream, Bass.ChannelSeconds2Bytes(_stream, start.TotalSeconds));
        _playing = true;
        Attach();
    }

    public void Stop()
    {
        if (_stream != 0)
        {
            Detach();
            Bass.StreamFree(_stream);
        }
        _stream = 0;
        _sound.Detach();   // freed with the stream
        _playing = false;
        TrackPath = null;
    }

    /// <summary>
    /// The song's EQ and evening out, as a deck would play it (#137): set before or after
    /// <see cref="Play"/>, and kept until changed.
    /// </summary>
    public void SetEq(double low, double mid, double high, double midHz = 1000) => _sound.SetEq(low, mid, high, midHz);

    public (double Low, double Mid, double High) Eq => _sound.Eq;

    public Deck.Evening Even
    {
        get => _sound.Even;
        set => _sound.Even = value;
    }

    /// <summary>Gain in dB (loudness matching and the DJ's own), as on a deck.</summary>
    public double GainDb
    {
        get => 20 * Math.Log10(_gain);
        set
        {
            _gain = Math.Pow(10, Math.Clamp(value, -30, 20) / 20);
            ApplyVolume();
        }
    }

    public void Seek(TimeSpan position)
    {
        if (_stream != 0)
            Bass.ChannelSetPosition(_stream, Bass.ChannelSeconds2Bytes(_stream, Math.Clamp(position.TotalSeconds, 0, Length.TotalSeconds)));
    }

    public TimeSpan Position => _stream == 0
        ? TimeSpan.Zero
        : TimeSpan.FromSeconds(Bass.ChannelBytes2Seconds(_stream, Bass.ChannelGetPosition(_stream)));

    public TimeSpan Length => _stream == 0
        ? TimeSpan.Zero
        : TimeSpan.FromSeconds(Bass.ChannelBytes2Seconds(_stream, Bass.ChannelGetLength(_stream)));

    internal void Update()
    {
        if (_playing && Bass.ChannelIsActive(_stream) == PlaybackState.Stopped)
        {
            Stop();
            Finished?.Invoke(this, EventArgs.Empty);
        }
    }

    internal void Attach()
    {
        if (_stream != 0 && !BassMix.MixerAddChannel(_mix.Cue.Handle, _stream, BassFlags.MixerChanDownMix))
            throw new AudioException("Could not route the preview to the headphones", Bass.LastError);
        ApplyVolume();
    }

    /// <summary>The preview is cued: the phones mix turned all the way to the room leaves it out.</summary>
    internal void ApplyVolume()
    {
        if (_stream != 0)
            Bass.ChannelSetAttribute(_stream, ChannelAttribute.Volume, _mix.PhonesCueLevel * _gain);
    }

    internal void Detach()
    {
        if (_stream != 0)
            BassMix.MixerRemoveChannel(_stream);
    }

    public void Dispose() => Stop();
}
