using System.Text.RegularExpressions;

namespace GZJam.Library;

/// <summary>What to bring over from Mixxx.</summary>
public sealed record MixxxImportOptions(bool Ratings = true, bool Crates = true, bool History = true, bool LockedBpms = true);

/// <summary>What an import brought over.</summary>
public sealed record MixxxImportResult(int Ratings, int Labels, int LabelledSongs, int Sessions, int Plays, int Bpms);

/// <summary>
/// Bringing a Mixxx library into GZJam, planned first (so the DJ sees what will come over) and
/// then applied. Songs are matched by path, or by file name and size when the music has moved
/// (one match only). Only what GZJam doesn't have yet is filled in: a song rated in GZJam keeps
/// its rating, a typed BPM stays, and a History night already imported isn't added twice. Nothing
/// is written to Mixxx or to the music files.
/// <list type="bullet">
/// <item>Ratings: 4-5 stars become ♥, 1-2 stars a broken heart; 3 stars stay unrated.</item>
/// <item>Crates become labels of the same name.</item>
/// <item>History: each Mixxx night becomes a session, with its songs and when they were played.</item>
/// <item>BPMs locked in Mixxx (the DJ confirmed them) become typed BPMs.</item>
/// </list>
/// Cue points aren't brought over: Mixxx places its intro and outro markers automatically, and
/// GZJam finds where each song starts and ends for itself.
/// </summary>
public sealed partial class MixxxImport
{
    private readonly List<(string Path, Rating Rating)> _ratings = [];
    private readonly List<(string Name, List<string> Paths)> _crates = [];
    private readonly List<(string? Name, List<(Track Track, DateTime PlayedUtc)> Plays)> _sessions = [];
    private readonly List<(string Path, double Bpm)> _bpms = [];

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
    public int SessionCount => _sessions.Count;
    public int SessionPlayCount => _sessions.Sum(s => s.Plays.Count);
    public int BpmCount => _bpms.Count;

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
            Rating rating = song.Stars switch { >= 4 => Rating.Loved, 1 or 2 => Rating.Rejected, _ => Rating.Unrated };
            if (rating != Rating.Unrated && track.Rating == Rating.Unrated)
                import._ratings.Add((track.Path, rating));
            if (song.BpmLocked && song.Bpm is { } bpm && track.ManualBpm is null)
                import._bpms.Add((track.Path, Math.Round(bpm, 2)));
        }

        foreach (MixxxCrate crate in mixxx.Crates())
        {
            var paths = crate.TrackIds.Where(matches.ContainsKey).Select(id => matches[id].Path).Distinct().ToList();
            if (paths.Count > 0)
                import._crates.Add((crate.Name, paths));
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
        int ratings = 0, labelledSongs = 0, sessions = 0, plays = 0, bpms = 0;
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
            labelledSongs = CrateSongCount;
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
        return new MixxxImportResult(ratings, options.Crates ? _crates.Count : 0, labelledSongs, sessions, plays, bpms);
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
