namespace GZJam.Library;

/// <summary>
/// Bringing M3U playlists into GZJam (see <see cref="PlaylistFile"/>), planned first so the DJ
/// sees what will come over, then applied. Each file becomes a playlist named after it, with the
/// songs GZJam has, in order. Songs are matched by path, or where the playlist was made on another
/// computer, by the end of their path (<see cref="MovedSongs"/>); ones outside GZJam's music
/// folders, or gone, are counted and left out. A name GZJam has already is skipped, so importing the same
/// files again adds nothing twice. The playlist files are only read.
/// </summary>
public sealed class PlaylistImport
{
    private readonly List<(string Name, List<string> Paths)> _playlists = [];

    private PlaylistImport()
    {
    }

    /// <summary>Playlists that would come over, with how many of their songs GZJam has.</summary>
    public IReadOnlyList<(string Name, int Songs)> Playlists => _playlists.Select(p => (p.Name, p.Paths.Count)).ToList();

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

    /// <summary>Works out what would come over from <paramref name="files"/> into <paramref name="gz"/>.</summary>
    public static PlaylistImport Plan(LibraryDatabase gz, IEnumerable<string> files)
    {
        var byPath = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (Track track in gz.Search(null))
            byPath.TryAdd(track.Path, track.Path);

        var existing = gz.GetPlaylists().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var read = new List<(string File, string Name, IReadOnlyList<string> Songs)>();
        var alreadyThere = new List<string>();
        var nothing = new List<string>();
        foreach (string file in files)
        {
            string name = Path.GetFileNameWithoutExtension(file).Trim();
            if (name.Length == 0)
                name = "Imported playlist";
            // Also the second of two files of the same name, picked together: not read, so not counted.
            if (!existing.Add(name))
            {
                alreadyThere.Add(name);
                continue;
            }
            try
            {
                read.Add((file, name, PlaylistFile.Read(file)));
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

        var planned = new List<(string, List<string>)>();
        var missing = new List<string>();
        int foundElsewhere = 0, outside = 0;
        foreach (var (file, name, songs) in read)
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
                planned.Add((name, paths));
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

    /// <summary>Makes the planned playlists. Returns how many were made (one named since the plan is skipped).</summary>
    public int Apply(LibraryDatabase gz)
    {
        var existing = gz.GetPlaylists().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int made = 0;
        foreach (var (name, paths) in _playlists)
        {
            if (!existing.Add(name))
                continue;
            gz.AddToPlaylist(gz.CreatePlaylist(name).Id, paths);
            made++;
        }
        return made;
    }
}
