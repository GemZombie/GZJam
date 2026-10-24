using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GZJam.Audio;

namespace GZJam.App.ViewModels;

/// <summary>
/// The mixer column: the crossfader, which fades the room between the left side (Deck 1, and 3
/// with four decks) and the right (Deck 2, and 4); the headphones are separate: each deck's cue
/// button. Plus speaker and headphone volume and the speakers' level meter.
/// </summary>
public sealed partial class MixerViewModel : ObservableObject
{
    private static readonly TimeSpan SnapTime = TimeSpan.FromMilliseconds(300);

    /// <summary>How long the limit light stays on after the limiter acts, so a brief peak is noticed.</summary>
    private static readonly TimeSpan LimitHold = TimeSpan.FromSeconds(1.5);

    private readonly MainWindowViewModel owner;
    private readonly MixEngine mix;
    private readonly AppSettings _settings;
    private long _limitedAt;
    private double? _snapTarget;
    private double _snapFrom;
    private long _snapStarted;
    private TimeSpan _snapTime = SnapTime;
    private string? _liveText;

    public MixerViewModel(MainWindowViewModel owner, MixEngine mix, AppSettings settings)
    {
        this.owner = owner;
        this.mix = mix;
        _settings = settings;
        mix.Main.Mono = mix.Cue.Mono = mix.Booth.Mono = settings.Mono;
        mix.Main.Volume = Gain(settings.MainVolume);
        mix.Cue.Volume = Gain(settings.CueVolume);
        mix.Booth.Volume = Gain(settings.BoothVolume);
        mix.PhonesMix = settings.PhonesMix;
    }

    /// <summary>Volume percent to gain: an audio taper, so the slider's middle is about half as loud (-12 dB).</summary>
    private static float Gain(double percent) => (float)Math.Pow(Math.Clamp(percent, 0, 100) / 100, 2);

    /// <summary>Speaker volume, 0-100.</summary>
    public double MainVolume
    {
        get => _settings.MainVolume;
        set
        {
            value = Math.Clamp(Math.Round(value), 0, 100);
            if (value == _settings.MainVolume)
                return;
            _settings.MainVolume = value;
            mix.Main.Volume = Gain(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(MainVolumeText));
            OnPropertyChanged(nameof(MainVolumeTip));
        }
    }

    /// <summary>Headphone volume, 0-100.</summary>
    public double CueVolume
    {
        get => _settings.CueVolume;
        set
        {
            value = Math.Clamp(Math.Round(value), 0, 100);
            if (value == _settings.CueVolume)
                return;
            _settings.CueVolume = value;
            mix.Cue.Volume = Gain(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(CueVolumeText));
            OnPropertyChanged(nameof(CueVolumeTip));
        }
    }

    /// <summary>Booth volume, 0-100 (set in Settings › Sound, beside the booth output).</summary>
    public double BoothVolume
    {
        get => _settings.BoothVolume;
        set
        {
            value = Math.Clamp(Math.Round(value), 0, 100);
            if (value == _settings.BoothVolume)
                return;
            _settings.BoothVolume = value;
            mix.Booth.Volume = Gain(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(BoothVolumeText));
        }
    }

    public string BoothVolumeText => Db(BoothVolume);

    /// <summary>
    /// The phones mix: 0 = the headphones hear what's cued (the decks' cue buttons and the
    /// preview), 1 = what the room hears; between, both (at the centre both at full).
    /// </summary>
    public double PhonesMix
    {
        get => _settings.PhonesMix;
        set
        {
            value = Math.Clamp(value, 0, 1);
            if (value == _settings.PhonesMix)
                return;
            _settings.PhonesMix = value;
            mix.PhonesMix = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PhonesMixTip));
        }
    }

    public string PhonesMixTip => PhonesMix switch
    {
        <= 0 => "Phones mix: what's cued. Slide towards MAIN to hear the room too, as the speakers play it.",
        >= 1 => "Phones mix: the room only, as the speakers play it (no cued decks or preview)",
        < 0.5 => "Phones mix: what's cued, with some of the room",
        0.5 => "Phones mix: what's cued and the room, both at full",
        _ => "Phones mix: the room, with some of what's cued",
    };

    public string MainVolumeText => Db(MainVolume);
    public string CueVolumeText => Db(CueVolume);
    public string MainVolumeTip => $"Main (speaker) volume: {MainVolumeText}";
    public string CueVolumeTip => $"Phones (headphone) volume: {CueVolumeText}";

    /// <summary>A volume as the dB it turns the output down by: "0 dB", "−6 dB", "off".</summary>
    private static string Db(double percent)
    {
        if (percent <= 0)
            return "off";
        double db = 20 * Math.Log10(Gain(percent));
        return Math.Abs(db) < 0.05 ? "0 dB" : $"−{-db:0.#} dB";
    }

    /// <summary>Speakers, booth and headphones play mono (a setting, on by default).</summary>
    public bool IsMono
    {
        get => mix.Main.Mono;
        set
        {
            if (value == mix.Main.Mono)
                return;
            mix.Main.Mono = mix.Cue.Mono = mix.Booth.Mono = value;
            _settings.Mono = value;
            Log.Info($"Speakers, booth and headphones: {(value ? "mono" : "stereo")}");
            OnPropertyChanged();
        }
    }

    /// <summary>The speakers' level, 0..1 along the meter, falling back smoothly.</summary>
    [ObservableProperty]
    public partial double LevelLeft { get; private set; }

    [ObservableProperty]
    public partial double LevelRight { get; private set; }

    /// <summary>The limiter is holding the speakers down: something is too loud.</summary>
    [ObservableProperty]
    public partial bool IsLimiting { get; private set; }

    private void UpdateMeter()
    {
        var (left, right, limited) = mix.Main.TakeLevels();
        LevelLeft = Math.Max(Controls.LevelMeter.FromPeak(left), LevelLeft - Controls.LevelMeter.Fall);
        LevelRight = Math.Max(Controls.LevelMeter.FromPeak(right), LevelRight - Controls.LevelMeter.Fall);
        if (limited)
            _limitedAt = TimeProvider.System.GetTimestamp();
        IsLimiting = _limitedAt != 0 && TimeProvider.System.GetElapsedTime(_limitedAt) < LimitHold;
    }

    /// <summary>How close to an end counts as the end, so a trackpad or finger needn't be pixel-perfect.</summary>
    private const double StickyEnd = 0.04;

    public double Crossfader
    {
        get => mix.Crossfader;
        set
        {
            value = value <= StickyEnd ? 0 : value >= 1 - StickyEnd ? 1 : value;
            if (Math.Abs(mix.Crossfader - value) < 0.0001)
            {
                OnPropertyChanged();   // snap the slider itself to the end too
                return;
            }
            _snapTarget = null;   // grabbing the fader cancels a snap in progress
            mix.Crossfader = value;
            Changed();
        }
    }

    /// <summary>
    /// Slides the crossfader to <paramref name="target"/> over <paramref name="time"/>: auto-play
    /// does this through the gap between songs, while the room is silent, so the DJ sees the hand
    /// over coming. Grabbing the fader stops it (<see cref="IsGliding"/> goes false).
    /// </summary>
    public void GlideTo(double target, TimeSpan time)
    {
        _snapFrom = mix.Crossfader;
        _snapTarget = target;
        _snapTime = time;
        _snapStarted = TimeProvider.System.GetTimestamp();
        Update();
    }

    /// <summary>A glide or a switch is under way (false once it's there, or the DJ grabbed the fader).</summary>
    public bool IsGliding => _snapTarget is not null;

    /// <summary>Moves the crossfader straight to a position (auto-play does this while the decks are silent).</summary>
    public void SetNow(double value)
    {
        _snapTarget = null;
        mix.Crossfader = value;
        Changed();
    }

    /// <summary>
    /// "1 · LIVE", "2 · LIVE", or "BLEND" when the room hears some of both. With four decks, the
    /// deck playing on the live side, or both of that side's decks ("1 / 3 · LIVE") if neither is.
    /// </summary>
    public string LiveText
    {
        get
        {
            if (mix.Crossfader is > 0.03 and < 0.97)
                return "BLEND";
            int side = mix.LiveSide;
            if (!owner.FourDecks || mix.LiveDeck.IsPlaying)
                return $"{mix.LiveDeck.Name} · LIVE";
            return $"{side + 1} / {side + 3} · LIVE";
        }
    }

    public bool IsDeck1Live => mix.LiveSide == 0;
    public bool IsDeck2Live => mix.LiveSide == 1;

    /// <summary>Decks 3 and 4 are on screen, so the side buttons show them too.</summary>
    public bool IsFourDecks => owner.FourDecks;

    public string LeftTip => owner.FourDecks ? "Switch the room to the left: Decks 1 and 3" : "Switch the room to Deck 1";
    public string RightTip => owner.FourDecks ? "Switch the room to the right: Decks 2 and 4" : "Switch the room to Deck 2";

    internal void DeckCountChanged()
    {
        OnPropertyChanged(nameof(IsFourDecks));
        OnPropertyChanged(nameof(LeftTip));
        OnPropertyChanged(nameof(RightTip));
        OnPropertyChanged(nameof(LiveText));
    }

    /// <summary>Glides the fader fully to the left (Deck 1): a quick, click-free switch that's easy to hit on a touchscreen.</summary>
    [RelayCommand]
    private void SnapTo1() => SnapTo(0);

    [RelayCommand]
    private void SnapTo2() => SnapTo(1);

    private void SnapTo(double target)
    {
        Log.Info($"Crossfader: switch to the {(target == 0 ? "left (Deck 1" : "right (Deck 2")}{(owner.FourDecks ? target == 0 ? " and 3" : " and 4" : "")}) (from {mix.Crossfader:0.00})");
        GlideTo(target, SnapTime);
    }

    /// <summary>Advances a snap in progress; called on every UI tick.</summary>
    public void Update()
    {
        UpdateMeter();
        if (LiveText != _liveText)
        {
            _liveText = LiveText;   // with four decks it follows which deck is playing, too
            OnPropertyChanged(nameof(LiveText));
        }
        if (_snapTarget is not { } target)
            return;
        double t = _snapTime <= TimeSpan.Zero ? 1 : Math.Min(1, TimeProvider.System.GetElapsedTime(_snapStarted) / _snapTime);
        mix.Crossfader = _snapFrom + (target - _snapFrom) * t;
        if (t >= 1)
            _snapTarget = null;
        Changed();
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(Crossfader));
        OnPropertyChanged(nameof(LiveText));
        OnPropertyChanged(nameof(IsDeck1Live));
        OnPropertyChanged(nameof(IsDeck2Live));
        owner.RefreshDecks();
    }
}
