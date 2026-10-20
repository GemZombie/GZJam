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

    public IReadOnlyList<Choice<LiveDeckPause>> PauseChoices { get; } =
    [
        new(LiveDeckPause.Hold, "Press and hold"),
        new(LiveDeckPause.Tap, "Tap"),
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

    public Choice<LiveDeckPause> PauseChoice
    {
        get => PauseChoices.First(c => c.Value == _settings.LiveDeckPause);
        set
        {
            _settings.LiveDeckPause = value.Value;
            OnPropertyChanged();
            _owner.RefreshDecks();
        }
    }

    public double FadeOutSeconds
    {
        get => _settings.FadeOutSeconds;
        set
        {
            _settings.FadeOutSeconds = Math.Clamp(Math.Round(value), 1, 30);
            OnPropertyChanged();
        }
    }

    /// <summary>Re-lists devices, e.g. after plugging in a USB interface.</summary>
    [RelayCommand]
    public void RefreshDevices()
    {
        if (_owner.Audio is not { } audio)
            return;
        _loading = true;
        var devices = audio.GetOutputDevices();
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
