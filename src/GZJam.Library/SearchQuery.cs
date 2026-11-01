using System.Globalization;
using System.Text;

namespace GZJam.Library;

/// <summary>
/// What the DJ typed in the search box, turned into SQL conditions. Plain words match tags, folder
/// and file names as word prefixes; words in quotes match as a phrase (<c>"duke ellington"</c>: those
/// words together, in one field). Filters narrow by field, much like Mixxx's:
/// <list type="bullet">
/// <item><c>artist:basie</c>, <c>title:</c>, <c>album:</c>, <c>genre:</c>, <c>comment:</c>, <c>grouping:</c>, <c>folder:</c>,
/// <c>file:</c> (the file name; quote a phrase: <c>artist:"count basie"</c>)</item>
/// <item><c>bpm:140</c>, <c>bpm:130-150</c>, <c>bpm:&gt;200</c>; <c>~bpm:140</c> is near 140: within 8%
/// either side (as far as the tempo slider reaches) unless the DJ chose otherwise in Settings, or
/// for one search <c>~bpm:140/5</c> (5%)</item>
/// <item><c>year:1938</c>, <c>year:1935-1940</c>, <c>year:&lt;1950</c></item>
/// <item><c>rating:yes</c> (♥), <c>rating:no</c> (broken heart), <c>rating:none</c></item>
/// <item><c>energy:high</c>, <c>energy:medium</c>, <c>energy:low</c>, <c>energy:none</c>, <c>energy:2-3</c></item>
/// <item><c>label:lindy</c> (a label starting with that), <c>label:"female vocals"</c>, <c>label:none</c></item>
/// <item><c>played:7</c> (the room heard it in the last 7 days), <c>played:never</c>, <c>played:yes</c>;
/// <c>-played:7</c> leaves out last week's songs</item>
/// </list>
/// A leading <c>-</c> excludes: <c>-rating:no</c>, <c>-live</c>. <c>OR</c>, in capitals, between two
/// words or filters takes either (<c>label:balboa OR label:charleston</c>), binding tighter than the
/// spaces between the rest, as in Gmail (#245). Anything that isn't a complete filter (say,
/// <c>bpm:</c> half typed, or an <c>OR</c> with nothing after it yet) is ignored rather than
/// emptying the results.
/// </summary>
internal sealed class SearchQuery
{

    /// <summary>The BPM the app shows: the tag's, unless the DJ chose a range for detection.</summary>
    private const string BpmSql = "COALESCE(mb.bpm, CASE WHEN r.min IS NOT NULL THEN COALESCE(a.bpm, t.bpm) ELSE COALESCE(t.bpm, a.bpm) END)";

    private static readonly Dictionary<string, string> TextColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["title"] = "title",
        ["artist"] = "artist",
        ["album"] = "album",
        ["genre"] = "genre",
        ["comment"] = "comment",
        ["grouping"] = "grouping",
        ["folder"] = "folder",
        ["file"] = "file_name",
    };

    /// <summary>Where plain words count most: they name the song.</summary>
    private const string BestColumns = "{title artist album_artist}";

    /// <summary>Everything searched but the comment, which can mention anyone ("worked with Duke Ellington", #240).</summary>
    private const string AllButCommentColumns = "{title artist album_artist album genre grouping folder file_name}";

    private readonly List<string> _conditions = [];
    private readonly Dictionary<string, object> _parameters = [];
    private readonly List<string> _terms = [];
    private readonly DateTime _nowUtc;
    private readonly double _nearBpm;

    private SearchQuery(DateTime nowUtc, double nearBpm) => (_nowUtc, _nearBpm) = (nowUtc, nearBpm);

    /// <summary>SQL conditions to AND together; they refer to the tracks (t), analysis (a), BPM range (r) and manual BPM (mb).</summary>
    public IReadOnlyList<string> Conditions => _conditions;

    public IReadOnlyDictionary<string, object> Parameters => _parameters;

    /// <summary>
    /// When the search has plain words: an SQL expression to sort by first, 0 for songs whose title,
    /// artist or album artist hold the words, 1 for songs that have them elsewhere but the comment,
    /// 2 for songs that only match through their comment. Null otherwise (folder order alone).
    /// </summary>
    public string? Ranking { get; private set; }

    /// <param name="nowUtc">For <c>played:</c> filters; defaults to now.</param>
    /// <param name="nearBpm">How far <c>~bpm:</c> reaches either side, as a fraction.</param>
    public static SearchQuery Parse(string? text, DateTime? nowUtc = null, double nearBpm = LibraryDatabase.DefaultNearBpm)
    {
        var query = new SearchQuery(nowUtc ?? DateTime.UtcNow, nearBpm);
        foreach (var group in OrGroups(Tokenize(text ?? "")))
        {
            if (group.Count == 1)
                query.Add(group[0]);
            else
                query.AddEither(group);
        }
        if (query._terms.Count > 0)
        {
            string match = string.Join(' ', query._terms);
            query.Match(match, negated: false);
            query.Ranking = $"CASE WHEN {query.MatchSql($"{BestColumns} : ({match})")} THEN 0 " +
                            $"WHEN {query.MatchSql($"{AllButCommentColumns} : ({match})")} THEN 1 ELSE 2 END";
        }
        return query;
    }

    /// <summary>What <c>OR</c> joins in a search, in capitals only: a lowercase "or" is a word ("Now or Never").</summary>
    public const string Or = "OR";

    /// <summary>
    /// The tokens in groups of either-or: <c>jump a OR b c</c> is [jump], [a, b], [c]. An
    /// <c>OR</c> with nothing on one side is ignored.
    /// </summary>
    internal static List<List<string>> OrGroups(IEnumerable<string> tokens)
    {
        var groups = new List<List<string>>();
        bool joining = false;
        foreach (string token in tokens)
        {
            if (token == Or)
            {
                joining = groups.Count > 0;
                continue;
            }
            if (joining)
                groups[^1].Add(token);
            else
                groups.Add([token]);
            joining = false;
        }
        return groups;
    }

    /// <summary>
    /// Songs matching any of <paramref name="tokens"/>: each is read as on its own, and what it
    /// would have added is joined with OR instead. Words alone stay one search of the index (so
    /// best matches still come first); with a filter among them, an SQL condition.
    /// </summary>
    private void AddEither(List<string> tokens)
    {
        var parts = new List<(List<string> Conditions, List<string> Terms)>();
        foreach (string token in tokens)
        {
            int conditions = _conditions.Count, terms = _terms.Count;
            Add(token);
            var part = (_conditions[conditions..], _terms[terms..]);
            _conditions.RemoveRange(conditions, _conditions.Count - conditions);
            _terms.RemoveRange(terms, _terms.Count - terms);
            if (part.Item1.Count + part.Item2.Count > 0)
                parts.Add(part);
        }
        if (parts.Count == 0)
            return;
        if (parts.Count == 1)
        {
            _conditions.AddRange(parts[0].Conditions);
            _terms.AddRange(parts[0].Terms);
        }
        else if (parts.All(p => p.Conditions.Count == 0))
        {
            _terms.Add($"({string.Join(" OR ", parts.Select(p => $"({string.Join(' ', p.Terms)})"))})");
        }
        else
        {
            _conditions.Add($"({string.Join(" OR ", parts.Select(p =>
                $"({string.Join(" AND ", p.Conditions.Concat(p.Terms.Select(MatchSql)))})"))})");
        }
    }

    private void Add(string token)
    {
        bool negated = token.Length > 1 && token[0] == '-';
        if (negated)
            token = token[1..];

        int colon = token.IndexOf(':');
        if (colon <= 0 || token[0] == '"')
        {
            AddWords(token, negated);
            return;
        }
        string field = token[..colon], typed = token[(colon + 1)..], value = Unquote(typed);
        bool near = field.StartsWith('~');
        if (near)
            field = field[1..];

        string? condition = field.ToLowerInvariant() switch
        {
            "bpm" when near => NearBpmCondition(value),
            "bpm" => NumberCondition(BpmSql, value, roundExact: true),
            "year" => NumberCondition("t.year", value, roundExact: false),
            "rating" => RatingCondition(value),
            "energy" => EnergyCondition(value),
            "played" => PlayedCondition(value),
            "label" or "labels" => LabelCondition(value),
            var f when TextColumns.TryGetValue(f, out string? column) => TextCondition(column, value, phrase: typed.StartsWith('"'), negated),
            _ => null,
        };
        if (condition is null)
        {
            // Not a filter we know (or not finished): search for the words, or ignore a half-typed filter.
            if (!IsKnownField(field))
                AddWords(token, negated);
            return;
        }
        if (condition.Length > 0)
            _conditions.Add(negated && !TextColumns.ContainsKey(field) ? $"NOT COALESCE({condition}, 0)" : condition);
    }

    /// <summary>Whether a filter's field holds words (<c>artist:</c>, <c>title:</c>...), not a number or a choice.</summary>
    internal static bool IsTextField(string field) => TextColumns.ContainsKey(field);

    private static bool IsKnownField(string field) =>
        TextColumns.ContainsKey(field) || field.ToLowerInvariant() is "bpm" or "year" or "rating" or "energy" or "played" or "label" or "labels";

    /// <summary>Plain words, or a phrase in quotes (<c>"duke ellington"</c>, its closing quote maybe still to come).</summary>
    private void AddWords(string token, bool negated)
    {
        string? match = token.StartsWith('"')
            ? LibraryDatabase.BuildPhraseExpression(Unquote(token))
            : LibraryDatabase.BuildMatchExpression(token);
        if (match is null)
            return;
        if (negated)
            Match(match, negated: true);
        else
            _terms.Add(match);
    }

    private void Match(string expression, bool negated) =>
        _conditions.Add(negated ? $"NOT {MatchSql(expression)}" : MatchSql(expression));

    private string MatchSql(string expression) =>
        $"t.id IN (SELECT rowid FROM tracks_fts WHERE tracks_fts MATCH {Parameter(expression)})";

    /// <summary>
    /// Every word must appear in that column (as a prefix), or with <paramref name="phrase"/> the
    /// words together, in order. Returns "" once handled.
    /// </summary>
    private string? TextCondition(string column, string value, bool phrase, bool negated)
    {
        string? words = phrase ? LibraryDatabase.BuildPhraseExpression(value) : LibraryDatabase.BuildMatchExpression(value);
        if (words is null)
            return null;
        Match($"{column} : ({words})", negated);
        return "";
    }

    /// <summary>"140": near 140, as far as the DJ chose. "140/5": within 5% either side.</summary>
    private string? NearBpmCondition(string value)
    {
        double near = _nearBpm;
        int slash = value.IndexOf('/');
        if (slash >= 0)
        {
            string typed = value[(slash + 1)..].TrimEnd('%');
            if (typed.Length > 0)   // "140/" still being typed: as near as usual meanwhile
            {
                if (!TryNumber(typed, out double percent) || percent <= 0)
                    return null;
                near = Math.Min(percent, 50) / 100;
            }
            value = value[..slash];
        }
        if (!TryNumber(value, out double bpm) || bpm <= 0)
            return null;
        return $"{BpmSql} BETWEEN {Parameter(bpm * (1 - near))} AND {Parameter(bpm * (1 + near))}";
    }

    private string? NumberCondition(string sql, string value, bool roundExact) =>
        NumberTest.Parse(value, roundExact) is { } test ? NumberSql(sql, test) : null;

    private string NumberSql(string sql, NumberTest test) => test.Op switch
    {
        "between" => $"{sql} BETWEEN {Parameter(test.Low)} AND {Parameter(test.High)}",
        "round" => $"({sql} >= {Parameter(test.Low)} AND {sql} < {Parameter(test.High)})",
        var op => $"{sql} {op} {Parameter(test.Low)}",
    };

    /// <summary>
    /// "140", "130-150", "&gt;200", "&lt;=1945". With <c>roundExact</c>, a single number matches
    /// anything that rounds to it (a BPM of 139.6 is "140").
    /// </summary>
    private readonly record struct NumberTest(string Op, double Low, double High)
    {
        public static NumberTest? Parse(string value, bool roundExact)
        {
            foreach (string op in new[] { ">=", "<=", ">", "<" })
            {
                if (value.StartsWith(op, StringComparison.Ordinal))
                    return TryNumber(value[op.Length..], out double limit) ? new NumberTest(op, limit, limit) : null;
            }
            int dash = value.Length > 1 ? value.IndexOf('-', 1) : -1;   // not a leading minus
            if (dash > 0)
            {
                if (!TryNumber(value[..dash], out double low) || !TryNumber(value[(dash + 1)..], out double high))
                    return null;
                return low > high ? new NumberTest("between", high, low) : new NumberTest("between", low, high);
            }
            if (!TryNumber(value, out double exact))
                return null;
            return roundExact ? new NumberTest("round", exact - 0.5, exact + 0.5) : new NumberTest("=", exact, exact);
        }

        public bool Matches(double x) => Op switch
        {
            ">=" => x >= Low,
            "<=" => x <= Low,
            ">" => x > Low,
            "<" => x < Low,
            "between" => x >= Low && x <= High,
            "round" => x >= Low && x < High,
            _ => x == Low,
        };
    }

    /// <summary>
    /// Songs whose marks pass <paramref name="test"/> on <paramref name="column"/>. Only marked songs
    /// have a row in marks, so a test unmarked songs (0) fail starts from those rows, which is quick
    /// even in a huge library; one they pass leaves out the marked songs that fail it.
    /// </summary>
    private string MarksCondition(string column, NumberTest test) => test.Matches(0)
        ? $"t.path NOT IN (SELECT path FROM marks WHERE NOT ({NumberSql(column, test)}))"
        : $"t.path IN (SELECT path FROM marks WHERE {NumberSql(column, test)})";

    private string? RatingCondition(string value)
    {
        Rating? rating = value.ToLowerInvariant() switch
        {
            "yes" or "y" or "loved" or "love" or "heart" or "♥" or "1" => Rating.Loved,
            "no" or "n" or "rejected" or "broken" or "-1" => Rating.Rejected,
            "none" or "unrated" or "0" => Rating.Unrated,
            _ => null,
        };
        return rating is null ? null : MarksCondition("rating", new NumberTest("=", (int)rating, (int)rating));
    }

    private string? EnergyCondition(string value)
    {
        string number = value.ToLowerInvariant() switch
        {
            "high" or "h" => "3",
            "medium" or "med" or "m" => "2",
            "low" or "l" => "1",
            "none" or "unset" => "0",
            var other => other,
        };
        return NumberTest.Parse(number, roundExact: false) is { } test ? MarksCondition("energy", test) : null;
    }

    /// <summary>A label whose name starts with the value (any case), or "none" for songs without labels.</summary>
    private string? LabelCondition(string value)
    {
        value = value.Trim();
        if (value.Length == 0)
            return null;
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
            return "t.path NOT IN (SELECT path FROM track_labels)";
        string pattern = value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";
        return $"t.path IN (SELECT tl.path FROM track_labels tl JOIN labels l ON l.id = tl.label_id WHERE l.name LIKE {Parameter(pattern)} ESCAPE '\\')";
    }

    /// <summary>"never", "yes", or a number of days: played within that many days.</summary>
    private string? PlayedCondition(string value)
    {
        switch (value.ToLowerInvariant())
        {
            case "never" or "no" or "none":
                return "t.path NOT IN (SELECT path FROM plays)";
            case "yes" or "ever" or "y":
                return "t.path IN (SELECT path FROM plays)";
        }
        if (!TryNumber(value, out double days) || days <= 0)
            return null;
        return $"t.path IN (SELECT path FROM plays WHERE played_utc >= {Parameter((_nowUtc - TimeSpan.FromDays(days)).Ticks)})";
    }

    private string Parameter(object value)
    {
        string name = $"$q{_parameters.Count}";
        _parameters[name] = value;
        return name;
    }

    private static bool TryNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1]
        : value.StartsWith('"') ? value[1..]   // still typing the closing quote
        : value;

    /// <summary>Splits on spaces, keeping quoted text together: <c>artist:"count basie" jump</c> is two tokens.</summary>
    internal static IEnumerable<string> Tokenize(string text)
    {
        var token = new StringBuilder();
        bool quoted = false;
        foreach (char ch in text)
        {
            if (ch == '"')
                quoted = !quoted;
            if (char.IsWhiteSpace(ch) && !quoted)
            {
                if (token.Length > 0)
                    yield return token.ToString();
                token.Clear();
            }
            else
            {
                token.Append(ch);
            }
        }
        if (token.Length > 0)
            yield return token.ToString();
    }
}
