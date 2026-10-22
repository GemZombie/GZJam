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
    /// <summary>Represents "no headphone output".</summary>
    public static readonly AudioDevice NoDevice = new(-1, "None", "", false);

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

    /// <summary>The speakers play mono (the headphones stay stereo).</summary>
    public bool MonoSpeakers
    {
        get => _owner.Mixer?.IsMono ?? _settings.MonoSpeakers;
        set
        {
            if (_owner.Mixer is { } mixer)
                mixer.IsMono = value;
            else
                _settings.MonoSpeakers = value;
            OnPropertyChanged();
        }
    }

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

    /// <summary>Re-lists devices, e.g. after plugging in a USB interface.</summary>
    [RelayCommand]
    public void RefreshDevices()
    {
        if (_owner.Audio is not { } audio)
            return;
        _loading = true;
        var devices = audio.GetOutputDevices();
        Log.Info("Output devices: " + string.Join("; ", devices.Select(d => $"{d.Index} \"{d.Name}\" [{d.Driver}]")));
        MainDevices = devices;
        CueDevices = devices.Prepend(NoDevice).ToList();
        MainDevice = devices.FirstOrDefault(d => d.Index == _owner.Mix?.Main.Device?.Index);
        CueDevice = devices.FirstOrDefault(d => d.Index == _owner.Mix?.Cue.Device?.Index) ?? NoDevice;
        _loading = false;
    }

    partial void OnMainDeviceChanged(AudioDevice? value)
    {
        if (!_loading && value is not null)
            _owner.SetOutputs(value, CueDevice == NoDevice ? null : CueDevice);
    }

    partial void OnCueDeviceChanged(AudioDevice? value)
    {
        if (!_loading)
            _owner.SetOutputs(MainDevice, value == NoDevice ? null : value);
    }
}
