namespace GZJam.Library;

/// <summary>A value found in the library (an artist, a genre, a decade) and how many songs have it.</summary>
public sealed record ValueCount(string Value, int Count);

public sealed partial class LibraryDatabase
{
    /// <summary>The tag fields the search box suggests values for, after <c>artist:</c> and the like (#241).</summary>
    public static readonly IReadOnlySet<string> SuggestedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "artist", "album", "genre", "grouping",
    };

    /// <summary>
    /// Every value of a tag field in the library, with how many songs have it, most first. Several
    /// artists or genres in one tag ("Count Basie; Joe Williams") count as each.
    /// </summary>
    public IReadOnlyList<ValueCount> GetFieldValues(string field)
    {
        if (!SuggestedFields.Contains(field))
            throw new ArgumentException($"No values are suggested for {field}.", nameof(field));
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT {field.ToLowerInvariant()}, COUNT(*) FROM tracks WHERE {field} IS NOT NULL GROUP BY {field};";
        using var r = cmd.ExecuteReader();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        while (r.Read())
        {
            foreach (string value in r.GetString(0).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                counts[value] = counts.GetValueOrDefault(value) + r.GetInt32(1);
        }
        return counts.Select(p => new ValueCount(p.Key, p.Value))
            .OrderByDescending(v => v.Count).ThenBy(v => v.Value, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The decades the library's songs come from ("1930"), oldest first, with how many songs.</summary>
    public IReadOnlyList<ValueCount> GetDecades()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT year / 10 * 10 AS decade, COUNT(*) FROM tracks WHERE year > 0 GROUP BY decade ORDER BY decade;";
        using var r = cmd.ExecuteReader();
        var decades = new List<ValueCount>();
        while (r.Read())
            decades.Add(new ValueCount(r.GetInt64(0).ToString(System.Globalization.CultureInfo.InvariantCulture), r.GetInt32(1)));
        return decades;
    }
}
