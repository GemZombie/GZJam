using System.Globalization;
using System.Text;

namespace GZJam.Library;

/// <summary>A word in the search index, as it's indexed (lowercase, without accents), and how many songs have it.</summary>
public sealed record SearchWord(string Term, int Songs);

/// <summary>
/// "Did you mean...?" for a search that finds nothing because of a typo (#242): each word that no
/// word in the library starts with is swapped for the nearest one that's really there
/// (<c>elington</c> becomes <c>ellington</c>). Searching itself stays exact; only the suggestion forgives.
/// </summary>
public static class SearchSpelling
{
    /// <summary>Words shorter than this aren't corrected: one letter off in two or three is another word.</summary>
    public const int MinLength = 3;

    /// <summary>
    /// The search with its misspelled words corrected, or null if every word is in the library (or
    /// none has a near neighbor). <paramref name="words"/> is the library's index, sorted by term.
    /// Filters, excluded words (<c>-live</c>) and <c>OR</c> are kept as they are.
    /// </summary>
    public static string? Suggest(string text, IReadOnlyList<SearchWord> words)
    {
        var tokens = SearchQuery.Tokenize(text).ToList();
        bool changed = false;
        for (int i = 0; i < tokens.Count; i++)
        {
            string token = tokens[i];
            if (token == SearchQuery.Or || token.StartsWith('-'))
                continue;
            int colon = token.IndexOf(':');
            int start = 0;
            if (!token.StartsWith('"') && colon > 0)
            {
                if (!SearchQuery.IsTextField(token[..colon]))
                    continue;   // bpm:140, rating:yes...
                start = colon + 1;
            }
            var fixedToken = new StringBuilder(token[..start]);
            int at = start;
            while (at < token.Length)
            {
                if (!char.IsLetterOrDigit(token[at]))
                {
                    fixedToken.Append(token[at++]);
                    continue;
                }
                int end = at;
                while (end < token.Length && char.IsLetterOrDigit(token[end]))
                    end++;
                string word = token[at..end];
                string? better = Correct(word, words);
                changed |= better is not null;
                fixedToken.Append(better ?? word);
                at = end;
            }
            tokens[i] = fixedToken.ToString();
        }
        return changed ? string.Join(' ', tokens) : null;
    }

    /// <summary>The library word nearest <paramref name="typed"/>, or null if a word starts with it already, or none is near.</summary>
    internal static string? Correct(string typed, IReadOnlyList<SearchWord> words)
    {
        string word = Indexed(typed);
        if (word.Length < MinLength || word.All(char.IsDigit) || StartsAny(word, words))
            return null;
        int most = word.Length <= 4 ? 1 : 2;
        SearchWord? best = null;
        int bestDistance = int.MaxValue;
        bool bestWhole = false;
        foreach (SearchWord candidate in words)
        {
            string term = candidate.Term;
            if (term.Length < word.Length - most)
                continue;
            // The whole word, or its start (the search matches word starts: "elingt" is "ellingt...").
            int whole = term.Length <= word.Length + most ? Distance(word, term, most) : int.MaxValue;
            int start = int.MaxValue;
            for (int k = Math.Max(1, word.Length - 1); k <= Math.Min(term.Length - 1, word.Length + 1); k++)
                start = Math.Min(start, Distance(word, term[..k], most));
            int distance = Math.Min(whole, start);
            if (distance > most)
                continue;
            bool isWhole = whole == distance;
            if (distance < bestDistance || (distance == bestDistance
                && (isWhole && !bestWhole || isWhole == bestWhole && candidate.Songs > best!.Songs)))
            {
                (best, bestDistance, bestWhole) = (candidate, distance, isWhole);
            }
        }
        return best?.Term;
    }

    /// <summary>A word as the index keeps it: lowercase, without accents (the tokenizer's remove_diacritics).</summary>
    internal static string Indexed(string word)
    {
        var plain = new StringBuilder(word.Length);
        foreach (char c in word.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                plain.Append(char.ToLowerInvariant(c));
        }
        return plain.ToString();
    }

    /// <summary>Whether a library word starts with <paramref name="word"/>: a binary search, the words being sorted.</summary>
    private static bool StartsAny(string word, IReadOnlyList<SearchWord> words)
    {
        int low = 0, high = words.Count;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (string.CompareOrdinal(words[middle].Term, word) < 0)
                low = middle + 1;
            else
                high = middle;
        }
        return low < words.Count && words[low].Term.StartsWith(word, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Damerau-Levenshtein distance (a swapped pair of letters counts once), or more than
    /// <paramref name="most"/> as soon as it's sure to be.
    /// </summary>
    internal static int Distance(string a, string b, int most)
    {
        if (Math.Abs(a.Length - b.Length) > most)
            return most + 1;
        var before = new int[b.Length + 1];
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++)
            previous[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            int rowBest = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                int d = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d = Math.Min(d, before[j - 2] + 1);
                current[j] = d;
                rowBest = Math.Min(rowBest, d);
            }
            if (rowBest > most)
                return most + 1;
            (before, previous, current) = (previous, current, before);
        }
        return previous[b.Length];
    }
}
