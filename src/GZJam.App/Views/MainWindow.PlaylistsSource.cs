using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GZJam.App.ViewModels;

namespace GZJam.App.Views;

/// <summary>
/// Playlists as a source (#423), with the whole column. Songs (or files) dragged from a folder's
/// songs and held over the Playlists source open it after a moment, as a file manager opens a
/// folder held over, so they can be dropped onto a playlist. The song list stays as it was, under
/// the drag.
/// </summary>
public partial class MainWindow
{
    /// <summary>How long a drag rests on the Playlists source before it opens.</summary>
    internal static readonly TimeSpan SpringOpen = TimeSpan.FromMilliseconds(600);

    private CancellationTokenSource? _springTimer;

    /// <summary>The Playlists source's row in the sources list.</summary>
    private ListBoxItem? PlaylistsSourceRow() =>
        SourceList.GetVisualDescendants().OfType<ListBoxItem>()
            .FirstOrDefault(i => i.DataContext is SourceViewModel { Kind: LibrarySource.Playlists });

    /// <summary>A drag moved: over the Playlists source (not open yet) it opens after a moment; anywhere else, it doesn't.</summary>
    private void UpdateSpringOpen(Point at)
    {
        ListBoxItem? row = Vm.Library.IsPlaylistsSource ? null : PlaylistsSourceRow();
        bool over = row is not null && IsOver(row, at);
        row?.Classes.Set("drop-target", over);
        if (!over)
        {
            StopSpringOpen();
            return;
        }
        if (_springTimer is not null)
            return;   // already waiting
        var wait = _springTimer = new CancellationTokenSource();
        // As the drag's own hold is timed (DragStarter): a delay, then back on the window's thread.
        Task.Delay(SpringOpen, wait.Token).ContinueWith(_ => Dispatcher.UIThread.Post(() =>
        {
            if (wait.IsCancellationRequested || _springTimer != wait)
                return;
            _springTimer = null;
            PlaylistsSourceRow()?.Classes.Set("drop-target", false);
            Vm.Library.ShowPlaylistsColumn();
        }), TaskScheduler.Default);
    }

    private void StopSpringOpen()
    {
        _springTimer?.Cancel();
        _springTimer = null;
        PlaylistsSourceRow()?.Classes.Set("drop-target", false);
    }
}
