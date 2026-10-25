namespace GZJam.Library;

/// <summary>
/// Bringing M3U playlists into GZJam (see <see cref="PlaylistFile"/>), planned first so the DJ
/// sees what will come over, then applied. Each file becomes a playlist named after it, with the
/// songs GZJam has, in order. Songs are matched by path; ones outside GZJam's music folders, or
/// gone, are counted and left out. A name GZJam has already is skipped, so importing the same
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

    /// <summary>Songs listed that aren't in GZJam's music folders (or aren't there any more).</summary>
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
        var planned = new List<(string, List<string>)>();
        var alreadyThere = new List<string>();
        var nothing = new List<string>();
        var missing = new List<string>();
        foreach (string file in files)
        {
            string name = Path.GetFileNameWithoutExtension(file).Trim();
            if (name.Length == 0)
                name = "Imported playlist";
            if (existing.Contains(name))
            {
                alreadyThere.Add(name);
                continue;
            }
            IReadOnlyList<string> songs;
            try
            {
                songs = PlaylistFile.Read(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                nothing.Add(Path.GetFileName(file));
                continue;
            }
            var paths = new List<string>();
            foreach (string song in songs)
            {
                if (byPath.TryGetValue(song, out string? ours))
                    paths.Add(ours);   // as GZJam spells it, whatever the case in the playlist
                else
                    missing.Add(song);
            }
            if (paths.Count == 0)
            {
                nothing.Add(Path.GetFileName(file));
                continue;
            }
            existing.Add(name);
            planned.Add((name, paths));
        }

        var import = new PlaylistImport
        {
            AlreadyThere = alreadyThere,
            NothingToImport = nothing,
            MissingSongs = missing.Count,
            MissingFolders = missing
                .GroupBy(p => Path.GetDirectoryName(p) ?? "")
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .Take(3)
                .ToList(),
        };
        import._playlists.AddRange(planned);
        return import;
    }

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
