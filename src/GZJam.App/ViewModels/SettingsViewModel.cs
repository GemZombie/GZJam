using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GZJam.Audio;

namespace GZJam.App.ViewModels;

/// <summary>A choice in a settings drop-down.</summary>
public sealed record Choice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>The settings flyout: audio outputs and deck safety.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    /// <summary>Represents "no headphone output" (and no booth).</summary>
    public static readonly AudioDevice NoDevice = new(-1, "None", "none", false);   // a driver, so it isn't taken for "Default"

    private readonly MainWindowViewModel _owner;
    private readonly AppSettings _settings;
    private bool _loading;

    public SettingsViewModel(MainWindowViewModel owner, AppSettings settings)
    {
        _owner = owner;
        _settings = settings;
        RefreshDevices();
    }

    [ObservableProperty]
    public partial IReadOnlyList<AudioDevice> MainDevices { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<AudioDevice> CueDevices { get; private set; } = [];

    [ObservableProperty]
    public partial AudioDevice? MainDevice { get; set; }

    [ObservableProperty]
    public partial AudioDevice? CueDevice { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<AudioDevice> BoothDevices { get; private set; } = [];

    /// <summary>The booth (monitor) output, or <see cref="NoDevice"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBooth))]
    public partial AudioDevice? BoothDevice { get; set; }

    /// <summary>A booth output is chosen, so its volume shows.</summary>
    public bool HasBooth => BoothDevice is not null && BoothDevice != NoDevice;

    /// <summary>Booth volume, 0-100, in steps of 5.</summary>
    public double BoothVolume
    {
        get => _owner.Mixer?.BoothVolume ?? _settings.BoothVolume;
        set
        {
            value = Math.Clamp(Math.Round(value / 5) * 5, 0, 100);
            if (_owner.Mixer is { } mixer)
                mixer.BoothVolume = value;
            else
                _settings.BoothVolume = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BoothVolumeText));
        }
    }

    public string BoothVolumeText => _owner.Mixer?.BoothVolumeText ?? $"{BoothVolume:0}%";

    [RelayCommand]
    private void BoothQuieter() => BoothVolume -= 5;

    [RelayCommand]
    private void BoothLouder() => BoothVolume += 5;

    public IReadOnlyList<Choice<PlayingDeckLoad>> LoadChoices { get; } =
    [
        new(PlayingDeckLoad.Block, "Block it (safest)"),
        new(PlayingDeckLoad.Ask, "Ask first"),
        new(PlayingDeckLoad.Allow, "Allow it"),
    ];

    public IReadOnlyList<Choice<OnAirPause>> PauseChoices { get; } =
    [
        new(OnAirPause.Tap, "Tap"),
        new(OnAirPause.TapTwice, "Tap twice (or press and hold)"),
    ];

    public Choice<PlayingDeckLoad> LoadChoice
    {
        get => LoadChoices.First(c => c.Value == _settings.LoadOntoPlayingDeck);
        set
        {
            _settings.LoadOntoPlayingDeck = value.Value;
            OnPropertyChanged();
        }
    }

    public Choice<OnAirPause> PauseChoice
    {
        get => PauseChoices.First(c => c.Value == _settings.OnAirPause);
        set
        {
            _settings.OnAirPause = value.Value;
            OnPropertyChanged();
            _owner.RefreshDecks();
        }
    }

    /// <summary>Speakers, booth and headphones play mono.</summary>
    public bool Mono
    {
        get => _owner.Mixer?.IsMono ?? _settings.Mono;
        set
        {
            if (_owner.Mixer is { } mixer)
                mixer.IsMono = value;
            else
                _settings.Mono = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Speakers for GZJam alone (see <see cref="AppSettings.ExclusiveSpeakers"/>).</summary>
    public bool ExclusiveSpeakers
    {
        get => _settings.ExclusiveSpeakers;
        set
        {
            if (value == _settings.ExclusiveSpeakers)
                return;
            _owner.SetExclusiveSpeakers(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(SpeakersAreWindowsDefault));
        }
    }

    /// <summary>
    /// The speakers GZJam has to itself are also Windows' default output, so other programs that
    /// play there (a browser) may refuse to play while GZJam is open.
    /// </summary>
    public bool SpeakersAreWindowsDefault =>
        ShowsExclusiveSpeakers && ExclusiveSpeakers && MainDevice is { IsDefault: true, FollowsSystemDefault: false };

    /// <summary>Extra protection against crackles (see <see cref="AppSettings.ExtraBuffering"/>).</summary>
    public bool ExtraBuffering
    {
        get => _settings.ExtraBuffering;
        set
        {
            if (value == _settings.ExtraBuffering)
                return;
            _owner.SetExtraBuffering(value);
            OnPropertyChanged();
        }
    }

    /// <summary>Exclusive output is a Windows matter: Linux already opens a chosen card directly.</summary>
    public bool ShowsExclusiveSpeakers => OperatingSystem.IsWindows();

    /// <summary>Loudness matching (see <see cref="AppSettings.MatchLoudness"/>).</summary>
    public bool MatchLoudness
    {
        get => _settings.MatchLoudness;
        set
        {
            if (value == _settings.MatchLoudness)
                return;
            _settings.MatchLoudness = value;
            Log.Info($"Loudness matching {(value ? "on" : "off")}");
            foreach (DeckViewModel deck in _owner.Decks)
                deck.LoudnessSettingChanged();
            OnPropertyChanged();
        }
    }

    /// <summary>Touch mode (see <see cref="AppSettings.TouchMode"/>).</summary>
    public bool TouchMode
    {
        get => _settings.TouchMode;
        set
        {
            if (value == _settings.TouchMode)
                return;
            _settings.TouchMode = value;
            Log.Info($"Touch mode {(value ? "on" : "off")}");
            OnPropertyChanged();
        }
    }

    /// <summary>Four decks on screen rather than two.</summary>
    public bool FourDecks
    {
        get => _owner.FourDecks;
        set
        {
            _owner.SetDeckCount(value ? 4 : 2);
            OnPropertyChanged();   // refused (a deck is playing): the switch goes back
        }
    }

    public double FadeOutSeconds
    {
        get => _settings.FadeOutSeconds;
        set
        {
            _settings.FadeOutSeconds = Math.Clamp(Math.Round(value), 1, 30);
            OnPropertyChanged();
            OnPropertyChanged(nameof(FadeOutText));
        }
    }

    public string FadeOutText => $"{FadeOutSeconds:0} s";

    [RelayCommand]
    private void FadeOutShorter() => FadeOutSeconds -= 1;

    [RelayCommand]
    private void FadeOutLonger() => FadeOutSeconds += 1;

    /// <summary>How far <c>~bpm:</c> searches (and the BPM button's "Near Deck") reach either side, in percent.</summary>
    public double NearBpmPercent
    {
        get => _settings.NearBpmPercent;
        set
        {
            _settings.NearBpmPercent = Math.Clamp(Math.Round(value), 1, 20);
            _owner.Library.NearBpm = _settings.NearBpmPercent / 100;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NearBpmText));
        }
    }

    public string NearBpmText => $"±{NearBpmPercent:0}%";

    [RelayCommand]
    private void NearBpmCloser() => NearBpmPercent -= 1;

    [RelayCommand]
    private void NearBpmWider() => NearBpmPercent += 1;

    /// <summary>Re-lists devices, e.g. after plugging in a USB interface.</summary>
    [RelayCommand]
    public void RefreshDevices()
    {
        if (_owner.Audio is not { } audio)
            return;
        _loading = true;
        var devices = audio.GetOutputDevices();
        Log.Info("Output devices: " + string.Join("; ", devices.Select(d => $"{d.Index} \"{d.Name}\"" + (d.SystemName != d.Name ? $" (\"{d.SystemName}\")" : "") + $" [{d.Driver}]")));
        MainDevices = devices;
        CueDevices = devices.Prepend(NoDevice).ToList();
        BoothDevices = CueDevices;
        MainDevice = devices.FirstOrDefault(d => d.Index == _owner.Mix?.Main.Device?.Index);
        CueDevice = devices.FirstOrDefault(d => d.Index == _owner.Mix?.Cue.Device?.Index) ?? NoDevice;
        BoothDevice = devices.FirstOrDefault(d => d.Index == _owner.Mix?.Booth.Device?.Index) ?? NoDevice;
        _loading = false;
    }

    partial void OnMainDeviceChanged(AudioDevice? value)
    {
        OnPropertyChanged(nameof(SpeakersAreWindowsDefault));
        if (!_loading && value is not null)
            _owner.SetOutputs(value, Chosen(CueDevice), Chosen(BoothDevice));
    }

    partial void OnCueDeviceChanged(AudioDevice? value)
    {
        if (!_loading)
            _owner.SetOutputs(MainDevice, Chosen(value), Chosen(BoothDevice));
    }

    partial void OnBoothDeviceChanged(AudioDevice? value)
    {
        if (_loading)
            return;
        _owner.SetOutputs(MainDevice, Chosen(CueDevice), Chosen(value));
        if (Chosen(value) is not null && _owner.Mix?.Booth.Device is null)
            RefreshDevices();   // refused (the speakers' own output, say): back to None
    }

    private static AudioDevice? Chosen(AudioDevice? device) => device == NoDevice ? null : device;
}
