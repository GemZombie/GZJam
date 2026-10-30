namespace GZJam.Library;

/// <summary>
/// Finds songs listed by a path GZJam doesn't have, made on another computer or before the music
/// moved (#202): <c>D:\Music\Swing\song.mp3</c> when GZJam has <c>C:\Music\Swing\song.mp3</c>.
/// A song is the one GZJam has whose path ends the same way, the file name and at least its
/// folder, if no other ends as well. Each such song shows where its music moved
/// (<c>D:\</c> is now <c>C:\</c>). Once two songs agree on a move, the rest of that folder is
/// looked for only there: a file name in many folders (<c>01 Track 1.mp3</c>) is found that way,
/// and an ending like <c>CD1\01 Track 1.mp3</c> isn't taken from another album. Two equally good
/// matches are neither.
/// </summary>
internal static class MovedSongs
{
    private static readonly char[] Separators = ['\\', '/'];

    /// <summary>
    /// Of <paramref name="missing"/>, the ones found among <paramref name="ours"/> (GZJam's songs,
    /// keyed by path as the computer compares them), and where their music moved, most songs first:
    /// for each move, one song's folder then and now, to show the DJ.
    /// </summary>
    public static (Dictionary<string, string> Found, List<(string Was, string Now)> Moved) Find(
        IEnumerable<string> missing, IReadOnlyDictionary<string, string> ours)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        var paths = missing.Distinct(StringComparer.Ordinal).ToList();
        if (paths.Count == 0)
            return (found, []);

        var byName = ours.Values.Select(p => new Song(p, Segments(p))).ToLookup(s => s.Names[^1], StringComparer.OrdinalIgnoreCase);
        var anyCase = OperatingSystem.IsWindows() ? null : AnyCase(ours.Values);
        var endings = new Dictionary<string, (string Song, Move? Move)>(StringComparer.Ordinal);   // the song each ends as, and where that says it moved
        var votes = new Dictionary<Move, HashSet<string>>();   // the songs that show each move

        foreach (string path in paths)
        {
            string[] theirs = Segments(path);
            Song? best = null;
            int bestLength = 0;
            bool tied = false;
            foreach (Song candidate in byName[theirs[^1]])
            {
                int length = SameEnding(theirs, candidate.Names);
                if (length > bestLength)
                    (best, bestLength, tied) = (candidate, length, false);
                else if (length == bestLength)
                    tied = true;
            }
            if (best is null || tied || bestLength < 2)
                continue;
            // Where it moved: what's before the part that's the same. Not when that's all of it.
            Move? move = Parent(path, bestLength) is { } from && Parent(best.Path, bestLength) is { } to ? new Move(from, to) : null;
            endings[path] = (best.Path, move);
            if (move is null)
                continue;
            if (!votes.TryGetValue(move, out var songs))
                votes[move] = songs = new HashSet<string>(StringComparer.Ordinal);
            songs.Add(best.Path);   // a song listed twice, or in two playlists, is one vote
        }

        // The deepest folder first.
        var deepest = votes.Keys.OrderByDescending(m => m.From.Length).ThenByDescending(m => votes[m].Count).ToList();
        var agreed = deepest.Where(m => votes[m].Count >= 2).ToList();
        var used = new Dictionary<Move, (int Count, string Was, string Now)>();
        foreach (string path in paths)
        {
            (string Song, Move? Move)? ending = endings.TryGetValue(path, out var e) ? e : null;
            var covering = agreed.Where(m => IsUnder(path, m.From)).ToList();
            (string Song, Move? Move)? match;
            if (covering.Count == 0)
            {
                // No two songs agree where its folder went: any move one song showed, then its own ending.
                match = FindMoved(path, deepest, ours, anyCase) ?? ending;
            }
            else
            {
                // Where songs agree its folder went. Or its own ending, if that keeps every name the
                // song had below that folder (one added on the way, as Archive), none renamed.
                match = FindMoved(path, covering, ours, anyCase);
                if (match is null && ending is { } own && covering.Any(m => KeepsNames(path, own.Song, m)))
                    match = ending;
            }
            if (match is not var (song, move))
                continue;
            found[path] = song;
            if (move is not null)
                used[move] = used.TryGetValue(move, out var u) ? (u.Count + 1, u.Was, u.Now) : (1, Parent(path, 1) ?? path, Parent(song, 1) ?? song);
        }
        return (found, used.Values.OrderByDescending(u => u.Count).Select(u => (u.Was, u.Now)).ToList());
    }

    /// <summary>One of GZJam's songs, and the names in its path.</summary>
    private sealed record Song(string Path, string[] Names);

    /// <summary>Music that was under <c>From</c> (in any case: a playlist from Windows) and is under <c>To</c> now.</summary>
    private sealed record Move(string From, string To)
    {
        public bool Equals(Move? other) => other is not null && From.Equals(other.From, StringComparison.OrdinalIgnoreCase) && To == other.To;

        public override int GetHashCode() => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(From), To);
    }

    /// <summary>
    /// The first of <paramref name="moves"/> that puts <paramref name="path"/> on a song GZJam has,
    /// as GZJam spells it.
    /// </summary>
    private static (string Song, Move? Move)? FindMoved(string path, List<Move> moves, IReadOnlyDictionary<string, string> ours, Dictionary<string, string?>? anyCase)
    {
        foreach (Move move in moves)
        {
            if (LibraryDatabase.MovedPath(path, move.From, move.To) is { } moved
                && (ours.GetValueOrDefault(moved) ?? anyCase?.GetValueOrDefault(moved)) is { } song)
                return (song, move);
        }
        return null;
    }

    /// <summary>
    /// GZJam's songs by path in any case, for a playlist from Windows (where case doesn't matter)
    /// on Linux or a Mac. Null for a path two songs share but for case: it could be either.
    /// </summary>
    private static Dictionary<string, string?> AnyCase(IEnumerable<string> paths)
    {
        var anyCase = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
            anyCase[path] = anyCase.ContainsKey(path) ? null : path;
        return anyCase;
    }

    /// <summary>
    /// Whether <paramref name="song"/> is where <paramref name="move"/> took <paramref name="path"/>'s
    /// music, every name below the folder kept in order: <c>Music\Blues\c.mp3</c> as
    /// <c>Music\Archive\Blues\c.mp3</c>, but not <c>Basie\Live\01.mp3</c> as <c>Ellington\Live\01.mp3</c>.
    /// </summary>
    private static bool KeepsNames(string path, string song, Move move)
    {
        if (!IsUnder(song, move.To))
            return false;
        string[] theirs = Segments(path)[NameCount(move.From)..];
        int kept = 0;
        foreach (string name in Segments(song)[NameCount(move.To)..])
        {
            if (kept < theirs.Length && name.Equals(theirs[kept], StringComparison.OrdinalIgnoreCase))
                kept++;
        }
        return kept == theirs.Length;
    }

    /// <summary>Whether <paramref name="path"/> is in <paramref name="folder"/>, in any case.</summary>
    private static bool IsUnder(string path, string folder) =>
        path.Length > folder.Length && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
        && (Separators.Contains(path[folder.Length]) || Separators.Contains(folder[^1]));

    /// <summary>The names in a path, either separator (a playlist from Windows, read anywhere).</summary>
    private static string[] Segments(string path) => path.Split(Separators, StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } s ? s : [path];

    /// <summary>How many names are in a path (none in <c>/</c>).</summary>
    private static int NameCount(string path) => path.Split(Separators, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>How many names, from the end, two paths share (in any case).</summary>
    private static int SameEnding(string[] a, string[] b)
    {
        int n = 0;
        while (n < a.Length && n < b.Length && a[^(n + 1)].Equals(b[^(n + 1)], StringComparison.OrdinalIgnoreCase))
            n++;
        return n;
    }

    /// <summary><paramref name="path"/> with its last <paramref name="names"/> names gone, or null if nothing's left.</summary>
    private static string? Parent(string path, int names)
    {
        string rest = path.TrimEnd(Separators);
        for (int i = 0; i < names; i++)
        {
            int cut = rest.LastIndexOfAny(Separators);
            if (cut < 0)
                return null;
            rest = rest[..cut].TrimEnd(Separators);
        }
        return rest.Length > 0 ? rest : path[..1] is "/" ? "/" : null;
    }
}
