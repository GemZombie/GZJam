using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Views;

/// <summary>
/// A song's info (#129): its right-click menu › <em>Song info…</em> in the song list (and an open
/// playlist), the queue or History, or Ctrl+I (Cmd+I) for the song selected in whichever has the
/// keyboard. One window at a time.
/// </summary>
public partial class MainWindow
{
    private SongInfoWindow? _songInfo;

    /// <summary>The song info window open now, if one is.</summary>
    internal SongInfoWindow? SongInfo => _songInfo;

    private void SetUpSongInfo()
    {
        var gesture = new KeyGesture(Key.I, CommandModifier);
        TrackInfoItem.InputGesture = gesture;
        QueueInfoItem.InputGesture = gesture;
        HistoryInfoItem.InputGesture = gesture;
    }

    /// <summary>Opens a song's info, in place of one open already.</summary>
    internal SongInfoWindow ShowSongInfo(Track track)
    {
        _songInfo?.Close();
        var window = new SongInfoWindow(new SongInfoViewModel(track, Vm));
        window.Closed += (_, _) =>
        {
            if (_songInfo == window)
                _songInfo = null;
        };
        _songInfo = window;
        window.Show(this);
        Log.Info($"Song info for \"{track.DisplayTitle}\"");
        return window;
    }

    /// <summary>Ctrl+I: the info of the one song selected where the keyboard is (the queue, History, else the song list).</summary>
    private bool ShowSelectedSongInfo()
    {
        Track? track = QueueList.IsKeyboardFocusWithin ? SelectedQueueItems() is [var item] ? item.Track : null
            : HistoryList.IsKeyboardFocusWithin ? SelectedPlays() is [var play] ? play.Track : null
            : SelectedTracks() is [var selected] ? selected : null;
        if (track is null)
            return false;
        ShowSongInfo(track);
        return true;
    }

    private void OnTrackInfoClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedTracks() is [var track])
            ShowSongInfo(track);
    }

    private void OnQueueInfoClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedQueueItems() is [var item])
            ShowSongInfo(item.Track);
    }

    private void OnHistoryInfoClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedPlays() is [var play])
            ShowSongInfo(play.Track);
    }
}
