using Avalonia.Threading;
using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>
/// Files, the library's third source (#318): the computer's places and drives, and any USB stick
/// as soon as it's plugged in. A song from a stick is read into GZJam when it's loaded or queued,
/// so pulling the stick never stops the room; Add to my music copies songs into the DJ's music.
/// </summary>
public sealed class FilesViewModel : IDisposable
{
    private readonly MainWindowViewModel _owner;
    private readonly LibraryDatabase _database;
    private readonly DispatcherTimer _watch;
    private IReadOnlyList<FilePlace> _sticks = [];
    private string _sticksSeen = "";

    public FilesViewModel(MainWindowViewModel owner, LibraryDatabase database)
    {
        _owner = owner;
        _database = database;
        ReadInFolder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(database.DatabasePath))!, "From USB sticks");
        _watch = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => _ = CheckSticksAsync());
    }

    /// <summary>The computer's own places. Tests put their own here.</summary>
    public Func<IReadOnlyList<FilePlace>> Places { get; set; } = FileSources.Places;

    /// <summary>The computer's own drives.</summary>
    public Func<IReadOnlyList<FilePlace>> Drives { get; set; } = FileSources.Drives;

    /// <summary>The USB sticks plugged in now. Tests put their own here.</summary>
    public Func<IReadOnlyList<FilePlace>> Sticks
    {
        get;
        set
        {
            field = value;
            _sticksRead = null;
        }
    } = FileSources.Sticks;

    /// <summary>The sticks the last check read, for the tree to show without reading them again; null to read them.</summary>
    private IReadOnlyList<FilePlace>? _sticksRead;

    /// <summary>Where songs from a stick are read into, by the stick's name and the song's place on it.</summary>
    public string ReadInFolder { get; set; }

    /// <summary>Starts looking for sticks plugged in and pulled out (every couple of seconds; it's quick).</summary>
    public void Start()
    {
        CheckSticks();
        _watch.Start();
    }

    /// <summary>Stops looking for sticks by itself (tests that check by hand).</summary>
    internal void StopWatching() => _watch.Stop();

    /// <summary>A check started by the timer is still running.</summary>
    internal bool IsChecking => _checking;

    /// <summary>Files' places for the tree: "This computer" with its places and drives, then each stick.</summary>
    public IReadOnlyList<FolderNode> TreeNodes()
    {
        _sticks = _sticksRead ?? Sticks();
        _sticksRead = null;
        _sticksSeen = Seen(_sticks);
        var computer = new FilePlace("This computer", "", FilesKind.Computer);
        return [FolderNode.ForFiles(computer, Places().Concat(Drives())), .. _sticks.Select(s => FolderNode.ForFiles(s))];
    }

    private static string Seen(IReadOnlyList<FilePlace> sticks) => string.Join("|", sticks.Select(s => s.Path + "=" + s.Name));

    /// <summary>A check of the sticks is running in the background: the next tick waits for it.</summary>
    private bool _checking;

    /// <summary>
    /// The timer's check (#367): the drives are listed in the background, since a card reader or a
    /// failing stick can be slow to say whether it's ready, and only a change comes back here.
    /// </summary>
    internal async Task CheckSticksAsync()
    {
        if (_checking)
            return;
        _checking = true;
        try
        {
            if (await Task.Run(ReadSticks) is { } now)
                ShowSticks(now);
        }
        finally
        {
            _checking = false;
        }
    }

    private IReadOnlyList<FilePlace>? ReadSticks()
    {
        try
        {
            return Sticks();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>A stick plugged in or pulled out: Files shows it, or not. Now, on this thread (tests, and starting).</summary>
    public void CheckSticks()
    {
        if (ReadSticks() is { } now)
            ShowSticks(now);
    }

    private void ShowSticks(IReadOnlyList<FilePlace> now)
    {
        string seen = Seen(now);
        _sticksRead = seen == _sticksSeen ? null : now;   // the tree, made again, shows these
        if (seen == _sticksSeen)
            return;
        var arrived = now.Where(s => _sticks.All(o => o.Path != s.Path)).ToList();
        Log.Info($"Files: USB sticks now {(now.Count == 0 ? "none" : string.Join(", ", now.Select(s => $"{s.Name} at {s.Path}")))}");
        _owner.Library.RefreshFiles();
        if (arrived.Count > 0)
            _owner.ShowNotice($"{arrived[0].Name} is in the library's Files: its songs play from there, read in first.", seconds: 5);
    }

    /// <summary>The stick <paramref name="path"/> is on, or null for the computer's own.</summary>
    private FilePlace? StickOf(string path) => _sticks.FirstOrDefault(s => FileSources.IsIn(path, s.Path));

    /// <summary>Where a song on a stick is read into: by the stick's name, then its place on the stick.</summary>
    private string ReadInPath(FilePlace stick, string path) =>
        Path.Combine(ReadInFolder, FileSources.SafeName(stick.Name), Path.GetRelativePath(stick.Path, path));

    /// <summary>
    /// A folder's songs for the song list, each with what the DJ gave it and when it arrived on the
    /// drive (#383). A song on a stick that's been read in already is its copy: what the DJ gave it
    /// is there, and it arrived when the stick's file did.
    /// </summary>
    public Task<IReadOnlyList<Track>> ListAsync(string folder, CancellationToken cancel) => Task.Run(() =>
    {
        FilePlace? stick = StickOf(folder);
        var arrived = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        var paths = FileSources.Songs(folder).Select(f =>
        {
            string path = f.FullName;
            if (stick is not null)
            {
                string copy = ReadInPath(stick, f.FullName);
                if (File.Exists(copy) && new FileInfo(copy).Length == f.Length)
                    path = copy;
            }
            arrived[path] = FileSources.ArrivedUtc(f);
            return path;
        }).ToList();
        var songs = _database.TracksForFiles(paths, cancel);
        foreach (Track song in songs)
            song.AddedUtc = arrived.TryGetValue(song.Path, out DateTime at) ? at : null;
        return songs;
    }, cancel);

    /// <summary>Whether a song is on a USB stick, so it's read in before it's loaded or queued.</summary>
    public bool IsOnStick(Track track) => StickOf(track.Path) is not null;

    /// <summary>
    /// Songs from a stick read into GZJam, for a deck or the queue: copied (what the DJ gave them
    /// going with them), so pulling the stick never stops the room. Others are as they were. A
    /// song that can't be read is left out, with a notice.
    /// </summary>
    public async Task<IReadOnlyList<Track>> ReadInAsync(IReadOnlyList<Track> tracks)
    {
        if (!tracks.Any(IsOnStick))
            return tracks;
        int count = tracks.Count(IsOnStick);
        _owner.ShowNotice(count == 1 ? $"Reading {tracks.First(IsOnStick).DisplayTitle} from the USB stick…" : $"Reading {count} songs from the USB stick…", seconds: 30);
        var read = new List<Track>(tracks.Count);
        var moves = new List<(string From, string To)>();
        string? failed = null;
        foreach (Track track in tracks)
        {
            if (StickOf(track.Path) is not { } stick)
            {
                read.Add(track);
                continue;
            }
            string copy = ReadInPath(stick, track.Path);
            try
            {
                await Task.Run(() => CopyIn(track.Path, copy));
                moves.Add((track.Path, copy));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Info($"Files: couldn't read {track.Path} from {stick.Name}: {e.Message}");
                failed ??= $"Couldn't read {track.DisplayTitle} from {stick.Name}: {e.Message}";
            }
        }
        if (moves.Count > 0)
        {
            await Task.Run(() => _database.MoveSongFiles(moves));   // its hearts and cues, set while it was on the stick, go with it
            var copies = await Task.Run(() => _database.TracksForFiles(moves.Select(m => m.To).ToList()).ToDictionary(t => t.Path));
            var byFrom = moves.ToDictionary(m => m.From, m => m.To);
            read = tracks.Where(t => !IsOnStick(t) || (byFrom.TryGetValue(t.Path, out string? to) && copies.ContainsKey(to)))
                .Select(t => byFrom.TryGetValue(t.Path, out string? to) ? copies[to] : t).ToList();
            Log.Info($"Files: read {moves.Count} songs in from the USB stick, into {ReadInFolder}");
        }
        string? title = read.FirstOrDefault(t => moves.Any(m => m.To == t.Path))?.DisplayTitle;
        _owner.ShowNotice(failed ?? (moves.Count == 1 ? $"{title} is read in: safe to pull the USB stick out." : $"{moves.Count} songs read in: safe to pull the USB stick out."),
            seconds: failed is null ? 3 : 8);
        return read;
    }

    /// <summary>A file copied in, whole or not at all; one there already, as big, is kept.</summary>
    private static void CopyIn(string from, string to)
    {
        var source = new FileInfo(from);
        if (File.Exists(to) && new FileInfo(to).Length == source.Length)
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        string partial = to + ".part";
        File.Copy(from, partial, overwrite: true);
        File.SetLastWriteTimeUtc(partial, source.LastWriteTimeUtc);
        File.Move(partial, to, overwrite: true);
    }

    // ───────────── Add to my music ─────────────

    /// <summary>
    /// Add to my music, from Files (#318): the songs copied into the DJ's music folder for added
    /// songs, under Added and the folder they came from ("Added › Gig Nov 14"), the originals left
    /// where they were. What the DJ gave them goes with them; they're the DJ's from then on.
    /// </summary>
    /// <param name="from">The folder they're in, by name, and where they are under it ('/'-separated, "" for in it).</param>
    public async Task AddToMyMusicAsync(IReadOnlyList<(Track Track, string Folder)> songs, string from)
    {
        int already = songs.Count(s => !s.Track.IsOutsideLibrary);
        songs = songs.Where(s => s.Track.IsOutsideLibrary).ToList();   // in the library already: not twice
        if (songs.Count == 0)
        {
            _owner.ShowNotice(already == 1 ? "That song is in your library already." : "Those songs are in your library already.", seconds: 5);
            return;
        }
        var servers = _owner.Servers;
        if (await servers.KeepFolderToUseAsync() is not var (folder, choosing))
            return;
        string usual = Path.Combine(folder, "Added", FileSources.SafeName(from));
        string count = songs.Count == 1 ? "1 song" : $"{Format.Count(songs.Count)} songs";
        string them = songs.Count == 1 ? "it" : "them";
        string what = $"In My music they're yours from then on, never removed by GZJam, with what you gave {them} here.";
        bool onStick = songs.Any(s => IsOnStick(s.Track));
        if (await servers.AskAddPlaceAsync($"Add {count} to My music?", what, usual, canKeepFolders: songs.Any(s => s.Folder.Length > 0),
                $"Copied into {usual}, the originals left where they are: yours from then on, never removed by GZJam, with what you gave {them} here.",
                canMove: true, cantMoveWhy: onStick ? "Songs on a USB stick are copied: GZJam never changes a stick, which can be pulled out at any moment" : null) is not { } place)
            return;
        if (choosing && !servers.SetKeepFolder(folder))
            return;
        await CopyIntoMyMusicAsync(songs, place.Folder, place.KeepFolders, folder, move: place.Move && !onStick);
    }

    /// <summary>
    /// Songs copied into a folder of My music, with what the DJ gave them, the originals left where
    /// they are; or, with <paramref name="move"/>, moved there (#384), out of where they were. Says
    /// how it went, and returns each song and where it went.
    /// </summary>
    /// <param name="rootIfNone">The music folder to add to the library if <paramref name="into"/> isn't in one yet.</param>
    internal async Task<List<(string From, string To)>> CopyIntoMyMusicAsync(IReadOnlyList<(Track Track, string Folder)> songs, string into, bool keepFolders, string rootIfNone,
        bool move = false)
    {
        var servers = _owner.Servers;
        var moves = new List<(string From, string To)>();
        string? failed = null;
        foreach (var (track, under) in songs)
        {
            string inside = keepFolders ? under.Replace('/', Path.DirectorySeparatorChar) : "";
            try
            {
                string wanted = Path.Combine(into, inside, track.FileName);
                string to = move ? await Task.Run(() => MoveIn(track.Path, wanted)) : FreePath(wanted, track.Path);
                if (!move && !File.Exists(to))
                    await Task.Run(() => CopyIn(track.Path, to));
                moves.Add((track.Path, to));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Info($"Files: couldn't {(move ? "move" : "add")} {track.Path} to My music: {e.Message}");
                failed ??= $"Couldn't {(move ? "move" : "copy")} {track.DisplayTitle}: {e.Message}";
            }
        }
        if (moves.Count > 0)
        {
            await Task.Run(() => _database.MoveSongFiles(moves));
            if (servers.RootOf(into) is { } root)
                await _owner.Library.ScanRootAsync(root.Id);
            else
                await _owner.Library.AddRootAsync(rootIfNone);
            Log.Info($"Files: {(move ? "moved" : "added")} {moves.Count} songs to My music, in {into}");
        }
        if (move && moves.Count > 0)
            _owner.Library.FollowMoves(moves);   // on a deck or in the queue: there still, from its new place
        else
            _owner.Library.RefreshResults();
        string done = $"{(move ? "Moved" : "Added")} {(moves.Count == 1 ? "1 song" : $"{Format.Count(moves.Count)} songs")} {(move ? "into" : "to")} My music, in {into}.";
        _owner.ShowNotice(failed is null ? done : $"{done} {failed}", seconds: 8);
        return moves;
    }

    /// <summary>Whether two paths are on one drive, so a move is only a rename. Tests say no.</summary>
    internal Func<string, string, bool> SameDrive { get; set; } = (a, b) =>
        string.Equals(Path.GetPathRoot(a), Path.GetPathRoot(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A song moved into My music (#384), whole in one place or the other however it ends, and
    /// where it went. On one drive it's a rename. Across drives it's copied whole, checked, and only
    /// then the original removed; if that can't be, the copy goes and the original stays. The very
    /// same file there already: the original goes. A different one of that name: a number.
    /// </summary>
    private string MoveIn(string from, string wanted)
    {
        string to = wanted;
        string folder = Path.GetDirectoryName(wanted)!, name = Path.GetFileNameWithoutExtension(wanted), extension = Path.GetExtension(wanted);
        for (int n = 2; File.Exists(to) && !SameContent(from, to); n++)
            to = Path.Combine(folder, $"{name} ({n}){extension}");
        if (File.Exists(to))
        {
            BusyFile.Delete(from);   // in My music already, byte for byte
            return to;
        }
        Directory.CreateDirectory(folder);
        if (SameDrive(from, to))
        {
            BusyFile.Move(from, to);
            return to;
        }
        CopyIn(from, to);
        if (new FileInfo(to).Length != new FileInfo(from).Length)
        {
            File.Delete(to);
            throw new IOException("the copy wasn't whole");
        }
        try
        {
            BusyFile.Delete(from);   // only now the copy is whole
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            File.Delete(to);
            throw;
        }
        return to;
    }

    /// <summary>Whether two files hold the same bytes.</summary>
    private static bool SameContent(string a, string b)
    {
        var (first, second) = (new FileInfo(a), new FileInfo(b));
        if (first.Length != second.Length)
            return false;
        using FileStream x = first.OpenRead(), y = second.OpenRead();
        byte[] bufferX = new byte[81920], bufferY = new byte[81920];
        while (true)
        {
            int read = x.ReadAtLeast(bufferX, bufferX.Length, throwOnEndOfStream: false);
            if (y.ReadAtLeast(bufferY, bufferY.Length, throwOnEndOfStream: false) != read)
                return false;
            if (read == 0)
                return true;
            if (!bufferX.AsSpan(0, read).SequenceEqual(bufferY.AsSpan(0, read)))
                return false;
        }
    }

    /// <summary>A folder in Files, or a stick: every song in it and in its folders, laid out as there.</summary>
    public async Task AddFolderToMyMusicAsync(FolderNodeViewModel folder)
    {
        string path = folder.Node.FullPath;
        var found = await Task.Run(() => FileSources.SongsBelow(path));
        if (found.Count == 0)
        {
            _owner.ShowNotice($"There are no songs in {folder.Name}.", seconds: 5);
            return;
        }
        var tracks = await Task.Run(() => _database.TracksForFiles(found.Select(f => f.File.FullName).ToList()));
        await AddToMyMusicAsync(tracks.Zip(found, (t, f) => (t, f.Folder)).ToList(), FolderName(folder));
    }

    /// <summary>A stick's name without its drive letter ("SANDISK"); a folder's own name.</summary>
    private static string FolderName(FolderNodeViewModel folder) =>
        folder.IsStick && folder.Name.LastIndexOf(" (", StringComparison.Ordinal) is var at and > 0 ? folder.Name[..at] : folder.Name;

    /// <summary>Songs chosen in a Files folder: added under that folder's name.</summary>
    public Task AddTracksToMyMusicAsync(IReadOnlyList<Track> tracks, FolderNodeViewModel? folder) =>
        AddToMyMusicAsync(tracks.Select(t => (t, "")).ToList(),
            folder is not null ? FolderName(folder) : Path.GetFileName(Path.GetDirectoryName(tracks[0].Path)) ?? "Files");

    /// <summary>A path that's free for <paramref name="from"/>'s copy: the same file there already is fine, another gets a number.</summary>
    private static string FreePath(string to, string from)
    {
        long size = new FileInfo(from).Length;
        string folder = Path.GetDirectoryName(to)!, name = Path.GetFileNameWithoutExtension(to), extension = Path.GetExtension(to);
        for (int n = 2; File.Exists(to) && new FileInfo(to).Length != size; n++)
            to = Path.Combine(folder, $"{name} ({n}){extension}");
        return to;
    }

    // ───────────── Delete (#385) ─────────────

    /// <summary>Whether a file there can go to the Recycle Bin. Tests say.</summary>
    internal Func<string, bool> CanRecycle { get; set; } = RecycleBin.CanRecycle;

    /// <summary>Puts a file in the Recycle Bin. Tests keep their own.</summary>
    internal Action<string> Recycle { get; set; } = RecycleBin.Recycle;

    /// <summary>Deletes a file for good, where there's no Recycle Bin.</summary>
    internal Action<string> DeleteForGood { get; set; } = path => BusyFile.Delete(path);

    /// <summary>Why a song can't be deleted from Files, or null if it can.</summary>
    public string? CantDelete(Track track) =>
        !track.IsOutsideLibrary ? "it's in My music"
        : IsOnStick(track) ? "it's on a USB stick, which GZJam never changes"
        : _owner.Decks.Any(d => d.Track?.Path == track.Path) ? "it's on a deck"
        : _owner.Queue.Items.Any(i => i.Track.Path == track.Path) ? "it's in the queue"
        : _database.GetPlaylistsWith(track.Path) is [var playlist, ..] ? $"it's in the playlist {playlist}"
        : null;

    /// <summary>Whether any of these can be deleted: Delete… is offered for them.</summary>
    public bool CanDeleteAny(IReadOnlyList<Track> tracks) => tracks.Any(t => CantDelete(t) is null);

    /// <summary>
    /// Songs in Files deleted from the drive (#385), after one question: to the Recycle Bin, or for
    /// good where there's none (a network drive), the question saying so. Songs in use (on a deck,
    /// in the queue, in a playlist), in My music or on a USB stick are left, and the question says
    /// how many and why. What the DJ gave a deleted song goes with it.
    /// </summary>
    public async Task DeleteAsync(IReadOnlyList<Track> tracks)
    {
        var why = tracks.Select(t => (Track: t, Why: CantDelete(t))).ToList();
        var songs = why.Where(x => x.Why is null).Select(x => x.Track).DistinctBy(t => t.Path).ToList();
        var kept = why.Where(x => x.Why is not null).ToList();
        if (songs.Count == 0)
        {
            _owner.ShowNotice(kept is [var one] ? $"{one.Track.DisplayTitle} isn't deleted: {one.Why}." : "None of those can be deleted: they're in use, in My music or on a USB stick.", seconds: 6);
            return;
        }
        var forGood = await Task.Run(() => songs.Where(t => !CanRecycle(t.Path)).Select(t => t.Path).ToHashSet());
        string bin = RecycleBin.Name;
        string count = songs.Count == 1 ? $"\"{songs[0].DisplayTitle}\"" : $"{Format.Count(songs.Count)} songs";
        string heading = forGood.Count == 0 ? $"Move {count} to the {bin}?" : $"Delete {count} for good?";
        var lines = new List<string>();
        if (songs.Count > 1)
            lines.Add(string.Join(", ", songs.Take(3).Select(t => t.DisplayTitle)) + (songs.Count > 3 ? $", and {Format.Count(songs.Count - 3)} more." : "."));
        lines.Add(forGood.Count == 0
            ? $"They go to the computer's {bin}, so they can be put back from there."
            : forGood.Count == songs.Count
                ? $"Their drive has no {bin}: they're deleted for good, and can't be put back."
                : $"{Format.Count(forGood.Count)} of them are on a drive with no {bin}: those are deleted for good.");
        if (kept.Count > 0)
            lines.Add(kept is [var only] ? $"{only.Track.DisplayTitle} stays: {only.Why}." : $"{Format.Count(kept.Count)} others stay: they're in use, in My music or on a USB stick.");
        if (_owner.ConfirmAsync is not { } confirm || !await confirm(heading, string.Join("\n\n", lines), forGood.Count == 0 ? "Delete" : "Delete for good"))
            return;

        if (_owner.Preview is { Song: { } previewing } preview && songs.Any(t => t.Path == previewing.Path))
            preview.Stop();   // the song it was playing is going
        var deleted = new List<string>();
        string? failed = null;
        foreach (Track song in songs)
        {
            try
            {
                bool good = forGood.Contains(song.Path);
                await Task.Run(() =>
                {
                    if (good)
                        DeleteForGood(song.Path);
                    else
                        Recycle(song.Path);
                });
                deleted.Add(song.Path);
                Log.Info($"Files: deleted {song.Path}{(good ? " for good" : $", to the {bin}")}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Info($"Files: couldn't delete {song.Path}: {e.Message}");
                failed ??= $"Couldn't delete {song.DisplayTitle}: {e.Message}";
            }
        }
        if (deleted.Count > 0)
            await Task.Run(() => _database.ForgetSongFiles(deleted));
        _owner.Library.RefreshResults();
        string done = deleted.Count == 0 ? ""
            : forGood.Count == 0 ? $"{(deleted.Count == 1 ? "1 song" : $"{Format.Count(deleted.Count)} songs")} moved to the {bin}."
            : $"{(deleted.Count == 1 ? "1 song" : $"{Format.Count(deleted.Count)} songs")} deleted.";
        _owner.ShowNotice(failed is null ? done : $"{done} {failed}".Trim(), seconds: failed is null ? 4 : 8);
    }

    public void Dispose() => _watch.Stop();
}
