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
        var byName = ours.Values.ToLookup(p => Segments(p)[^1], StringComparer.OrdinalIgnoreCase);
        var paths = missing.Distinct(StringComparer.Ordinal).ToList();
        var byEnding = new Dictionary<string, (string Song, Move? Move)>(StringComparer.Ordinal);
        var votes = new Dictionary<Move, int>();

        foreach (string path in paths)
        {
            string[] theirs = Segments(path);
            string? best = null;
            int bestLength = 0;
            bool tied = false;
            foreach (string candidate in byName[theirs[^1]])
            {
                int length = SameEnding(theirs, Segments(candidate));
                if (length > bestLength)
                    (best, bestLength, tied) = (candidate, length, false);
                else if (length == bestLength)
                    tied = true;
            }
            if (best is null || tied || bestLength < 2)
                continue;
            // Where it moved: what's before the part that's the same. Not when that's all of it.
            Move? move = Parent(path, bestLength) is { } from && Parent(best, bestLength) is { } to ? new Move(from, to) : null;
            byEnding[path] = (best, move);
            if (move is not null)
                votes[move] = votes.GetValueOrDefault(move) + 1;
        }

        // The deepest folder first; where no agreed move covers a song, any move one song showed,
        // then the song's own ending.
        var deepest = votes.Keys.OrderByDescending(m => m.From.Length).ThenByDescending(m => votes[m]).ToList();
        var agreed = deepest.Where(m => votes[m] >= 2).ToList();
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        var used = new Dictionary<Move, (int Count, string Was, string Now)>();
        foreach (string path in paths)
        {
            (string Song, Move? Move)? ending = byEnding.TryGetValue(path, out var e) ? e : null;
            bool covered = agreed.Any(m => LibraryDatabase.MovedPath(path, m.From, m.To) is not null);
            if ((FindMoved(path, covered ? agreed : deepest, ours, ending?.Song) ?? (covered ? null : ending)) is not var (song, move))
                continue;
            found[path] = song;
            if (move is not null)
                used[move] = used.TryGetValue(move, out var u) ? (u.Count + 1, u.Was, u.Now) : (1, Parent(path, 1) ?? path, Parent(song, 1) ?? song);
        }
        return (found, used.Values.OrderByDescending(u => u.Count).Select(u => (u.Was, u.Now)).ToList());
    }

    /// <summary>Music that was under <c>From</c> and is under <c>To</c> now.</summary>
    private sealed record Move(string From, string To);

    /// <summary>
    /// The first of <paramref name="moves"/> that puts <paramref name="path"/> on a song GZJam has,
    /// as GZJam spells it, or on its <paramref name="ending"/> in another case (a playlist from
    /// Windows, where case doesn't matter, on Linux or a Mac).
    /// </summary>
    private static (string Song, Move? Move)? FindMoved(string path, IEnumerable<Move> moves, IReadOnlyDictionary<string, string> ours, string? ending)
    {
        foreach (Move move in moves)
        {
            if (LibraryDatabase.MovedPath(path, move.From, move.To) is not { } moved)
                continue;
            if (ours.TryGetValue(moved, out string? song))
                return (song, move);
            if (moved.Equals(ending, StringComparison.OrdinalIgnoreCase))
                return (ending, move);
        }
        return null;
    }

    /// <summary>The names in a path, either separator (a playlist from Windows, read anywhere).</summary>
    private static string[] Segments(string path) => path.Split(Separators, StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } s ? s : [path];

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
