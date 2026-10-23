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

    /// <summary>The most decks there can be. The app shows 2 or 4; unused decks cost nothing.</summary>
    public const int MaxDecks = 4;

    private readonly bool _offline;
    private double _crossfader;
    private bool _mainLostRaised;
    private long _lastDropout = long.MinValue / 2;
    private long _dryAt = -1;

    /// <param name="offline">
    /// Mix without audio devices; pull audio with <see cref="OutputBus.Render"/>. For tests.
    /// </param>
    /// <param name="time">Clock for fades; tests pass a manual one.</param>
    public MixEngine(AudioEngine engine, int deckCount = MaxDecks, bool offline = false, TimeProvider? time = null)
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

    /// <summary>
    /// Windows: the speakers' sound card is GZJam's alone (WASAPI exclusive mode), so no other
    /// program's sound can reach the room. Only for a chosen device, never "Default", and only the
    /// speakers: the headphones stay shared. Applies the next time outputs are set. If the card
    /// can't be had alone it's shared instead, and <see cref="ExclusiveRefusal"/> says why.
    /// </summary>
    public bool ExclusiveSpeakers { get; set; }

    /// <summary>Why the speakers are shared although <see cref="ExclusiveSpeakers"/> is on (after the last <see cref="SetOutputs"/>), or null.</summary>
    public string? ExclusiveRefusal { get; private set; }

    /// <summary>
    /// Why the headphones couldn't be opened at the last <see cref="SetOutputs"/>, or null. The
    /// speakers open regardless, so the room never goes quiet because of the headphones; the
    /// headphones are then silent.
    /// </summary>
    public string? CueRefusal { get; private set; }

    /// <summary>
    /// The speakers' exclusive output stopped by itself (unplugged, or taken by Windows). Raised
    /// once, from <see cref="Update"/>; the app opens outputs again.
    /// </summary>
    public event EventHandler? MainOutputLost;

    /// <summary>
    /// The speakers' exclusive output ran dry: a dropout or crackle may have been heard because the
    /// computer didn't keep up. Raised from <see cref="Update"/> half a second later, unless the card
    /// turned out to be unplugged, and at most every 10 seconds; the app logs it.
    /// </summary>
    public event EventHandler? MainDropout;

    public OutputBus Main { get; private set; }
    public OutputBus Cue { get; private set; }
    public IReadOnlyList<Deck> Decks { get; }
    public PreviewPlayer Preview { get; }

    /// <summary>Crossfader position: 0 = all the left side (Decks 1 and 3), 1 = all the right (2 and 4).</summary>
    public double Crossfader
    {
        get => _crossfader;
        set
        {
            _crossfader = Math.Clamp(value, 0, 1);
            ApplyRouting();
        }
    }

    /// <summary>The side the crossfader leans towards: 0 = left, 1 = right (left at dead centre).</summary>
    public int LiveSide => _crossfader <= 0.5 ? 0 : 1;

    /// <summary>
    /// The deck the crossfader leans towards: the one playing on that side, else the first deck
    /// on it. Both sides may be audible in a blend.
    /// </summary>
    public Deck LiveDeck =>
        Decks.FirstOrDefault(d => Crossfade.Side(d.Index) == LiveSide && d.IsPlaying) ?? Decks[LiveSide];

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

        // Let go of the old outputs before opening the new ones. A Linux sound card opens only
        // once, and GZJam's own "Default" (through PipeWire) may be playing on the very card chosen.
        oldMain.Dispose();
        oldCue.Dispose();
        // An output wanted for GZJam alone is let go of too: Windows won't give it exclusively
        // while GZJam itself still has it shared.
        Engine.ReleaseDevicesExcept(WantsExclusive(main, cue) ? null : main?.Index, cue?.Index);
        try
        {
            Open(main, cue, oldMain, oldCue);
        }
        catch (AudioException)
        {
            // Back to what was playing, so a refused output doesn't silence the room.
            Engine.ReleaseDevicesExcept(oldMain.Device?.Index, oldCue.Device?.Index);
            try
            {
                Open(oldMain.Device, oldCue.Device, oldMain, oldCue);
            }
            catch (AudioException)
            {
                // The old speakers are gone too (unplugged): silent speakers, but the headphones,
                // if they still open, carry on.
                Open(null, oldCue.Device, oldMain, oldCue);
            }
            throw;
        }
    }

    private void Open(AudioDevice? main, AudioDevice? cue, OutputBus settingsMain, OutputBus settingsCue)
    {
        OutputBus newMain = CreateMainBus(main, cue);
        OutputBus newCue;
        CueRefusal = null;
        try
        {
            newCue = CreateBus(cue);
        }
        catch (AudioException ex)
        {
            // The room comes first: the speakers play on, the headphones are silent, and the app
            // says why.
            CueRefusal = ex.Message;
            newCue = CreateBus(null);
        }
        newMain.CopySettingsFrom(settingsMain);
        newCue.CopySettingsFrom(settingsCue);
        Main = newMain;
        Cue = newCue;
        _mainLostRaised = false;
        _dryAt = -1;

        foreach (Deck deck in Decks)
            deck.Attach();
        Preview.Attach();
        ApplyRouting();
    }

    /// <summary>Runs time-based work: fade-outs, end-of-track detection. Raises events on the calling thread.</summary>
    public void Update()
    {
        foreach (Deck deck in Decks)
            deck.Update();
        Preview.Update();
        long now = Environment.TickCount64;
        if (!_mainLostRaised && Main.IsLost)
        {
            _mainLostRaised = true;
            _dryAt = -1;
            MainOutputLost?.Invoke(this, EventArgs.Empty);
        }
        else if (_dryAt < 0 && now - _lastDropout > 10_000 && Main.IsDry())
        {
            _dryAt = now;
        }
        // A card being unplugged runs dry a moment before Windows says it's gone: only a dry
        // buffer that isn't followed by the card going within half a second is a dropout.
        if (_dryAt >= 0 && now - _dryAt >= 500)
        {
            _dryAt = -1;
            if (!Main.IsLost)
            {
                _lastDropout = now;
                MainDropout?.Invoke(this, EventArgs.Empty);
            }
        }
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

    /// <summary>
    /// The speakers are to be GZJam's alone. Not when the headphones are the same output: GZJam
    /// would then be keeping its own headphones out.
    /// </summary>
    private bool WantsExclusive(AudioDevice? device, AudioDevice? cue) =>
        ExclusiveSpeakers && OperatingSystem.IsWindows() && device is { FollowsSystemDefault: false } && cue?.Index != device.Index;

    /// <summary>The speakers: GZJam's alone if wanted and possible, else shared (saying why).</summary>
    private OutputBus CreateMainBus(AudioDevice? device, AudioDevice? cue)
    {
        ExclusiveRefusal = null;
        if (ExclusiveSpeakers && OperatingSystem.IsWindows() && device is { FollowsSystemDefault: false } && cue?.Index == device.Index)
            ExclusiveRefusal = $"{device.Name} is also the headphone output";
        if (WantsExclusive(device, cue))
        {
            try
            {
                var exclusive = ExclusiveOutput.Open(Engine, device!, MixerFlags);
                return new OutputBus(exclusive.Mixer, device, exclusive.SampleRate, exclusive);
            }
            catch (AudioException ex)
            {
                ExclusiveRefusal = ex.Message;
            }
        }
        return CreateBus(device);
    }

    // NonStop: keep the output running through silence, so starting a track is instant.
    private const BassFlags MixerFlags = BassFlags.Float | BassFlags.MixerNonStop;

    private OutputBus CreateBus(AudioDevice? device)
    {
        const BassFlags flags = MixerFlags;

        if (device is null)
        {
            Engine.UseNoSoundDevice();
            int offline = BassMix.CreateMixerStream(SampleRate, Channels, flags | BassFlags.Decode);
            if (offline == 0)
                throw new AudioException("Could not create mixer", Bass.LastError);
            return new OutputBus(offline, null);
        }

        try
        {
            Engine.EnsureInitialized(device.Index);
        }
        catch (AudioException ex) when (ex.Error == Errors.Busy)
        {
            throw AudioException.Busy(device.Name);
        }
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
