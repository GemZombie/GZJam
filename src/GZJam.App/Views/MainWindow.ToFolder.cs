using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GZJam.App.Controls;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Views;

/// <summary>
/// Songs dragged from the queue to a folder of My music (#335): only the queue's, and the drop only
/// asks. The song list's drags are as they always were: deck, queue or playlist.
/// </summary>
public partial class MainWindow
{
    private Control? _folderDropRow;

    /// <summary>What the drag's tag says over a folder now, for tests.</summary>
    internal string? DragHint => _dragHint is { IsVisible: true } hint ? hint.Text : null;

    /// <summary>
    /// The place in My music under <paramref name="at"/> (window coordinates), if any: a folder of
    /// the tree, My music's own name (Path null), which is there whichever source is showing, or
    /// the song list, which means wherever in My music it's showing.
    /// </summary>
    private (Control Row, string? Path, string Name)? MusicFolderAt(Point at)
    {
        if (IsOver(FolderTree, at)
            && FolderTree.GetVisualDescendants().OfType<FolderRow>().FirstOrDefault(r => IsOver(r, at)) is { DataContext: FolderNodeViewModel { IsFolder: true } node } row
            && !FileSources.IsIn(node.Node.FullPath, Vm.Servers.DownloadsFolder))
            return (row, node.Node.FullPath, node.Name);
        if (IsOver(SourceList, at)
            && SourceList.GetVisualDescendants().OfType<ListBoxItem>().FirstOrDefault(i => IsOver(i, at)) is { DataContext: SourceViewModel { IsMyMusic: true } source } item)
            return (item, null, source.Name);
        if (IsOver(SongPanel, at) && Vm.Library.ShownMusicFolder(Vm.Servers.DownloadsFolder) is { } shown)
            return (SongPanel, shown.Path, shown.Name);
        return null;
    }

    /// <summary>Lights the folder a queue drag is over, and has its tag say what will happen. True if the songs can't go there.</summary>
    private bool UpdateFolderDrop(Point at, TrackDrag drag)
    {
        var over = drag.FromQueue is null ? null : MusicFolderAt(at);
        string? text = over is { } place ? Vm.ToFolderDragText(drag.Tracks, place.Name) : null;
        bool refused = over is not null && text is null;
        LightFolderRow(text is null ? null : over!.Value.Row);
        if (_dragHint is { } hint)
        {
            hint.Text = refused ? "On a deck: it stays where it is" : text;
            hint.IsVisible = hint.Text is not null;
        }
        return refused;
    }

    private void LightFolderRow(Control? row)
    {
        if (row == _folderDropRow)
            return;
        _folderDropRow?.Classes.Set("drop-target", false);
        _folderDropRow = row;
        row?.Classes.Set("drop-target", true);
    }

    private void OnQueueToFolderClick(object? sender, RoutedEventArgs e) =>
        _ = Vm.SendToFolderAsync(SelectedQueueItems().Select(i => i.Track).ToList(), null);
}
