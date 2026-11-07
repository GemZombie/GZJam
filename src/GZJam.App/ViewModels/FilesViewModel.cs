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
        _watch = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => CheckSticks());
    }

    /// <summary>The computer's own places. Tests put their own here.</summary>
    public Func<IReadOnlyList<FilePlace>> Places { get; set; } = FileSources.Places;

    /// <summary>The computer's own drives.</summary>
    public Func<IReadOnlyList<FilePlace>> Drives { get; set; } = FileSources.Drives;

    /// <summary>The USB sticks plugged in now.</summary>
    public Func<IReadOnlyList<FilePlace>> Sticks { get; set; } = FileSources.Sticks;

    /// <summary>Where songs from a stick are read into, by the stick's name and the song's place on it.</summary>
    public string ReadInFolder { get; set; }

    /// <summary>Starts looking for sticks plugged in and pulled out (every couple of seconds; it's quick).</summary>
    public void Start()
    {
        CheckSticks();
        _watch.Start();
    }

    /// <summary>Files' places for the tree: "This computer" with its places and drives, then each stick.</summary>
    public IReadOnlyList<FolderNode> TreeNodes()
    {
        _sticks = Sticks();
        _sticksSeen = Seen(_sticks);
        var computer = new FilePlace("This computer", "", FilesKind.Computer);
        return [FolderNode.ForFiles(computer, Places().Concat(Drives())), .. _sticks.Select(s => FolderNode.ForFiles(s))];
    }

    private static string Seen(IReadOnlyList<FilePlace> sticks) => string.Join("|", sticks.Select(s => s.Path + "=" + s.Name));

    /// <summary>A stick plugged in or pulled out: Files shows it, or not.</summary>
    public void CheckSticks()
    {
        IReadOnlyList<FilePlace> now;
        try
        {
            now = Sticks();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;
        }
        string seen = Seen(now);
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
    /// A folder's songs for the song list, each with what the DJ gave it. A song on a stick that's
    /// been read in already is its copy: what the DJ gave it is there.
    /// </summary>
    public Task<IReadOnlyList<Track>> ListAsync(string folder, CancellationToken cancel) => Task.Run(() =>
    {
        FilePlace? stick = StickOf(folder);
        var paths = FileSources.Songs(folder).Select(f =>
        {
            if (stick is null)
                return f.FullName;
            string copy = ReadInPath(stick, f.FullName);
            return File.Exists(copy) && new FileInfo(copy).Length == f.Length ? copy : f.FullName;
        }).ToList();
        return _database.TracksForFiles(paths, cancel);
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
        string into = Path.Combine(folder, "Added", FileSources.SafeName(from));
        string count = songs.Count == 1 ? "1 song" : $"{Format.Count(songs.Count)} songs";
        if (_owner.ConfirmAsync is null || !await _owner.ConfirmAsync($"Add {count} to My music?",
                $"Copied into {into}, the originals left where they are: yours from then on, never removed by GZJam, with what you gave {(songs.Count == 1 ? "it" : "them")} here.",
                "Add"))
            return;
        if (choosing && !servers.SetKeepFolder(folder))
            return;

        var moves = new List<(string From, string To)>();
        string? failed = null;
        foreach (var (track, under) in songs)
        {
            string to = FreePath(Path.Combine(into, under.Replace('/', Path.DirectorySeparatorChar), track.FileName), track.Path);
            try
            {
                if (!File.Exists(to))
                    await Task.Run(() => CopyIn(track.Path, to));
                moves.Add((track.Path, to));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Info($"Files: couldn't add {track.Path} to My music: {e.Message}");
                failed ??= $"Couldn't copy {track.DisplayTitle}: {e.Message}";
            }
        }
        if (moves.Count > 0)
        {
            await Task.Run(() => _database.MoveSongFiles(moves));
            if (servers.RootOf(folder) is null)
                await _owner.Library.AddRootAsync(folder);
            else if (servers.RootOf(folder) is { } root)
                await _owner.Library.ScanRootAsync(root.Id);
            Log.Info($"Files: added {moves.Count} songs to My music, in {into}");
        }
        _owner.Library.RefreshResults();
        string done = $"Added {(moves.Count == 1 ? "1 song" : $"{Format.Count(moves.Count)} songs")} to My music, in {into}.";
        _owner.ShowNotice(failed is null ? done : $"{done} {failed}", seconds: 8);
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

    public void Dispose() => _watch.Stop();
}
