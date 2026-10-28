using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GZJam.Library;

namespace GZJam.App.Views;

/// <summary>
/// Export as files (#101), from a playlist's menu, the queue's, or a History night's ⋯: the DJ
/// picks where (a USB stick, say), and the songs are copied there into a new folder.
/// </summary>
public partial class MainWindow
{
    private async void OnExportPlaylistClick(object? sender, RoutedEventArgs e)
    {
        if (PlaylistOf(sender) is { } playlist)
            await ExportAsync(playlist.Name, Vm.PlaylistSongs(playlist));
    }

    private async void OnExportQueueClick(object? sender, RoutedEventArgs e)
    {
        var (name, songs) = Vm.QueueForExport();
        await ExportAsync(name, songs);
    }

    private async void OnExportSessionClick(object? sender, RoutedEventArgs e)
    {
        var (name, songs) = Vm.SessionForExport();
        await ExportAsync(name, songs);
    }

    private async Task ExportAsync(string name, IReadOnlyList<Track> songs)
    {
        if (songs.Count == 0)
        {
            Vm.ShowNotice("No songs to copy.");
            return;
        }
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = $"Where to copy \"{name}\" (a new folder is made there)",
            AllowMultiple = false,
        });
        if (folders is not [var folder] || folder.TryGetLocalPath() is not { } path)
            return;
        await Vm.ExportAsFilesAsync(name, songs, path);
    }
}
