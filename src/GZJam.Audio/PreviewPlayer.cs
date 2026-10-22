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
    private int _stream;
    private bool _playing;

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
        _playing = false;
        TrackPath = null;
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
    }

    internal void Detach()
    {
        if (_stream != 0)
            BassMix.MixerRemoveChannel(_stream);
    }

    public void Dispose() => Stop();
}
