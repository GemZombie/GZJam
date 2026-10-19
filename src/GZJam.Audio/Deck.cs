using ManagedBass;
using ManagedBass.Fx;

namespace GZJam.Audio;

/// <summary>
/// A single track player bound to one output device. Playback runs through a BASS_FX
/// tempo stream so tempo can change without changing pitch.
/// </summary>
public sealed class Deck(AudioEngine engine, string name) : IDisposable
{
    private int _stream;
    private double _tempoPercent;
    private double _volume = 1.0;

    public string Name { get; } = name;
    public AudioDevice? Device { get; private set; }
    public string? TrackPath { get; private set; }

    /// <summary>Routes this deck to a device. Moves a loaded track without interrupting it.</summary>
    public void SetDevice(AudioDevice device)
    {
        engine.EnsureInitialized(device.Index);
        if (_stream != 0 && !Bass.ChannelSetDevice(_stream, device.Index))
            throw new AudioException($"Could not move {Name} to '{device.Name}'", Bass.LastError);
        Device = device;
    }

    public void Load(string path)
    {
        if (Device is null)
            throw new InvalidOperationException($"{Name} has no output device.");

        Unload();

        // BASS's "current device" is per-thread; streams are created on it.
        Bass.CurrentDevice = Device.Index;

        // Prescan gives exact length/seeking for VBR MP3s.
        int source = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float | BassFlags.Prescan);
        if (source == 0)
            throw new AudioException($"Could not open '{path}'", Bass.LastError);

        int tempo = BassFx.TempoCreate(source, BassFlags.FxFreeSource);
        if (tempo == 0)
        {
            Errors error = Bass.LastError;
            Bass.StreamFree(source);
            throw new AudioException($"Could not create tempo stream for '{path}'", error);
        }

        _stream = tempo;
        TrackPath = path;
        ApplyTempo();
        ApplyVolume();
    }

    public void Unload()
    {
        if (_stream != 0)
        {
            Bass.StreamFree(_stream);
            _stream = 0;
        }
        TrackPath = null;
    }

    public void Play()
    {
        if (_stream != 0 && !Bass.ChannelPlay(_stream))
            throw new AudioException($"Could not play {Name}", Bass.LastError);
    }

    public void Pause()
    {
        if (_stream != 0)
            Bass.ChannelPause(_stream);
    }

    public void Stop()
    {
        if (_stream == 0)
            return;
        Bass.ChannelStop(_stream);
        Seek(TimeSpan.Zero);
    }

    public void Seek(TimeSpan position)
    {
        if (_stream == 0)
            return;
        long bytes = Bass.ChannelSeconds2Bytes(_stream, Math.Max(0, position.TotalSeconds));
        Bass.ChannelSetPosition(_stream, bytes);
    }

    public bool IsPlaying => _stream != 0 && Bass.ChannelIsActive(_stream) == PlaybackState.Playing;

    /// <summary>Position within the original track (not affected by tempo).</summary>
    public TimeSpan Position => _stream == 0
        ? TimeSpan.Zero
        : TimeSpan.FromSeconds(Bass.ChannelBytes2Seconds(_stream, Bass.ChannelGetPosition(_stream)));

    public TimeSpan Length => _stream == 0
        ? TimeSpan.Zero
        : TimeSpan.FromSeconds(Bass.ChannelBytes2Seconds(_stream, Bass.ChannelGetLength(_stream)));

    /// <summary>Real time left to play, accounting for tempo.</summary>
    public TimeSpan Remaining => (Length - Position) / (1 + _tempoPercent / 100);

    /// <summary>Tempo change in percent; -10 plays 10% slower. Pitch is preserved.</summary>
    public double TempoPercent
    {
        get => _tempoPercent;
        set
        {
            _tempoPercent = Math.Clamp(value, -50, 50);
            ApplyTempo();
        }
    }

    /// <summary>Volume, 0.0 to 1.0.</summary>
    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            ApplyVolume();
        }
    }

    private void ApplyTempo()
    {
        if (_stream != 0)
            Bass.ChannelSetAttribute(_stream, ChannelAttribute.Tempo, _tempoPercent);
    }

    private void ApplyVolume()
    {
        if (_stream != 0)
            Bass.ChannelSetAttribute(_stream, ChannelAttribute.Volume, _volume);
    }

    public void Dispose() => Unload();
}
