using System.Globalization;
using System.Text;

namespace GZJam.Library;

/// <summary>
/// What the DJ typed in the search box, turned into SQL conditions. Plain words match tags and
/// folder names as word prefixes; filters narrow by field, much like Mixxx's:
/// <list type="bullet">
/// <item><c>artist:basie</c>, <c>title:</c>, <c>album:</c>, <c>genre:</c>, <c>comment:</c>, <c>folder:</c>
/// (quote several words: <c>artist:"count basie"</c>)</item>
/// <item><c>bpm:140</c>, <c>bpm:130-150</c>, <c>bpm:&gt;200</c>; <c>~bpm:140</c> is within 8% either side,
/// as far as the tempo slider reaches</item>
/// <item><c>year:1938</c>, <c>year:1935-1940</c>, <c>year:&lt;1950</c></item>
/// <item><c>rating:yes</c> (♥), <c>rating:no</c> (broken heart), <c>rating:none</c></item>
/// <item><c>energy:high</c>, <c>energy:medium</c>, <c>energy:low</c>, <c>energy:none</c>, <c>energy:2-3</c></item>
/// <item><c>played:7</c> (the room heard it in the last 7 days), <c>played:never</c>, <c>played:yes</c>;
/// <c>-played:7</c> leaves out last week's songs</item>
/// </list>
/// A leading <c>-</c> excludes: <c>-rating:no</c>, <c>-live</c>. Anything that isn't a complete
/// filter (say, <c>bpm:</c> half typed) is ignored rather than emptying the results.
/// </summary>
internal sealed class SearchQuery
{
    /// <summary>How far <c>~bpm:</c> reaches either side: the tempo slider's range.</summary>
    public const double NearBpm = 0.08;

    /// <summary>The BPM the app shows: the tag's, unless the DJ chose a range for detection.</summary>
    private const string BpmSql = "(CASE WHEN r.min IS NOT NULL THEN COALESCE(a.bpm, t.bpm) ELSE COALESCE(t.bpm, a.bpm) END)";

    private static readonly Dictionary<string, string> TextColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["title"] = "title",
        ["artist"] = "artist",
        ["album"] = "album",
        ["genre"] = "genre",
        ["comment"] = "comment",
        ["folder"] = "folder",
    };

    private readonly List<string> _conditions = [];
    private readonly Dictionary<string, object> _parameters = [];
    private readonly List<string> _words = [];
    private readonly DateTime _nowUtc;

    private SearchQuery(DateTime nowUtc) => _nowUtc = nowUtc;

    /// <summary>SQL conditions to AND together; they refer to the tracks (t), analysis (a), BPM range (r), marks (m) and plays (lp).</summary>
    public IReadOnlyList<string> Conditions => _conditions;

    public IReadOnlyDictionary<string, object> Parameters => _parameters;

    /// <param name="nowUtc">For <c>played:</c> filters; defaults to now.</param>
    public static SearchQuery Parse(string? text, DateTime? nowUtc = null)
    {
        var query = new SearchQuery(nowUtc ?? DateTime.UtcNow);
        foreach (string token in Tokenize(text ?? ""))
            query.Add(token);
        if (LibraryDatabase.BuildMatchExpression(string.Join(' ', query._words)) is { } match)
            query.Match(match, negated: false);
        return query;
    }

    private void Add(string token)
    {
        bool negated = token.Length > 1 && token[0] == '-';
        if (negated)
            token = token[1..];

        int colon = token.IndexOf(':');
        if (colon <= 0)
        {
            AddWords(token, negated);
            return;
        }
        string field = token[..colon], value = Unquote(token[(colon + 1)..]);
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
            var f when TextColumns.TryGetValue(f, out string? column) => TextCondition(column, value, negated),
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

    private static bool IsKnownField(string field) =>
        TextColumns.ContainsKey(field) || field.ToLowerInvariant() is "bpm" or "year" or "rating" or "energy" or "played";

    private void AddWords(string token, bool negated)
    {
        if (!negated)
            _words.Add(token);
        else if (LibraryDatabase.BuildMatchExpression(token) is { } match)
            Match(match, negated: true);
    }

    private void Match(string expression, bool negated)
    {
        string name = Parameter(expression);
        _conditions.Add($"t.id {(negated ? "NOT IN" : "IN")} (SELECT rowid FROM tracks_fts WHERE tracks_fts MATCH {name})");
    }

    /// <summary>Every word must appear in that column (as a prefix). Returns "" once handled.</summary>
    private string? TextCondition(string column, string value, bool negated)
    {
        string? words = LibraryDatabase.BuildMatchExpression(value);
        if (words is null)
            return null;
        // Each term gets the column filter: artist : "count"* artist : "basie"*
        Match(string.Join(' ', words.Split(' ').Select(term => $"{column} : {term}")), negated);
        return "";
    }

    private string? NearBpmCondition(string value)
    {
        if (!TryNumber(value, out double bpm) || bpm <= 0)
            return null;
        return $"{BpmSql} BETWEEN {Parameter(bpm * (1 - NearBpm))} AND {Parameter(bpm * (1 + NearBpm))}";
    }

    /// <summary>
    /// "140", "130-150", "&gt;200", "&lt;=1945". With <paramref name="roundExact"/>, a single
    /// number matches anything that rounds to it (a BPM of 139.6 is "140").
    /// </summary>
    private string? NumberCondition(string sql, string value, bool roundExact)
    {
        foreach (string op in new[] { ">=", "<=", ">", "<" })
        {
            if (value.StartsWith(op, StringComparison.Ordinal))
                return TryNumber(value[op.Length..], out double limit) ? $"{sql} {op} {Parameter(limit)}" : null;
        }
        int dash = value.Length > 1 ? value.IndexOf('-', 1) : -1;   // not a leading minus
        if (dash > 0)
        {
            if (!TryNumber(value[..dash], out double low) || !TryNumber(value[(dash + 1)..], out double high))
                return null;
            if (low > high)
                (low, high) = (high, low);
            return $"{sql} BETWEEN {Parameter(low)} AND {Parameter(high)}";
        }
        if (!TryNumber(value, out double exact))
            return null;
        return roundExact
            ? $"({sql} >= {Parameter(exact - 0.5)} AND {sql} < {Parameter(exact + 0.5)})"
            : $"{sql} = {Parameter(exact)}";
    }

    private string? RatingCondition(string value)
    {
        Rating? rating = value.ToLowerInvariant() switch
        {
            "yes" or "y" or "loved" or "love" or "heart" or "♥" or "1" => Rating.Loved,
            "no" or "n" or "rejected" or "broken" or "-1" => Rating.Rejected,
            "none" or "unrated" or "0" => Rating.Unrated,
            _ => null,
        };
        return rating is null ? null : $"COALESCE(m.rating, 0) = {(int)rating}";
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
        return NumberCondition("COALESCE(m.energy, 0)", number, roundExact: false);
    }

    /// <summary>"never", "yes", or a number of days: played within that many days.</summary>
    private string? PlayedCondition(string value)
    {
        switch (value.ToLowerInvariant())
        {
            case "never" or "no" or "none":
                return "lp.last_played IS NULL";
            case "yes" or "ever" or "y":
                return "lp.last_played IS NOT NULL";
        }
        if (!TryNumber(value, out double days) || days <= 0)
            return null;
        return $"lp.last_played >= {Parameter((_nowUtc - TimeSpan.FromDays(days)).Ticks)}";
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
    private static IEnumerable<string> Tokenize(string text)
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
