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
            player.Play(track.Path);
            Track = track;
        }
        catch (AudioException ex)
        {
            owner.ShowNotice($"Couldn't preview {track.DisplayTitle}: {ex.Message}");
        }
        Refresh();
    }

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
