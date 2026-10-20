using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GZJam.Audio;

namespace GZJam.App.ViewModels;

/// <summary>The crossfader, which decides what the room hears and what goes to the headphones.</summary>
public sealed partial class MixerViewModel(MainWindowViewModel owner, MixEngine mix) : ObservableObject
{
    private static readonly TimeSpan SnapTime = TimeSpan.FromMilliseconds(300);
    private double? _snapTarget;
    private double _snapFrom;
    private long _snapStarted;

    public double Crossfader
    {
        get => mix.Crossfader;
        set
        {
            if (Math.Abs(mix.Crossfader - value) < 0.0001)
                return;
            _snapTarget = null;   // grabbing the fader cancels a snap in progress
            mix.Crossfader = value;
            Changed();
        }
    }

    public string LiveText => $"{mix.LiveDeck.Name} · LIVE";
    public bool IsALive => mix.LiveDeck.Index == 0;
    public bool IsBLive => mix.LiveDeck.Index == 1;

    /// <summary>Glides the fader fully to Deck A: a quick, click-free switch that's easy to hit on a touchscreen.</summary>
    [RelayCommand]
    private void SnapToA() => SnapTo(0);

    [RelayCommand]
    private void SnapToB() => SnapTo(1);

    private void SnapTo(double target)
    {
        _snapFrom = mix.Crossfader;
        _snapTarget = target;
        _snapStarted = TimeProvider.System.GetTimestamp();
        Update();
    }

    /// <summary>Advances a snap in progress; called on every UI tick.</summary>
    public void Update()
    {
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
        OnPropertyChanged(nameof(IsALive));
        OnPropertyChanged(nameof(IsBLive));
        owner.RefreshDecks();
    }
}
