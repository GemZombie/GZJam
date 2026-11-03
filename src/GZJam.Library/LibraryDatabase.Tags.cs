namespace GZJam.Library;

/// <summary>The song files' other tags (#119), as found in the library: for the column menu, filters and suggestions.</summary>
public sealed partial class LibraryDatabase
{
    private volatile IReadOnlyList<TagName>? _tagNames;

    /// <summary>
    /// Every other tag the library's songs have, most songs first, each by the name most of them
    /// give it. Kept until the songs change (a scan, a folder removed).
    /// </summary>
    public IReadOnlyList<TagName> GetTagNames()
    {
        if (_tagNames is { } known)
            return known;
        var counts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        using (var c = Open())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT tags FROM tracks WHERE tags IS NOT NULL;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                foreach (SongTag tag in SongTags.Decode(r.GetString(0)))
                {
                    if (!counts.TryGetValue(tag.Key, out var names))
                        counts[tag.Key] = names = new Dictionary<string, int>(StringComparer.Ordinal);
                    names[tag.Name] = names.GetValueOrDefault(tag.Name) + 1;
                }
            }
        }
        var found = counts
            .Select(p => new TagName(p.Key, p.Value.MaxBy(n => n.Value).Key, p.Value.Values.Sum()))
            .OrderByDescending(t => t.Count).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return _tagNames = found;
    }

    private void ForgetTagNames() => _tagNames = null;

    /// <summary>The tags a search can filter on, looked up only when it might have a filter.</summary>
    private IReadOnlySet<string>? TagKeys(string? text) =>
        text?.Contains(':') == true ? GetTagNames().Select(t => t.Key).ToHashSet(StringComparer.Ordinal) : null;

    /// <summary>
    /// Every value of the tag with <paramref name="key"/> in the library, with how many songs have
    /// it, most first: for the suggestions after <c>dance:</c>. Several values in one ("Waltz;
    /// Foxtrot") count as each.
    /// </summary>
    public IReadOnlyList<ValueCount> GetTagValues(string key)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using (var c = Open())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT tags FROM tracks WHERE tags IS NOT NULL;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (SongTags.ValueOf(r.GetString(0), key) is not { } value)
                    continue;
                foreach (string part in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    counts[part] = counts.GetValueOrDefault(part) + 1;
            }
        }
        return counts.Select(p => new ValueCount(p.Key, p.Value))
            .OrderByDescending(v => v.Count).ThenBy(v => v.Value, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
