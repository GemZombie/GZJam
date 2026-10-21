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

    /// <summary>How far the meter falls per tick (about 30 a second): roughly 30 dB a second.</summary>
    private const double MeterFall = 0.02;

    private readonly MainWindowViewModel owner;
    private readonly MixEngine mix;
    private readonly AppSettings _settings;
    private long _limitedAt;
    private double? _snapTarget;
    private double _snapFrom;
    private long _snapStarted;
    private string? _liveText;

    public MixerViewModel(MainWindowViewModel owner, MixEngine mix, AppSettings settings)
    {
        this.owner = owner;
        this.mix = mix;
        _settings = settings;
        mix.Main.Mono = settings.MonoSpeakers;
        mix.Main.Volume = Gain(settings.MainVolume);
        mix.Cue.Volume = Gain(settings.CueVolume);
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
        }
    }

    public string MainVolumeText => $"{MainVolume:0}";
    public string CueVolumeText => $"{CueVolume:0}";

    /// <summary>The speakers play mono (a setting, on by default).</summary>
    public bool IsMono
    {
        get => mix.Main.Mono;
        set
        {
            if (value == mix.Main.Mono)
                return;
            mix.Main.Mono = value;
            _settings.MonoSpeakers = value;
            Log.Info($"Speakers: {(value ? "mono" : "stereo")}");
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
        LevelLeft = Math.Max(Controls.LevelMeter.FromPeak(left), LevelLeft - MeterFall);
        LevelRight = Math.Max(Controls.LevelMeter.FromPeak(right), LevelRight - MeterFall);
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
        _snapFrom = mix.Crossfader;
        _snapTarget = target;
        _snapStarted = TimeProvider.System.GetTimestamp();
        Update();
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
        double t = Math.Min(1, TimeProvider.System.GetElapsedTime(_snapStarted) / SnapTime);
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
