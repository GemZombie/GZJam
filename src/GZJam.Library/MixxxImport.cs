using System.Text.RegularExpressions;

namespace GZJam.Library;

/// <summary>What to bring over from Mixxx.</summary>
public sealed record MixxxImportOptions(bool Ratings = true, bool Crates = true, bool History = true, bool LockedBpms = true, bool HotCues = true,
    bool Playlists = true);

/// <summary>What an import brought over.</summary>
public sealed record MixxxImportResult(int Ratings, int Labels, int LabeledSongs, int Sessions, int Plays, int Bpms, int CuedSongs = 0,
    int Playlists = 0);

/// <summary>
/// Bringing a Mixxx library into GZJam, planned first (so the DJ sees what will come over) and
/// then applied. Songs are matched by path, or by file name and size when the music has moved
/// (one match only). Only what GZJam doesn't have yet is filled in: a song rated in GZJam keeps
/// its rating, a typed BPM stays, and a History night already imported isn't added twice. Nothing
/// is written to Mixxx or to the music files.
/// <list type="bullet">
/// <item>Ratings: 4-5 stars become ♥, 1-2 stars a broken heart; 3 stars stay unrated.</item>
/// <item>Crates become labels of the same name.</item>
/// <item>Playlists become playlists, in their order, unless GZJam has one of that name already.</item>
/// <item>History: each Mixxx night becomes a session, with its songs and when they were played.</item>
/// <item>BPMs locked in Mixxx (the DJ confirmed them) become typed BPMs.</item>
/// <item>Hot cues 1 to 4 become cues 1 to 4, for songs with no cues in GZJam yet; Mixxx's others
/// (it has up to 36) are counted and left out.</item>
/// </list>
/// Mixxx's intro and outro markers aren't brought over: Mixxx places them automatically, and
/// GZJam finds where each song starts and ends for itself.
/// </summary>
public sealed partial class MixxxImport
{
    private readonly List<(string Path, Rating Rating)> _ratings = [];
    private readonly List<(string Name, List<string> Paths)> _crates = [];
    private readonly List<(string Name, List<string> Paths)> _playlists = [];
    private readonly List<(string? Name, List<(Track Track, DateTime PlayedUtc)> Plays)> _sessions = [];
    private readonly List<(string Path, double Bpm)> _bpms = [];
    private readonly List<(string Path, List<SongCue> Cues)> _cues = [];

    private MixxxImport()
    {
    }

    /// <summary>The Mixxx database planned from.</summary>
    public required string Source { get; init; }

    /// <summary>Songs in Mixxx's library (not counting ones removed from it).</summary>
    public int MixxxSongs { get; private init; }

    /// <summary>Of those, the songs found in GZJam's library.</summary>
    public int FoundSongs { get; private init; }

    /// <summary>Folders with Mixxx songs GZJam doesn't have (not among its music folders), most songs first.</summary>
    public IReadOnlyList<string> MissingFolders { get; private init; } = [];

    public int RatingCount => _ratings.Count;
    public int CrateCount => _crates.Count;
    public int CrateSongCount => _crates.SelectMany(c => c.Paths).Distinct().Count();
    public int PlaylistCount => _playlists.Count;
    public int PlaylistSongCount => _playlists.Sum(p => p.Paths.Count);
    public int SessionCount => _sessions.Count;
    public int SessionPlayCount => _sessions.Sum(s => s.Plays.Count);
    public int BpmCount => _bpms.Count;

    /// <summary>Songs whose hot cues 1 to 4 come over as cues.</summary>
    public int CuedSongCount => _cues.Count;

    /// <summary>Of those songs' hot cues, the ones numbered above 4: GZJam has four cue keys.</summary>
    public int LeftOutHotCues { get; private set; }

    /// <summary>Works out what would come over from <paramref name="mixxx"/> into <paramref name="gz"/>.</summary>
    public static MixxxImport Plan(LibraryDatabase gz, MixxxLibrary mixxx)
    {
        IReadOnlyList<Track> ours = gz.Search(null);
        var byPath = new Dictionary<string, Track>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (Track track in ours)
            byPath.TryAdd(track.Path, track);
        // Moved music: the same file name and size, if only one song has them.
        var byNameAndSize = ours
            .GroupBy(t => (t.FileName.ToLowerInvariant(), t.FileSize))
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.First());

        IReadOnlyList<MixxxTrack> theirs = mixxx.Tracks();
        var matches = new Dictionary<long, Track>();
        foreach (MixxxTrack song in theirs)
        {
            if (byPath.TryGetValue(song.Path, out Track? match) ||
                song.FileSize is { } size && byNameAndSize.TryGetValue((song.FileName.ToLowerInvariant(), size), out match))
                matches[song.Id] = match;
        }

        var listed = theirs.Where(t => !t.Hidden).ToList();
        var import = new MixxxImport
        {
            Source = mixxx.Path,
            MixxxSongs = listed.Count,
            FoundSongs = listed.Count(t => matches.ContainsKey(t.Id)),
            MissingFolders = listed
                .Where(t => !matches.ContainsKey(t.Id))
                .GroupBy(t => System.IO.Path.GetDirectoryName(t.Path) ?? "")
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .Take(3)
                .ToList(),
        };

        foreach (MixxxTrack song in listed)
        {
            if (!matches.TryGetValue(song.Id, out Track? track))
                continue;
            Rating rating = FileRatings.RatingFromStars(song.Stars);
            if (rating != Rating.Unrated && track.Rating == Rating.Unrated)
                import._ratings.Add((track.Path, rating));
            if (song.BpmLocked && song.Bpm is { } bpm && track.ManualBpm is null)
                import._bpms.Add((track.Path, Math.Round(bpm, 2)));
        }

        var listedIds = listed.Select(t => t.Id).ToHashSet();
        foreach (var song in mixxx.HotCues().Where(c => listedIds.Contains(c.TrackId)).GroupBy(c => c.TrackId))
        {
            if (!matches.TryGetValue(song.Key, out Track? track) || gz.GetCues(track.Path).Count > 0)
                continue;   // not in GZJam, or cues set in GZJam already: those stay
            var cues = song.Where(c => c.Number <= LibraryDatabase.MaxCues)
                .GroupBy(c => c.Number).Select(g => new SongCue(g.Key, g.First().At)).OrderBy(c => c.Number).ToList();
            import.LeftOutHotCues += song.Count(c => c.Number > LibraryDatabase.MaxCues);
            if (cues.Count > 0)
                import._cues.Add((track.Path, cues));
        }

        foreach (MixxxCrate crate in mixxx.Crates())
        {
            var paths = crate.TrackIds.Where(matches.ContainsKey).Select(id => matches[id].Path).Distinct().ToList();
            if (paths.Count > 0)
                import._crates.Add((crate.Name, paths));
        }

        // Playlists: the songs GZJam has, in Mixxx's order. One GZJam already has by name was
        // imported before, or is the DJ's own: left alone.
        var existing = gz.GetPlaylists().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (MixxxPlaylist playlist in mixxx.Playlists())
        {
            var paths = playlist.TrackIds.Where(matches.ContainsKey).Select(id => matches[id].Path).ToList();
            if (paths.Count > 0 && existing.Add(playlist.Name))
                import._playlists.Add((playlist.Name, paths));
        }

        var songs = theirs.ToDictionary(t => t.Id);
        foreach (MixxxSession night in mixxx.History())
        {
            var plays = new List<(Track, DateTime)>();
            foreach (var (id, playedUtc) in night.Plays)
            {
                if (matches.TryGetValue(id, out Track? track))
                    plays.Add((track, playedUtc));
                else if (songs.TryGetValue(id, out MixxxTrack? song))
                    plays.Add((AsTrack(song), playedUtc));   // not in GZJam's folders: still part of that night
            }
            // Imported before (the first song is already in History at that moment): skip the night.
            if (plays.Count == 0 || gz.HasPlay(plays[0].Item1.Path, plays[0].Item2))
                continue;
            import._sessions.Add((SessionName(night.Name), plays));
        }
        return import;
    }

    /// <summary>Brings over what was planned and chosen. Safe to repeat: what's there already is skipped.</summary>
    public MixxxImportResult Apply(LibraryDatabase gz, MixxxImportOptions options)
    {
        int ratings = 0, labeledSongs = 0, sessions = 0, plays = 0, bpms = 0, cuedSongs = 0, playlists = 0;
        if (options.Ratings)
        {
            foreach (var group in _ratings.GroupBy(r => r.Rating))
                gz.SetRating(group.Select(r => r.Path), group.Key);
            ratings = _ratings.Count;
        }
        if (options.Crates)
        {
            foreach (var (name, paths) in _crates)
                gz.AddLabel(paths, name);
            labeledSongs = CrateSongCount;
        }
        if (options.Playlists)
        {
            var existing = gz.GetPlaylists().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, paths) in _playlists)
            {
                if (!existing.Add(name))
                    continue;   // made in GZJam since the plan
                gz.AddToPlaylist(gz.CreatePlaylist(name).Id, paths);
                playlists++;
            }
        }
        if (options.History)
        {
            foreach (var (name, nightPlays) in _sessions)
            {
                gz.ImportSession(name, nightPlays);
                sessions++;
                plays += nightPlays.Count;
            }
        }
        if (options.LockedBpms)
        {
            foreach (var group in _bpms.GroupBy(b => b.Bpm))
                gz.SetManualBpm(group.Select(b => b.Path), group.Key);
            bpms = _bpms.Count;
        }
        if (options.HotCues)
        {
            foreach (var (path, cues) in _cues)
            {
                if (gz.GetCues(path).Count > 0)
                    continue;   // set in GZJam since the plan
                gz.SetCues(path, cues);
                cuedSongs++;
            }
        }
        return new MixxxImportResult(ratings, options.Crates ? _crates.Count : 0, labeledSongs, sessions, plays, bpms, cuedSongs, playlists);
    }

    /// <summary>A song GZJam doesn't have, as Mixxx knew it, for History.</summary>
    private static Track AsTrack(MixxxTrack song) => new()
    {
        Path = song.Path,
        Folder = "",
        FileName = song.FileName,
        Title = song.Title,
        Artist = song.Artist,
        Album = song.Album,
        TagBpm = song.Bpm,
        Duration = song.Duration,
    };

    /// <summary>Mixxx names History nights by date ("2024-03-14", "2024-03-14 (2)"); GZJam shows dates itself.</summary>
    private static string? SessionName(string name) =>
        string.IsNullOrWhiteSpace(name) || DateName().IsMatch(name.Trim()) ? null : name.Trim();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}( ?\(\d+\)| #\d+)?$")]
    private static partial Regex DateName();
}
