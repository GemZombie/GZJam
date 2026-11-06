using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Views;

/// <summary>
/// A song's info (#129): its right-click menu › <em>Song info…</em> in the song list (and an open
/// playlist), the queue or History, or Ctrl+I (Cmd+I) for the song selected in whichever has the
/// keyboard; several songs selected, all of them, to edit together. One window at a time.
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

    /// <summary>Opens songs' info, in place of one open already (as it is, changes and all: see <see cref="OpenSongInfoAsync"/>).</summary>
    internal SongInfoWindow ShowSongInfo(Track track) => ShowSongInfo([track]);

    /// <inheritdoc cref="ShowSongInfo(Track)"/>
    internal SongInfoWindow ShowSongInfo(IReadOnlyList<Track> tracks)
    {
        _songInfo?.CloseWithoutAsking();
        var window = new SongInfoWindow(new SongInfoViewModel(tracks, Vm));
        window.Closed += (_, _) =>
        {
            if (_songInfo == window)
                _songInfo = null;
        };
        _songInfo = window;
        window.Show(this);
        Log.Info(tracks.Count == 1 ? $"Song info for \"{tracks[0].DisplayTitle}\"" : $"Song info for {tracks.Count} songs");
        return window;
    }

    /// <summary>
    /// Opens songs' info from a menu or Ctrl+I. One open with changes not saved asks first; kept,
    /// it stays, and comes to the front.
    /// </summary>
    internal async Task<SongInfoWindow?> OpenSongInfoAsync(IReadOnlyList<Track> tracks)
    {
        tracks = tracks.Where(t => !t.IsOnServerOnly).ToList();   // still on a server (#290): its info is the file's, once it's here
        if (tracks.Count == 0)
            return null;
        if (_songInfo is { } open && !await open.AskToDiscardAsync())
        {
            open.Activate();
            return null;
        }
        return ShowSongInfo(tracks);
    }

    /// <summary>Ctrl+I: the info of the songs selected where the keyboard is (the queue, History, else the song list).</summary>
    private bool ShowSelectedSongInfo()
    {
        IReadOnlyList<Track> tracks = QueueList.IsKeyboardFocusWithin ? [.. SelectedQueueItems().Select(i => i.Track)]
            : HistoryList.IsKeyboardFocusWithin ? [.. SelectedPlays().Select(p => p.Track)]
            : SelectedTracks();
        if (tracks.Count == 0)
            return false;
        _ = OpenSongInfoAsync(tracks);
        return true;
    }

    private void OnTrackInfoClick(object? sender, RoutedEventArgs e) => _ = OpenSongInfoAsync(SelectedTracks());

    private void OnQueueInfoClick(object? sender, RoutedEventArgs e) => _ = OpenSongInfoAsync([.. SelectedQueueItems().Select(i => i.Track)]);

    private void OnHistoryInfoClick(object? sender, RoutedEventArgs e) => _ = OpenSongInfoAsync([.. SelectedPlays().Select(p => p.Track)]);
}
