namespace GZJam.Library;

/// <summary>
/// One line in the list under the search box. <paramref name="Insert"/> replaces the word being
/// typed when it's chosen; a line without one is a hint (how to write a BPM range), not a choice.
/// </summary>
public sealed record SearchSuggestion(string Text, string? Detail = null, int? Count = null, string? Insert = null);

/// <summary>Suggestions for the word being typed, which runs from <paramref name="Start"/> for <paramref name="Length"/> characters.</summary>
public sealed record SearchSuggestionList(IReadOnlyList<SearchSuggestion> Items, int Start, int Length);

/// <summary>
/// Suggestions as the DJ types in the search box (#241), so the filters needn't be memorized:
/// filter names (<c>ar</c> offers <c>artist:</c>), then, after one, what's in the library (after
/// <c>label:</c> the labels, after <c>genre:</c> the genres) or the filter's choices in words.
/// </summary>
public static class SearchSuggestions
{
    /// <summary>The most values listed at once: enough to choose from, short enough to read at a glance.</summary>
    public const int MaxValues = 8;

    /// <summary>Letters typed before filter names are offered: one would pop the list up at nearly every word.</summary>
    public const int MinNameLength = 2;

    private sealed record Filter(string Name, string Detail);

    private static readonly Filter[] Filters =
    [
        new("artist", "only the artist"),
        new("title", "only the title"),
        new("album", "only the album"),
        new("genre", "only the genre"),
        new("grouping", "only the Grouping"),
        new("comment", "only the comment"),
        new("folder", "only folder names"),
        new("file", "only the file name"),
        new("bpm", "a BPM, or a range"),
        new("year", "the year, or a range"),
        new("label", "a label"),
        new("rating", "hearts"),
        new("energy", "energy"),
        new("played", "when the room heard it"),
    ];

    private static readonly Dictionary<string, SearchSuggestion[]> Choices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["energy"] = [new("high"), new("medium"), new("low"), new("none", "not marked")],
        ["rating"] = [new("yes", "♥"), new("no", "broken heart"), new("none", "not rated")],
        ["played"] = [new("7", "in the last 7 days"), new("30", "in the last 30 days"), new("never"), new("yes", "ever")],
    };

    private static readonly Dictionary<string, string> Hints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bpm"] = "bpm:140   bpm:130-150   bpm:>200",
        ["~bpm"] = "~bpm:140 near 140   ~bpm:140/5 within 5%",
        ["year"] = "year:1938   year:1935-1940   year:<1950",
    };

    /// <summary>
    /// What to suggest for the word that ends at <paramref name="caret"/> in <paramref name="text"/>,
    /// or null for nothing (the list stays closed). <paramref name="values"/> gives what the library
    /// holds for a field ("label", "artist", "genre"...), most useful first.
    /// </summary>
    public static SearchSuggestionList? For(string text, int caret, Func<string, IReadOnlyList<ValueCount>> values)
    {
        if (caret < 0 || caret > text.Length || (caret < text.Length && !char.IsWhiteSpace(text[caret])))
            return null;   // only at the end of a word
        int start = WordStart(text, caret);
        if (start < 0)
            return null;   // inside quotes: a phrase, still being typed
        string word = text[start..caret];
        string prefix = word.StartsWith('-') ? "-" : "";
        string core = word[prefix.Length..];
        if (core.StartsWith('~'))
        {
            prefix += "~";
            core = core[1..];
        }
        if (core.Length == 0 || core.StartsWith('"'))
            return null;

        int colon = core.IndexOf(':');
        var items = colon < 0
            ? FilterNames(core, near: prefix.EndsWith('~'), prefix)
            : Values(core[..colon].ToLowerInvariant(), core[(colon + 1)..], prefix, values);
        return items.Count == 0 ? null : new SearchSuggestionList(items, start, word.Length);
    }

    /// <summary>The search text with <paramref name="choice"/> in place of the word it was for, and where the caret goes.</summary>
    public static (string Text, int Caret) Apply(string text, SearchSuggestionList list, SearchSuggestion choice)
    {
        string insert = choice.Insert ?? "";
        string after = text[(list.Start + list.Length)..];
        if (insert.EndsWith(' ') && after.StartsWith(' '))
            after = after[1..];
        return (text[..list.Start] + insert + after, list.Start + insert.Length);
    }

    /// <summary>Where the word ending at <paramref name="caret"/> starts, or -1 if the caret is inside quotes.</summary>
    private static int WordStart(string text, int caret)
    {
        int start = 0;
        bool quoted = false;
        for (int i = 0; i < caret; i++)
        {
            if (text[i] == '"')
                quoted = !quoted;
            else if (char.IsWhiteSpace(text[i]) && !quoted)
                start = i + 1;
        }
        // An open quote at the start of a value (label:"female) is still the value being typed.
        if (quoted && text.IndexOf(':', start, caret - start) is var colon and > 0 && colon + 1 < caret && text[colon + 1] == '"')
            return start;
        return quoted ? -1 : start;
    }

    private static List<SearchSuggestion> FilterNames(string typed, bool near, string prefix)
    {
        if (typed.Length < MinNameLength && !near)
            return [];
        return Filters
            .Where(f => (!near || f.Name == "bpm") && f.Name.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            .Select(f => near
                ? new SearchSuggestion("~bpm:", "near a BPM, as far as the tempo slider reaches", Insert: prefix + "bpm:")
                : new SearchSuggestion(f.Name + ":", f.Detail, Insert: prefix + f.Name + ":"))
            .ToList();
    }

    private static List<SearchSuggestion> Values(string field, string typed, string prefix, Func<string, IReadOnlyList<ValueCount>> values)
    {
        if (typed.Length > 1 && typed.StartsWith('"') && typed.EndsWith('"'))
            return [];   // a quoted value, finished
        typed = typed.Trim('"');
        string key = prefix.EndsWith('~') ? "~" + field : field;
        if (Hints.TryGetValue(key, out string? hint))
            return typed.Length == 0 ? [new SearchSuggestion(hint)] : [];

        IEnumerable<SearchSuggestion> candidates;
        if (Choices.TryGetValue(field, out var choices))
            candidates = choices;
        else if (field is "label" or "labels")
            candidates = values("label").Select(v => new SearchSuggestion(v.Value, Count: v.Count))
                .Append(new SearchSuggestion("none", "songs without a label"));
        else if (LibraryDatabase.SuggestedFields.Contains(field))
            candidates = values(field).Select(v => new SearchSuggestion(v.Value, Count: v.Count));
        else
            return [];

        var matching = candidates.Where(c => StartsAWord(c.Text, typed)).ToList();
        // The values the typing starts first, then those with a later word that starts with it.
        return matching.Where(c => c.Text.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            .Concat(matching.Where(c => !c.Text.StartsWith(typed, StringComparison.OrdinalIgnoreCase)))
            .Where(c => !c.Text.Equals(typed, StringComparison.OrdinalIgnoreCase))   // typed in full already
            .Take(MaxValues)
            .Select(c => c with { Insert = $"{prefix}{field}:{Quoted(c.Text)} " })
            .ToList();
    }

    /// <summary>Whether <paramref name="typed"/> starts <paramref name="value"/> or one of its words ("bas" in "Count Basie").</summary>
    private static bool StartsAWord(string value, string typed)
    {
        if (typed.Length == 0)
            return true;
        for (int i = 0; i < value.Length; i++)
        {
            bool wordStart = i == 0 || !char.IsLetterOrDigit(value[i - 1]);
            if (wordStart && string.Compare(value, i, typed, 0, typed.Length, StringComparison.OrdinalIgnoreCase) == 0)
                return true;
        }
        return false;
    }

    private static string Quoted(string value)
    {
        value = value.Replace("\"", "");
        return value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;
    }
}
