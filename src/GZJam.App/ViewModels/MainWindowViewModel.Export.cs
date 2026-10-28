using System.Globalization;
using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>
/// Export as files (#101): a playlist's songs, the queue's or a night's from History, copied in
/// order into a new folder with a playlist beside them, for a spare copy of the set on a USB stick
/// or to hand it to another DJ. Said first (how many, how big, the room there), then copied in the
/// background, shown in the top bar.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private SongExport? _export;
    private int _exported;

    /// <summary>"Copying 3 of 24 songs…", for the top bar.</summary>
    private string? ExportStatus => _export is { } export ? $"Copying {_exported} of {Format.Count(export.Files.Count)} songs…" : null;

    private double ExportPercent => _export is { Files.Count: > 0 } export ? 100.0 * _exported / export.Files.Count : 0;

    /// <summary>A playlist's songs, in its order.</summary>
    public IReadOnlyList<Track> PlaylistSongs(PlaylistInfo playlist) => _database.Search(null, playlist: playlist.Id).ToList();

    /// <summary>The queue's songs, and a name for them: "Queue, Oct 28".</summary>
    public (string Name, IReadOnlyList<Track> Songs) QueueForExport() =>
        ($"Queue, {DateTime.Now.ToString("MMM d", CultureInfo.CurrentCulture)}", Queue.Items.Select(i => i.Track).ToList());

    /// <summary>The night shown in History, in the order it was played, by its name.</summary>
    public (string Name, IReadOnlyList<Track> Songs) SessionForExport() =>
        (History.SelectedName ?? "GZJam night", History.Plays.Select(p => p.Track).ToList());

    /// <summary>Copies <paramref name="songs"/> into a new folder named <paramref name="name"/> in <paramref name="into"/>, once the DJ says so.</summary>
    public async Task ExportAsFilesAsync(string name, IReadOnlyList<Track> songs, string into)
    {
        if (_export is not null)
        {
            ShowNotice("Still copying the last set: one at a time.");
            return;
        }
        SongExport export;
        try
        {
            export = SongExport.Plan(name, songs, into);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Error($"Couldn't plan copying \"{name}\" to {into}", ex);
            ShowNotice($"Couldn't copy there: {ex.Message}", 8);
            return;
        }
        if (export.Files.Count == 0)
        {
            ShowNotice(songs.Count == 0 ? "No songs to copy." : "None of these songs' files are there to copy.");
            return;
        }
        string size = Format.Size(export.Bytes);
        if (!export.HasRoom)
        {
            ShowNotice($"Not enough room there: {size} to copy, {Format.Size(export.FreeBytes ?? 0)} free.", 8);
            return;
        }

        string songsText = export.Files.Count == 1 ? "1 song" : $"{Format.Count(export.Files.Count)} songs";
        string message = $"{songsText} ({size}) go into a new folder, \"{Path.GetFileName(export.Folder)}\", in {into}, "
            + "numbered in order, with a playlist beside them. Your own files are copied, never moved or changed.";
        if (export.FreeBytes is { } free)
            message += $" {Format.Size(free)} free there.";
        if (export.Missing.Count > 0)
            message += export.Missing.Count == 1
                ? $"\n\nLeft out, its file isn't there: {export.Missing[0].DisplayTitle}."
                : $"\n\nLeft out, their files aren't there: {Format.Count(export.Missing.Count)} songs.";
        if (ConfirmAsync is not null && !await ConfirmAsync($"Copy \"{name}\" as files?", message, "Copy"))
            return;

        _export = export;
        _exported = 0;
        BackgroundChanged();
        Log.Info($"Copying \"{name}\" as files: {songsText}, {size}, to {export.Folder}");
        try
        {
            await export.CopyAsync(new Progress<int>(n =>
            {
                _exported = n;
                BackgroundChanged();
            }));
            Log.Info($"Copied \"{name}\" to {export.Folder}");
            ShowNotice($"Copied {songsText} to {export.Folder}", 10);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Copying \"{name}\" to {export.Folder} stopped", ex);
            ShowNotice($"Copying stopped after {Format.Count(_exported)} songs: {ex.Message}", 10);
        }
        finally
        {
            _export = null;
            BackgroundChanged();
        }
    }
}
