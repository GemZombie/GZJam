using ManagedBass;
using ManagedBass.Mix;

namespace GZJam.Audio;

/// <summary>
/// An output at the DJ table, not the room's: unplugged during a night, it goes quiet and comes
/// back when plugged in again, without touching the speakers.
/// </summary>
public enum TableOutput
{
    Headphones,
    Booth,
}

/// <summary>
/// The DJ mixer: decks and a headphone preview player, feeding a main (speakers) bus, a cue
/// (headphones) bus and a booth (monitor) bus. Every deck feeds all three at all times: the
/// crossfader sets how loud each deck is in the room (and the booth, which hears what the room
/// hears), and each deck's cue button sets whether it's in the headphones. Neither interrupts
/// playback.
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
        Booth = CreateBus(null);
        Decks = Enumerable.Range(0, deckCount).Select(i => new Deck(this, i)).ToList();
        Preview = new PreviewPlayer(this);
        Reconnect = TryReconnect;
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
    /// Why the booth output couldn't be opened at the last <see cref="SetOutputs"/>, or null. Like
    /// the headphones, it never stops the speakers opening; the booth is then silent.
    /// </summary>
    public string? BoothRefusal { get; private set; }

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

    /// <summary>
    /// The headphones' or booth's output stopped by itself: unplugged. It stays quiet (nothing
    /// moves to another output) and is tried again every couple of seconds. Raised from <see cref="Update"/>.
    /// </summary>
    public event EventHandler<TableOutput>? OutputUnplugged;

    /// <summary>An unplugged headphone or booth output plays again. Raised from <see cref="Update"/>.</summary>
    public event EventHandler<TableOutput>? OutputBack;

    /// <summary>The headphones or booth are unplugged (or wouldn't open), waiting to come back.</summary>
    public bool IsUnplugged(TableOutput output) => _unplugged[(int)output];

    /// <summary>
    /// The device the headphones or booth are waiting for, while unplugged: still the DJ's choice
    /// (Settings lists only what's plugged in).
    /// </summary>
    public AudioDevice? WaitingFor(TableOutput output) => _waiting[(int)output];

    private readonly bool[] _unplugged = new bool[2];
    private readonly AudioDevice?[] _waiting = new AudioDevice?[2];
    private readonly long[] _nextReconnect = new long[2];

    /// <summary>How often an unplugged output is looked for again.</summary>
    private const int ReconnectMilliseconds = 2000;

    /// <summary>
    /// Whether an output's device has stopped by itself (BASS: "the channel's device is paused",
    /// e.g. a USB card disconnected). Tests stand in for a real unplugging.
    /// </summary>
    internal Func<OutputBus, bool> DeviceStopped { get; set; } = bus =>
        !bus.IsOffline && Bass.ChannelIsActive(bus.Handle) == PausedDevice;

    /// <summary>BASS_ACTIVE_PAUSED_DEVICE, newer than ManagedBass's list.</summary>
    private const PlaybackState PausedDevice = (PlaybackState)4;

    /// <summary>Tries to bring an unplugged output back; true if it plays. Tests stand in for the device.</summary>
    internal Func<TableOutput, bool> Reconnect { get; set; }

    public OutputBus Main { get; private set; }
    public OutputBus Cue { get; private set; }

    /// <summary>
    /// The speakers and headphones share one output, through a splitter cable: the speakers in
    /// mono on its left channel, the headphones in mono on its right. Chosen by giving the
    /// headphones the speakers' own output.
    /// </summary>
    public bool IsSplit => _carrier is not null;

    /// <summary>When split: the output on the device, carrying both (see <see cref="Split"/>).</summary>
    private OutputBus? _carrier;

    /// <summary>What plays on the speakers' device: GZJam's alone or not, unplugged or not.</summary>
    private OutputBus SpeakerOutput => _carrier ?? Main;

    /// <summary>GZJam has the speakers' output to itself (see <see cref="ExclusiveSpeakers"/>).</summary>
    public bool SpeakersAreExclusive => SpeakerOutput.IsExclusive;

    /// <summary>A monitor speaker for the DJ: what the room hears, at its own volume. Never the headphones' preview.</summary>
    public OutputBus Booth { get; private set; }
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

    private double _phonesMix;

    /// <summary>
    /// What the headphones hear: 0 = what's cued (the decks' cue buttons and the preview), 1 = what
    /// the room hears. In between, both; at the center both at full, like a DJ mixer's cue/master knob.
    /// </summary>
    public double PhonesMix
    {
        get => _phonesMix;
        set
        {
            _phonesMix = Math.Clamp(value, 0, 1);
            foreach (Deck deck in Decks)
                deck.ApplyVolumes();
            Preview.ApplyVolume();
        }
    }

    /// <summary>How much of what's cued the headphones get, from <see cref="PhonesMix"/>.</summary>
    internal double PhonesCueLevel => Math.Min(1, 2 * (1 - _phonesMix));

    /// <summary>How much of what the room hears the headphones get, from <see cref="PhonesMix"/>.</summary>
    internal double PhonesRoomLevel => Math.Min(1, 2 * _phonesMix);

    /// <summary>The side the crossfader leans toward: 0 = left, 1 = right (left at dead center).</summary>
    public int LiveSide => _crossfader <= 0.5 ? 0 : 1;

    /// <summary>
    /// The deck the crossfader leans toward: the one playing on that side, else the first deck
    /// on it. Both sides may be audible in a blend.
    /// </summary>
    public Deck LiveDeck =>
        Decks.FirstOrDefault(d => Crossfade.Side(d.Index) == LiveSide && d.IsPlaying) ?? Decks[LiveSide];

    /// <summary>
    /// Points the buses at audio devices. Decks and preview keep playing through the switch.
    /// A null cue device leaves the headphones silent (they are never sent to the speakers); a null
    /// booth device, the booth. Headphones on the speakers' own output split it (see
    /// <see cref="IsSplit"/>). A booth on the speakers' or headphones' own output isn't opened.
    /// </summary>
    public void SetOutputs(AudioDevice? main, AudioDevice? cue, AudioDevice? booth = null)
    {
        if (_offline)
            throw new InvalidOperationException("An offline mix engine has no devices.");

        OutputBus oldMain = Main, oldCue = Cue, oldBooth = Booth;
        foreach (Deck deck in Decks)
            deck.Detach();
        Preview.Detach();

        // Let go of the old outputs before opening the new ones. A Linux sound card opens only
        // once, and GZJam's own "Default" (through PipeWire) may be playing on the very card chosen.
        _carrier?.Dispose();
        _carrier = null;
        oldMain.Dispose();
        oldCue.Dispose();
        oldBooth.Dispose();
        // An output wanted for GZJam alone is let go of too: Windows won't give it exclusively
        // while GZJam itself still has it shared.
        Engine.ReleaseDevicesExcept(WantsExclusive(main) ? null : main?.Index, cue?.Index == main?.Index ? null : cue?.Index, booth?.Index);
        try
        {
            Open(main, cue, booth, oldMain, oldCue, oldBooth);
        }
        catch (AudioException)
        {
            // Back to what was playing, so a refused output doesn't silence the room.
            Engine.ReleaseDevicesExcept(oldMain.Device?.Index, oldCue.Device?.Index, oldBooth.Device?.Index);
            try
            {
                Open(oldMain.Device, oldCue.Device, oldBooth.Device, oldMain, oldCue, oldBooth);
            }
            catch (AudioException)
            {
                // The old speakers are gone too (unplugged): silent speakers, but the headphones
                // and booth, if they still open, carry on.
                Open(null, oldCue.Device, oldBooth.Device, oldMain, oldCue, oldBooth);
            }
            throw;
        }
    }

    private void Open(AudioDevice? main, AudioDevice? cue, AudioDevice? booth,
        OutputBus settingsMain, OutputBus settingsCue, OutputBus settingsBooth)
    {
        OutputBus newMain, newCue;
        CueRefusal = null;
        if (main is not null && cue?.Index == main.Index)
        {
            // One output, split: the speakers and headphones each keep their own mix, volume and
            // limiter, then go to one side each of the speakers' output.
            OutputBus carrier = CreateMainBus(main);
            newMain = new OutputBus(CreateMixer(BassFlags.Decode), main);
            newCue = new OutputBus(CreateMixer(BassFlags.Decode), main);
            Split(carrier.Handle, newMain, newCue);
            _carrier = carrier;
        }
        else
        {
            newMain = CreateMainBus(main);
            try
            {
                newCue = CreateBus(cue);
            }
            catch (AudioException ex)
            {
                // The room comes first: the speakers play on, the headphones are silent (until
                // they open), and the app says why.
                CueRefusal = ex.Message;
                newCue = CreateBus(null);
            }
        }
        OutputBus newBooth = OpenBooth(booth, main, cue);
        newMain.CopySettingsFrom(settingsMain);
        newCue.CopySettingsFrom(settingsCue);
        newBooth.CopySettingsFrom(settingsBooth);
        Main = newMain;
        Cue = newCue;
        Booth = newBooth;
        _mainLostRaised = false;
        _dryAt = -1;
        Array.Clear(_unplugged);
        Array.Clear(_waiting);
        // Headphones or a booth that wouldn't open (unplugged, say): waited for, as if unplugged
        // while playing, so they still come back by themselves.
        if (CueRefusal is not null)
            Wait(TableOutput.Headphones, cue);
        if (_boothFailed)
            Wait(TableOutput.Booth, booth);

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
        if (!_mainLostRaised && SpeakerOutput.IsLost)
        {
            _mainLostRaised = true;
            _dryAt = -1;
            MainOutputLost?.Invoke(this, EventArgs.Empty);
        }
        else if (_dryAt < 0 && now - _lastDropout > 10_000 && SpeakerOutput.IsDry())
        {
            _dryAt = now;
        }
        WatchTableOutputs(now);
        // A card being unplugged runs dry a moment before Windows says it's gone: only a dry
        // buffer that isn't followed by the card going within half a second is a dropout.
        if (_dryAt >= 0 && now - _dryAt >= 500)
        {
            _dryAt = -1;
            if (!SpeakerOutput.IsLost)
            {
                _lastDropout = now;
                MainDropout?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private OutputBus BusFor(TableOutput output) => output == TableOutput.Headphones ? Cue : Booth;

    /// <summary>
    /// Notices the headphones or booth stopping by themselves, and brings them back once they're
    /// plugged in again. Split headphones are on the speakers' output, which has its own watch.
    /// </summary>
    private void WatchTableOutputs(long now)
    {
        foreach (TableOutput output in (TableOutput[])[TableOutput.Headphones, TableOutput.Booth])
        {
            if (output == TableOutput.Headphones && IsSplit)
                continue;
            int i = (int)output;
            if (!_unplugged[i])
            {
                if (!DeviceStopped(BusFor(output)))
                    continue;
                Wait(output, BusFor(output).Device);
                OutputUnplugged?.Invoke(this, output);
            }
            else if (now >= _nextReconnect[i])
            {
                _nextReconnect[i] = now + ReconnectMilliseconds;
                if (Reconnect(output))
                {
                    _unplugged[i] = false;
                    _waiting[i] = null;
                    OutputBack?.Invoke(this, output);
                }
            }
        }
    }

    /// <summary>
    /// Once the output is listed again: starts its device again, or if that isn't enough (or it
    /// came back as a different device, as Linux may number it), opens it afresh.
    /// </summary>
    private bool TryReconnect(TableOutput output)
    {
        OutputBus bus = BusFor(output);
        if (_waiting[(int)output] is not { } was || Engine.FindDevice(was.Preference) is not { } device)
            return false;   // not back yet
        if (device.Index == bus.Device?.Index)
        {
            Bass.CurrentDevice = device.Index;
            if (Bass.Start() && Bass.ChannelIsActive(bus.Handle) == PlaybackState.Playing)
                return true;
        }
        try
        {
            Reopen(output, device, waitIfBusy: false);
            return true;
        }
        catch (AudioException)
        {
            return false;   // tried again in a moment
        }
    }

    /// <summary>
    /// Opens the headphones or booth again on <paramref name="device"/> (null: offline, for tests),
    /// moving the decks' feeds (and the preview, for the headphones) to it. The speakers play on
    /// untouched. If it won't open, that output is left silent and the error thrown.
    /// </summary>
    internal void Reopen(TableOutput output, AudioDevice? device, bool waitIfBusy = true)
    {
        OutputBus old = BusFor(output);
        foreach (Deck deck in Decks)
            deck.Detach(output);
        if (output == TableOutput.Headphones)
            Preview.Detach();
        old.Dispose();
        if (old.Device is { } was)
            Engine.ReleaseDevice(was.Index);
        OutputBus bus;
        AudioException? failed = null;
        try
        {
            bus = CreateBus(device, waitIfBusy);
        }
        catch (AudioException ex)
        {
            failed = ex;
            bus = CreateBus(null);
        }
        bus.CopySettingsFrom(old);
        if (output == TableOutput.Headphones)
            Cue = bus;
        else
            Booth = bus;
        foreach (Deck deck in Decks)
            deck.Attach(output);
        if (output == TableOutput.Headphones)
            Preview.Attach();
        if (failed is not null)
            throw failed;
    }

    /// <summary>Waits for the headphones or booth to be plugged in again, trying every couple of seconds.</summary>
    private void Wait(TableOutput output, AudioDevice? device)
    {
        int i = (int)output;
        _unplugged[i] = true;
        _waiting[i] = device;
        _nextReconnect[i] = Environment.TickCount64 + ReconnectMilliseconds;
    }

    /// <summary>Offline only: renders the speakers and headphones in small interleaved chunks, returning each one's peak level.</summary>
    public (float Main, float Cue) Render(TimeSpan duration)
    {
        var (main, cue, _) = RenderAll(duration);
        return (main, cue);
    }

    /// <summary>Offline only: <see cref="Render"/>, with the booth too.</summary>
    public (float Main, float Cue, float Booth) RenderAll(TimeSpan duration)
    {
        float main = 0, cue = 0, booth = 0;
        var chunk = TimeSpan.FromMilliseconds(50);
        for (var done = TimeSpan.Zero; done < duration; done += chunk)
        {
            main = Math.Max(main, Main.Render(chunk));
            cue = Math.Max(cue, Cue.Render(chunk));
            booth = Math.Max(booth, Booth.Render(chunk));
        }
        return (main, cue, booth);
    }

    internal void ApplyRouting()
    {
        foreach (Deck deck in Decks)
        {
            deck.SetMainGain(Crossfade.MainGain(Crossfade.Audibility(deck.Index, _crossfader)));
        }
    }

    /// <summary>The speakers are to be GZJam's alone (split with the headphones or not).</summary>
    private bool WantsExclusive(AudioDevice? device) =>
        ExclusiveSpeakers && OperatingSystem.IsWindows() && device is { FollowsSystemDefault: false };

    /// <summary>The speakers' output: GZJam's alone if wanted and possible, else shared (saying why).</summary>
    private OutputBus CreateMainBus(AudioDevice? device)
    {
        ExclusiveRefusal = null;
        if (WantsExclusive(device))
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

    /// <summary>
    /// The booth, if it can open. Never on the speakers' or headphones' own output (it would play
    /// the room twice over, or into the headphones); a booth that won't open is silent, saying why.
    /// </summary>
    private OutputBus OpenBooth(AudioDevice? booth, AudioDevice? main, AudioDevice? cue)
    {
        BoothRefusal = null;
        _boothFailed = false;
        if (booth is null)
            return CreateBus(null);
        if (booth.Index == main?.Index || booth.Index == cue?.Index)
        {
            BoothRefusal = $"{booth.Name} is also the {(booth.Index == main?.Index ? "speaker" : "headphone")} output";
            return CreateBus(null);
        }
        try
        {
            return CreateBus(booth);
        }
        catch (AudioException ex)
        {
            BoothRefusal = ex.Message;
            _boothFailed = true;
            return CreateBus(null);
        }
    }

    /// <summary>The booth's device wouldn't open at the last <see cref="SetOutputs"/> (not refused for being another output's).</summary>
    private bool _boothFailed;

    // NonStop: keep the output running through silence, so starting a track is instant.
    private const BassFlags MixerFlags = BassFlags.Float | BassFlags.MixerNonStop;

    /// <summary>A mixer playing on the device, or with none, one that tests pull (offline).</summary>
    private OutputBus CreateBus(AudioDevice? device, bool waitIfBusy = true) =>
        device is null ? new OutputBus(CreateMixer(BassFlags.Decode), null) : CreateDeviceBus(device, waitIfBusy);

    /// <summary>A mixer on no device: pulled by tests, or by another mixer.</summary>
    private int CreateMixer(BassFlags extra)
    {
        Engine.UseNoSoundDevice();
        int mixer = BassMix.CreateMixerStream(SampleRate, Channels, MixerFlags | extra);
        if (mixer == 0)
            throw new AudioException("Could not create mixer", Bass.LastError);
        return mixer;
    }

    /// <summary>
    /// The speakers to the left channel of <paramref name="carrier"/> and the headphones to the
    /// right, each in mono: a splitter cable gives each side its own plug.
    /// </summary>
    internal static void Split(int carrier, OutputBus speakers, OutputBus headphones)
    {
        foreach (var (bus, side) in new[] { (speakers, 0), (headphones, 1) })
        {
            if (!BassMix.MixerAddChannel(carrier, bus.Handle, BassFlags.MixerChanMatrix))
                throw new AudioException("Could not split the output", Bass.LastError);
            var matrix = new float[Channels, Channels];   // [out, in]
            matrix[side, 0] = matrix[side, 1] = 0.5f;
            if (!BassMix.ChannelSetMatrix(bus.Handle, matrix))
                throw new AudioException("Could not split the output", Bass.LastError);
        }
    }

    private OutputBus CreateDeviceBus(AudioDevice device, bool waitIfBusy)
    {
        const BassFlags flags = MixerFlags;

        try
        {
            Engine.EnsureInitialized(device.Index, waitIfBusy);
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
        _carrier?.Dispose();
        Main.Dispose();
        Cue.Dispose();
        Booth.Dispose();
    }
}
