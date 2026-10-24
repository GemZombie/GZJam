using Avalonia;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Views;

/// <summary>
/// Songs dragged in from the file manager (Explorer, Finder, a Linux file manager): one onto a
/// deck, or any number (and whole folders) into the queue, lit up as they come over like songs
/// from the library. A song outside the music folders, say one a dancer brings on a USB stick,
/// plays without joining the library.
/// </summary>
public partial class MainWindow
{
    private void InitializeFileDrop()
    {
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnFileDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => HideDropTarget());
        AddHandler(DragDrop.DropEvent, OnFileDrop);
    }

    /// <summary>The files and folders being dragged in, as paths on this computer.</summary>
    private static IReadOnlyList<string> DraggedPaths(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles()?.Select(item => item.TryGetLocalPath()).OfType<string>().ToList() ?? [];

    /// <summary>How many songs the paths stand for, as far as a deck cares: a single file is one.</summary>
    private static int SongsIn(IReadOnlyList<string> paths) => paths is [var one] && !Directory.Exists(one) ? 1 : 2;

    private void OnFileDragOver(object? sender, DragEventArgs e)
    {
        var paths = DraggedPaths(e);
        if (paths.Count == 0)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        object? target = ShowDropTarget(e.GetPosition(this), SongsIn(paths), out bool blocked);
        e.DragEffects = target is not null && !blocked ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnFileDrop(object? sender, DragEventArgs e)
    {
        var paths = DraggedPaths(e);
        Point at = e.GetPosition(this);
        object? target = DropTargetAt(at);
        int index = target == QueueArea && this.TranslatePoint(at, QueueArea) is { } queuePoint ? DropIndex(queuePoint, out _) : 0;
        HideDropTarget();
        if (paths.Count == 0)
            return;
        e.Handled = true;

        MainWindowViewModel vm = Vm;   // read here: the tags are read off the UI thread
        var (tracks, skipped) = await Task.Run(() => vm.TracksFromFiles(paths));
        string what = tracks.Count == 1 ? $"\"{tracks[0].DisplayTitle}\"" : $"{tracks.Count} songs";
        string where = target switch
        {
            DeckView { DataContext: DeckViewModel d } => $"Deck {d.Label}",
            _ when target == QueueArea => "the queue",
            _ => "nowhere (dropped outside the decks and queue)",
        };
        Log.Info($"Dropped {what} from the file manager on {where}" + (skipped.Count > 0 ? $"; {skipped.Count} not playable" : ""));

        if (tracks.Count == 0)
        {
            Vm.ShowNotice(skipped is [var file]
                ? $"{Path.GetFileName(file)} isn't a song GZJam can play."
                : "None of those are songs GZJam can play.");
            return;
        }
        switch (target)
        {
            case DeckView { DataContext: DeckViewModel deck } when tracks is [var track]:
                await Vm.LoadToDeckAsync(deck, track);
                break;
            case DeckView:
                Vm.ShowNotice("A deck takes one song. Drop several on the queue.");
                return;
            case not null when target == QueueArea:
                Vm.Queue.Add(tracks, index);
                break;
            default:
                Vm.ShowNotice("Drop songs on a deck, or on the queue.");
                return;
        }
        if (skipped.Count > 0)
            Vm.ShowNotice(skipped is [var file]
                ? $"{Path.GetFileName(file)} was left out: it isn't a song GZJam can play."
                : $"{skipped.Count} files were left out: they aren't songs GZJam can play.");
    }
}
