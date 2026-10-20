using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GZJam.Audio;

namespace GZJam.App.ViewModels;

/// <summary>The crossfader: fades the room between Deck 1 and Deck 2. The headphones are separate (each deck's cue button).</summary>
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

    /// <summary>"1 · LIVE", "2 · LIVE", or "BLEND" when the room hears some of both.</summary>
    public string LiveText => mix.Crossfader is > 0.03 and < 0.97 ? "BLEND" : $"{mix.LiveDeck.Name} · LIVE";
    public bool IsDeck1Live => mix.LiveDeck.Index == 0;
    public bool IsDeck2Live => mix.LiveDeck.Index == 1;

    /// <summary>Glides the fader fully to Deck 1: a quick, click-free switch that's easy to hit on a touchscreen.</summary>
    [RelayCommand]
    private void SnapTo1() => SnapTo(0);

    [RelayCommand]
    private void SnapTo2() => SnapTo(1);

    private void SnapTo(double target)
    {
        Log.Info($"Crossfader: switch to Deck {(target == 0 ? 1 : 2)} (from {mix.Crossfader:0.00})");
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
        OnPropertyChanged(nameof(IsDeck1Live));
        OnPropertyChanged(nameof(IsDeck2Live));
        owner.RefreshDecks();
    }
}
