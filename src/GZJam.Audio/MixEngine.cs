using ManagedBass;
using ManagedBass.Mix;

namespace GZJam.Audio;

/// <summary>
/// The DJ mixer: decks and a headphone preview player, feeding a main (speakers) bus and a cue
/// (headphones) bus. Every deck feeds both buses at all times: the crossfader sets how loud each
/// deck is in the room, and each deck's cue button sets whether it's in the headphones. Neither
/// interrupts playback.
/// Call <see cref="Update"/> regularly (the UI does ~30 times a second) to run fades and detect
/// tracks ending.
/// </summary>
public sealed class MixEngine : IDisposable
{
    public const int SampleRate = 44100;
    public const int Channels = 2;

    private readonly bool _offline;
    private double _crossfader;

    /// <param name="offline">
    /// Mix without audio devices; pull audio with <see cref="OutputBus.Render"/>. For tests.
    /// </param>
    /// <param name="time">Clock for fades; tests pass a manual one.</param>
    public MixEngine(AudioEngine engine, int deckCount = 2, bool offline = false, TimeProvider? time = null)
    {
        Engine = engine;
        _offline = offline;
        Time = time ?? TimeProvider.System;
        Main = CreateBus(null);
        Cue = CreateBus(null);
        Decks = Enumerable.Range(0, deckCount).Select(i => new Deck(this, i)).ToList();
        Preview = new PreviewPlayer(this);
        ApplyRouting();
    }

    internal AudioEngine Engine { get; }

    /// <summary>Mixing without audio devices (tests).</summary>
    public bool IsOffline => _offline;
    internal TimeProvider Time { get; }

    public OutputBus Main { get; private set; }
    public OutputBus Cue { get; private set; }
    public IReadOnlyList<Deck> Decks { get; }
    public PreviewPlayer Preview { get; }

    /// <summary>Crossfader position: 0 = all Deck A, 1 = all Deck B.</summary>
    public double Crossfader
    {
        get => _crossfader;
        set
        {
            _crossfader = Math.Clamp(value, 0, 1);
            ApplyRouting();
        }
    }

    /// <summary>The deck the crossfader leans towards (A at dead centre). Both may be audible in a blend.</summary>
    public Deck LiveDeck => Decks.MaxBy(d => Crossfade.Audibility(d.Index, _crossfader))!;

    /// <summary>
    /// Points the buses at audio devices. Decks and preview keep playing through the switch.
    /// A null cue device leaves the headphones silent (they are never sent to the speakers).
    /// </summary>
    public void SetOutputs(AudioDevice? main, AudioDevice? cue)
    {
        if (_offline)
            throw new InvalidOperationException("An offline mix engine has no devices.");

        OutputBus oldMain = Main, oldCue = Cue;
        foreach (Deck deck in Decks)
            deck.Detach();
        Preview.Detach();

        Main = CreateBus(main);
        Cue = CreateBus(cue);

        foreach (Deck deck in Decks)
            deck.Attach();
        Preview.Attach();
        oldMain.Dispose();
        oldCue.Dispose();
        ApplyRouting();
    }

    /// <summary>Runs time-based work: fade-outs, end-of-track detection. Raises events on the calling thread.</summary>
    public void Update()
    {
        foreach (Deck deck in Decks)
            deck.Update();
        Preview.Update();
    }

    /// <summary>Offline only: renders both buses in small interleaved chunks, returning each one's peak level.</summary>
    public (float Main, float Cue) Render(TimeSpan duration)
    {
        float main = 0, cue = 0;
        var chunk = TimeSpan.FromMilliseconds(50);
        for (var done = TimeSpan.Zero; done < duration; done += chunk)
        {
            main = Math.Max(main, Main.Render(chunk));
            cue = Math.Max(cue, Cue.Render(chunk));
        }
        return (main, cue);
    }

    internal void ApplyRouting()
    {
        foreach (Deck deck in Decks)
        {
            deck.SetMainGain(Crossfade.MainGain(Crossfade.Audibility(deck.Index, _crossfader)));
        }
    }

    private OutputBus CreateBus(AudioDevice? device)
    {
        // NonStop: keep the output running through silence, so starting a track is instant.
        const BassFlags flags = BassFlags.Float | BassFlags.MixerNonStop;

        if (device is null)
        {
            Engine.UseNoSoundDevice();
            int offline = BassMix.CreateMixerStream(SampleRate, Channels, flags | BassFlags.Decode);
            if (offline == 0)
                throw new AudioException("Could not create mixer", Bass.LastError);
            return new OutputBus(offline, null);
        }

        Engine.EnsureInitialized(device.Index);
        Bass.CurrentDevice = device.Index;
        int mixer = BassMix.CreateMixerStream(SampleRate, Channels, flags);
        if (mixer == 0)
            throw new AudioException($"Could not create mixer on '{device.Name}'", Bass.LastError);
        if (!Bass.ChannelPlay(mixer))
            throw new AudioException($"Could not start output on '{device.Name}'", Bass.LastError);
        return new OutputBus(mixer, device);
    }

    public void Dispose()
    {
        foreach (Deck deck in Decks)
            deck.Dispose();
        Preview.Dispose();
        Main.Dispose();
        Cue.Dispose();
    }
}
