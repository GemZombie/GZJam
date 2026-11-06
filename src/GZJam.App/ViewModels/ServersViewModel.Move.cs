using GZJam.Library;
using GZJam.Library.Servers;

namespace GZJam.App.ViewModels;

/// <summary>
/// The downloads folder moved (#290): to a bigger drive, say, after songs came. The songs GZJam
/// downloaded go with it, file by file, and everything kept by their paths (hearts, cues, EQ,
/// playlists, history, which server song each is) follows them, as do the queue and the decks.
/// Each song is moved and recorded before the next, so stopping halfway (the laptop closed)
/// leaves each song working where it is. The old folder leaves the library once it's empty.
/// </summary>
public sealed partial class ServersViewModel
{
    private bool _moving;

    /// <summary>While the songs move: the top bar says so, and syncs wait.</summary>
    public bool IsMovingDownloads => _moving;

    private static StringComparison PathComparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>Whether <paramref name="path"/> is <paramref name="folder"/> or inside it.</summary>
    private static bool IsUnder(string path, string folder)
    {
        string top = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string inside = Path.EndsInDirectorySeparator(top) ? top : top + Path.DirectorySeparatorChar;   // "C:\" and "/" end in one
        return full.Equals(top, PathComparison) || full.StartsWith(inside, PathComparison);
    }

    /// <summary>The drive a folder is on: on Linux and Macs, the mount it's under (an SD card's own, say).</summary>
    private static DriveInfo? DriveOf(string folder)
    {
        try
        {
            return DriveInfo.GetDrives()
                .Where(d => d.IsReady && IsUnder(folder, d.RootDirectory.FullName))
                .MaxBy(d => d.RootDirectory.FullName.Length);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The songs GZJam downloaded that are in <paramref name="folder"/>, from every server.</summary>
    private List<ServerDownload> DownloadsUnder(string folder) =>
        Items.SelectMany(i => _database.GetServerDownloads(i.Server.Id))
            .Where(d => IsUnder(d.Path, folder) && File.Exists(d.Path))
            .DistinctBy(d => d.Path)
            .ToList();

    /// <summary>
    /// CHANGE… in Settings › Servers: downloads go to <paramref name="folder"/> from now on, and the
    /// songs already downloaded move there, once the DJ says so.
    /// </summary>
    public async Task ChangeDownloadsFolderAsync(string folder)
    {
        if (!CanChangeDownloadsFolder)
            return;
        string to = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        string from = Path.TrimEndingDirectorySeparator(Path.GetFullPath(DownloadsFolder));
        if (to.Equals(from, PathComparison))
            return;
        var downloads = DownloadsUnder(from);
        if (downloads.Count == 0)
        {
            SetDownloadsFolder(to);
            return;
        }
        if (IsUnder(to, from))
        {
            _owner.ShowNotice($"Choose a folder outside {from}: the downloaded songs are in it.", seconds: 8);
            return;
        }
        var paths = downloads.Select(d => d.Path).ToHashSet(StringComparer.Ordinal);
        if (_owner.Decks.FirstOrDefault(d => d.Track is { } t && paths.Contains(t.Path)) is { } deck)
        {
            _owner.ShowNotice($"Take {deck.Track!.DisplayTitle} off {deck.Label} first: its file can't move while it's on a deck.", seconds: 8);
            return;
        }

        long bytes = downloads.Sum(d => d.Size);
        DriveInfo? toDrive = DriveOf(to);
        bool sameDrive = toDrive is not null && toDrive.Name == DriveOf(from)?.Name;
        if (!sameDrive && toDrive?.AvailableFreeSpace is { } free && free < bytes + 100_000_000)
        {
            _owner.ShowNotice($"There isn't room on that drive: the songs need {Format.Size(bytes)}, and it has {Format.Size(free)} free.", seconds: 8);
            return;
        }
        if (_owner.ConfirmAsync is null || !await _owner.ConfirmAsync(
                $"Move {Describe(downloads)} to {to}?",
                "Their hearts, cues, EQ, playlists and history go with them, and songs downloaded from now on go there too. "
                + (sameDrive ? "It's quick: they stay on the same drive." : "It takes a while: they're copied to the other drive, then removed from this one.")
                + $" {from} leaves the library once it's empty.",
                "Move"))
            return;

        _moving = true;
        RefreshDownloads();
        Log.Info($"Servers: moving {downloads.Count} downloaded songs ({Format.Size(bytes)}) from {from} to {to}");
        if (_owner.Preview?.Track is { } heard && paths.Contains(heard.Path))
            _owner.Preview.Stop();
        await _oneSync.WaitAsync();   // no sync meanwhile
        try
        {
            Directory.CreateDirectory(to);
            _settings.ServerDownloadsFolder = to;   // what comes on the spot meanwhile goes there already
            OnPropertyChanged(nameof(DownloadsFolder));
            await EnsureDownloadsInLibraryAsync();

            // Songs already coming down are headed for the old folder: they finish first, and move too.
            if (DownloadsUnderWay() is { Count: > 0 } coming)
            {
                SyncStatus = coming.Count == 1 ? "Waiting for a song still downloading…" : $"Waiting for {coming.Count} songs still downloading…";
                Log.Info($"Servers: the move waits for {coming.Count} downloads under way");
                try
                {
                    await Task.WhenAll(coming);
                }
                catch (Exception)
                {
                    // One that failed or stopped isn't there to move: the rest are.
                }
                downloads = DownloadsUnder(from);
                paths = downloads.Select(d => d.Path).ToHashSet(StringComparer.Ordinal);
            }

            SyncStatus = "Moving the downloaded songs…";
            SyncPercent = 0;
            var progress = new Progress<int>(done =>
            {
                SyncStatus = $"Moving the downloaded songs… {Format.Count(done)} / {Format.Count(downloads.Count)}";
                SyncPercent = 100.0 * done / downloads.Count;
            });
            var (moves, failed) = await _owner.Library.WithoutScansAsync(() => Task.Run(() => MoveFiles(downloads, from, to, progress)));
            Log.Info($"Servers: moved {moves.Count} downloaded songs to {to}" + (failed > 0 ? $"; {failed} couldn't be moved" : ""));

            await AfterMoveAsync(from, to);
            _owner.Library.FollowMoves(moves);
            long moved = downloads.Where(d => moves.Any(m => m.From == d.Path)).Sum(d => d.Size);
            _owner.ShowNotice(failed == 0
                ? $"Moved {(moves.Count == 1 ? "1 song" : Format.Count(moves.Count) + " songs")}, {Format.Size(moved)}, to {to}."
                : $"Moved {Format.Count(moves.Count)} songs to {to}; {Format.Count(failed)} couldn't be (in use?) and play from where they are: the activity log says which.",
                seconds: 8);
        }
        finally
        {
            SyncStatus = null;
            _moving = false;
            _oneSync.Release();
            foreach (ServerItemViewModel item in Items)
                RefreshKept(item);
            RefreshDownloads();
        }
    }

    /// <summary>Moves each file, then records it, before the next. Returns the moves made and how many couldn't be.</summary>
    private (List<(string From, string To)> Moves, int Failed) MoveFiles(
        IReadOnlyList<ServerDownload> downloads, string from, string to, IProgress<int> progress)
    {
        var moves = new List<(string From, string To)>();
        int failed = 0;
        for (int i = 0; i < downloads.Count; i++)
        {
            string path = downloads[i].Path;
            try
            {
                string target = Untaken(LibraryDatabase.MovedPath(path, from, to)!);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                DateTime modified = File.GetLastWriteTimeUtc(path);
                File.Move(path, target);
                File.SetLastWriteTimeUtc(target, modified);   // a copy to another drive is a new file
                _database.MoveSongFiles([(path, target)]);
                moves.Add((path, target));
                MoveArtAlong(Path.GetDirectoryName(path)!, Path.GetDirectoryName(target)!);
                RemoveEmptyFolders(Path.GetDirectoryName(path), from);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                failed++;
                Log.Info($"Servers: couldn't move {path}: {e.Message}");
            }
            progress.Report(i + 1);
        }
        try
        {
            if (Directory.Exists(from) && !Directory.EnumerateFileSystemEntries(from).Any())
                Directory.Delete(from);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Info($"Servers: couldn't remove the empty folder {from}: {e.Message}");
        }
        return (moves, failed);
    }

    /// <summary>The folder and those in it, where they hold nothing (or only an album's picture), from the bottom up.</summary>
    private static void RemoveEmptyTree(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
                return;
            foreach (string inside in Directory.EnumerateDirectories(folder).ToList())
                RemoveEmptyTree(inside);
            if (OnlyArtLeft(folder) is { } art)
            {
                foreach (string picture in art)
                    File.Delete(picture);
                Directory.Delete(folder);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Info($"Servers: couldn't remove the empty folder {folder}: {e.Message}");
        }
    }

    /// <summary>The album's picture follows its last song out of a folder, unless the new folder has one.</summary>
    private static void MoveArtAlong(string folder, string to)
    {
        if (!Directory.Exists(folder) || OnlyArtLeft(folder) is not { Count: > 0 } art)
            return;
        foreach (string picture in art)
        {
            string target = Path.Combine(to, Path.GetFileName(picture));
            try
            {
                if (File.Exists(target))
                    File.Delete(picture);
                else
                    File.Move(picture, target);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Info($"Servers: couldn't move the album picture {picture}: {e.Message}");
            }
        }
    }

    /// <summary>The path, or "… (2).mp3" and on if a file (or a server song) is there already.</summary>
    private string Untaken(string path)
    {
        bool Taken(string p) => File.Exists(p) || _database.IsServerSongPath(p);
        if (!Taken(path))
            return path;
        string folder = Path.GetDirectoryName(path)!, stem = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
        for (int n = 2; ; n++)
        {
            string numbered = Path.Combine(folder, $"{stem} ({n}){ext}");
            if (!Taken(numbered))
                return numbered;
        }
    }

    /// <summary>
    /// The library after a move: the new folder read (the songs at their new paths), and the old
    /// one out of it if it was a music folder of its own and nothing's left, or read again if not.
    /// </summary>
    private async Task AfterMoveAsync(string from, string to)
    {
        var library = _owner.Library;
        LibraryRoot? newRoot = RootOf(to), oldRoot = RootOf(from);
        if (oldRoot is not null && oldRoot.Id != newRoot?.Id)
        {
            bool itsOwn = Path.TrimEndingDirectorySeparator(oldRoot.Path).Equals(from, PathComparison);
            bool empty = !Directory.Exists(from) || !Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories).Any(LibraryScanner.IsAudioFile);
            if (itsOwn && empty)
            {
                Log.Info($"Servers: {from} is empty now, out of the library");
                library.RemoveRoot(oldRoot.Id);
                // No longer watched: on Windows a watched folder deleted stays until it isn't.
                await Task.Run(() => RemoveEmptyTree(from));
            }
            else
                await library.ScanRootAsync(oldRoot.Id);
        }
        if (newRoot is not null)
            await library.ScanRootAsync(newRoot.Id);
    }
}
