using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GZJam.Audio;
using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>The headphone preview player under the track list.</summary>
public sealed partial class PreviewViewModel(MainWindowViewModel owner, PreviewPlayer player) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive), nameof(Title), nameof(Artist))]
    public partial Track? Track { get; private set; }

    public bool IsActive => Track is not null;
    public string Title => Track?.DisplayTitle ?? "";
    public string Artist => Track?.Artist ?? "";
    public double Progress => player.Length > TimeSpan.Zero ? player.Position / player.Length : 0;
    public string TimeText => $"{Format.Duration(player.Position)} / {Format.Duration(player.Length)}";

    /// <summary>Starts previewing, or stops if this track is already previewing (a second tap stops it).</summary>
    public void Toggle(Track track)
    {
        if (Track?.Path == track.Path)
        {
            Stop();
            return;
        }
        if (!owner.HasCueOutput)
        {
            owner.ShowNotice("Choose a headphone output in Settings to preview tracks.");
            return;
        }
        try
        {
            _loudness = owner.GetLoudness(track.Path);
            Apply(owner.GetEq(track.Path));   // as a deck would play it (#137)
            player.Play(track.Path);
            Track = track;
        }
        catch (AudioException ex)
        {
            owner.ShowNotice($"Couldn't preview {track.DisplayTitle}: {ex.Message}");
        }
        Refresh();
    }

    private (double? Lufs, double? PeakDb) _loudness;
    private TrackEq _eq;

    /// <summary>The song's EQ, evening out and loudness matching, on the preview.</summary>
    private void Apply(TrackEq eq)
    {
        _eq = eq;
        player.SetEq(eq.Low, eq.Mid, eq.High, eq.MidHz);
        player.Even = new Deck.Evening(eq.Even / TrackEq.MaxEven, _loudness.Lufs ?? DeckViewModel.UnmeasuredLufs);
        player.GainDb = owner.MatchDb(_loudness.Lufs, _loudness.PeakDb) + eq.Gain;
    }

    /// <summary>A song's EQ changed: heard at once if it's the one previewing.</summary>
    internal void EqChanged(string path, TrackEq eq)
    {
        if (Track?.Path == path)
            Apply(eq);
    }

    /// <summary>Loudness matching was turned on or off in Settings, or its target changed.</summary>
    internal void LoudnessSettingChanged() => Apply(_eq);

    [RelayCommand]
    public void Stop()
    {
        player.Stop();
        Track = null;
        Refresh();
    }

    public void SeekTo(double fraction)
    {
        player.Seek(player.Length * Math.Clamp(fraction, 0, 1));
        Refresh();
    }

    public void Refresh()
    {
        if (Track is not null && !player.IsLoaded)
            Track = null;   // reached the end
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(TimeText));
    }
}
