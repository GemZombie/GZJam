namespace GZJam.Library;

/// <summary>
/// Bringing M3U playlists into GZJam (see <see cref="PlaylistFile"/>), planned first so the DJ
/// sees what will come over, then applied. Each file becomes a playlist named after it, with the
/// songs GZJam has, in order. Songs are matched by path, or where the playlist was made on another
/// computer, by the end of their path (<see cref="MovedSongs"/>); ones outside GZJam's music
/// folders, or gone, are counted and left out. A name GZJam has already is skipped, so importing the same
/// files again adds nothing twice. The playlist files are only read.
/// A folder of playlist files (<see cref="PlanFolder"/>) keeps its subfolders as folders of
/// playlists (#354), joining any GZJam has of those names; a name is skipped only if it's in that
/// folder already.
/// </summary>
public sealed class PlaylistImport
{
    private readonly List<(string Name, IReadOnlyList<string> Folders, List<string> Paths)> _playlists = [];

    private PlaylistImport()
    {
    }

    /// <summary>Playlists that would come over ("WCS › Nov 8" for one in folders), with how many of their songs GZJam has.</summary>
    public IReadOnlyList<(string Name, int Songs)> Playlists => _playlists.Select(p => (Shown(p.Folders, p.Name), p.Paths.Count)).ToList();

    /// <summary>A playlist's name with the folders it's in: "WCS › Nov 8".</summary>
    private static string Shown(IReadOnlyList<string> folders, string name) => string.Join(" › ", [.. folders, name]);

    /// <summary>The same, as a key that ignores case.</summary>
    private static string Key(IReadOnlyList<string> folders, string name) => Shown(folders, name).ToUpperInvariant();

    /// <summary>
    /// Every playlist file in <paramref name="folder"/> and the folders in it (#354), each going in
    /// the folders of playlists named as the folders it's in, below the one picked.
    /// </summary>
    public static PlaylistImport PlanFolder(LibraryDatabase gz, string folder)
    {
        string root = Path.GetFullPath(folder);
        // M3U only: a folder's other text files (a readme, notes) aren't playlists.
        var files = new[] { "*.m3u", "*.m3u8" }
            .SelectMany(pattern => Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Plan(gz, files, root);
    }

    /// <summary>The folders a file is in below <paramref name="root"/>: none for one in it directly.</summary>
    private static IReadOnlyList<string> FoldersBelow(string? root, string file)
    {
        if (root is null)
            return [];
        string? dir = Path.GetDirectoryName(Path.GetFullPath(file));
        if (dir is null)
            return [];
        string relative = Path.GetRelativePath(root, dir);
        return relative is "." || relative.StartsWith("..", StringComparison.Ordinal)
            ? []
            : relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>Each playlist GZJam has, as its folders and name (see <see cref="Key"/>).</summary>
    private static HashSet<string> Existing(LibraryDatabase gz)
    {
        var folders = gz.GetPlaylistFolders().ToDictionary(f => f.Id);
        List<string> PathOf(long? id)
        {
            var path = new List<string>();
            for (long? at = id; at is { } here && folders.TryGetValue(here, out var f) && path.Count <= folders.Count; at = f.ParentId)
                path.Insert(0, f.Name);
            return path;
        }
        return gz.GetPlaylists().Select(p => Key(PathOf(p.FolderId), p.Name)).ToHashSet();
    }

    public int SongCount => _playlists.Sum(p => p.Paths.Count);

    /// <summary>Playlists left alone because GZJam has one of that name already.</summary>
    public IReadOnlyList<string> AlreadyThere { get; private init; } = [];

    /// <summary>Playlist files that couldn't be read, or held no song GZJam has.</summary>
    public IReadOnlyList<string> NothingToImport { get; private init; } = [];

    /// <summary>
    /// Songs listed by a path GZJam doesn't have, found elsewhere in its music folders: the same
    /// file name in the same folder, on another drive or computer (#202). Counted once per listing.
    /// </summary>
    public int FoundElsewhere { get; private init; }

    /// <summary>Where those songs' music moved, a folder then and now for each, most songs first (up to three).</summary>
    public IReadOnlyList<(string Was, string Now)> MovedFolders { get; private init; } = [];

    /// <summary>
    /// Songs listed that are on this computer, where the playlist says, but outside GZJam's music
    /// folders: they come along in the playlist, and stay out of the library (#237).
    /// </summary>
    public int OutsideSongs { get; private init; }

    /// <summary>Songs listed that aren't on this computer, in GZJam's music folders or where the playlist says.</summary>
    public int MissingSongs { get; private init; }

    /// <summary>Where the most of those are, most first (up to three).</summary>
    public IReadOnlyList<string> MissingFolders { get; private init; } = [];

    /// <summary>
    /// Works out what would come over from <paramref name="files"/> into <paramref name="gz"/>:
    /// at the top, or below <paramref name="root"/> in folders as the files are.
    /// </summary>
    public static PlaylistImport Plan(LibraryDatabase gz, IEnumerable<string> files, string? root = null)
    {
        var byPath = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (Track track in gz.Search(null))
            byPath.TryAdd(track.Path, track.Path);

        var existing = Existing(gz);
        var read = new List<(string File, string Name, IReadOnlyList<string> Folders, IReadOnlyList<string> Songs)>();
        var alreadyThere = new List<string>();
        var nothing = new List<string>();
        foreach (string file in files)
        {
            string name = Path.GetFileNameWithoutExtension(file).Trim();
            if (name.Length == 0)
                name = "Imported playlist";
            var folders = FoldersBelow(root, file);
            // Also the second of two files of the same name, picked together: not read, so not counted.
            if (!existing.Add(Key(folders, name)))
            {
                alreadyThere.Add(Shown(folders, name));
                continue;
            }
            try
            {
                read.Add((file, name, folders, PlaylistFile.Read(file)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                nothing.Add(Path.GetFileName(file));
            }
        }

        // Songs outside the library, there on this computer: they come as they are (#237).
        var onDisk = read.SelectMany(r => r.Songs).Where(s => !byPath.ContainsKey(s)).Distinct()
            .Where(s => LibraryScanner.IsAudioFile(s) && File.Exists(s)).ToHashSet();
        // Songs not where the playlists say: made on another computer, or the music moved since (#202).
        var (elsewhere, moved) = MovedSongs.Find(read.SelectMany(r => r.Songs).Where(s => !byPath.ContainsKey(s) && !onDisk.Contains(s)), byPath);

        var planned = new List<(string, IReadOnlyList<string>, List<string>)>();
        var missing = new List<string>();
        int foundElsewhere = 0, outside = 0;
        foreach (var (file, name, folders, songs) in read)
        {
            var paths = new List<string>();
            foreach (string song in songs)
            {
                if (byPath.TryGetValue(song, out string? ours))
                {
                    paths.Add(ours);   // as GZJam spells it, whatever the case in the playlist
                }
                else if (onDisk.Contains(song))
                {
                    paths.Add(song);
                    outside++;
                }
                else if (elsewhere.TryGetValue(song, out ours))
                {
                    paths.Add(ours);
                    foundElsewhere++;
                }
                else
                {
                    missing.Add(song);
                }
            }
            if (paths.Count == 0)
                nothing.Add(Path.GetFileName(file));
            else
                planned.Add((name, folders, paths));
        }

        var import = new PlaylistImport
        {
            AlreadyThere = alreadyThere,
            NothingToImport = nothing,
            FoundElsewhere = foundElsewhere,
            MovedFolders = foundElsewhere > 0 ? moved.Take(3).ToList() : [],
            OutsideSongs = outside,
            MissingSongs = missing.Count,
            MissingFolders = missing
                .GroupBy(Folder)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .Take(3)
                .ToList(),
        };
        import._playlists.AddRange(planned);
        return import;
    }

    /// <summary>The folder a song was listed in, by either separator (a playlist from Windows, read anywhere).</summary>
    private static string Folder(string path) => path.LastIndexOfAny(['\\', '/']) is var cut and > 0 ? path[..cut] : path;

    /// <summary>
    /// Makes the planned playlists, and the folders they go in that GZJam hasn't got. Returns how
    /// many were made (one named since the plan is skipped).
    /// </summary>
    public int Apply(LibraryDatabase gz)
    {
        var existing = Existing(gz);
        int made = 0;
        foreach (var (name, folders, paths) in _playlists)
        {
            if (!existing.Add(Key(folders, name)))
                continue;
            gz.AddToPlaylist(gz.CreatePlaylist(name, FolderFor(gz, folders)).Id, paths);
            made++;
        }
        return made;
    }

    /// <summary>The folder of playlists at this path, the ones GZJam hasn't got made: null for the top.</summary>
    private static long? FolderFor(LibraryDatabase gz, IReadOnlyList<string> path)
    {
        long? parent = null;
        foreach (string name in path)
        {
            var have = gz.GetPlaylistFolders().FirstOrDefault(f => f.ParentId == parent && string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            parent = (have ?? gz.CreatePlaylistFolder(name, parent)).Id;
        }
        return parent;
    }
}
