using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GZJam.Library;

/// <summary>What a file name says about its song: any of its artist, title, year, track number and album.</summary>
public sealed record NameTags(string? Artist, string? Title, int? Year = null, int? Track = null, string? Album = null)
{
    public bool IsEmpty => Artist is null && Title is null && Year is null && Track is null && Album is null;
}

/// <summary>
/// Tags from file names (#273), for old transfers and rips whose tags are empty but whose names
/// are good: <c>1924 Ma Rainey - Lucky Rock Blues.mp3</c>, <c>Count Basie - 04 - Jumpin' at the
/// Woodside.flac</c>. A pattern says how a name is laid out, with <c>{artist}</c>, <c>{title}</c>,
/// <c>{year}</c>, <c>{track}</c>, <c>{album}</c> and <c>{skip}</c> (anything, left out) between
/// the words and dashes as they are. Underscores count as spaces.
/// </summary>
public static class FileNameTags
{
    /// <summary>The usual layouts, the more particular first: the first a name fits is the one shown for it.</summary>
    public static IReadOnlyList<string> Patterns { get; } =
    [
        "{track} - {artist} - {title}",
        "{track}. {artist} - {title}",
        "{track} {artist} - {title}",
        "{year} {artist} - {title}",
        "{year} - {artist} - {title}",
        "{artist} - {year} - {title}",
        "{artist} - {track} - {title}",
        "{track} - {title}",
        "{track}. {title}",
        "{track} {title}",
        "{artist} - {title}",
    ];

    private static readonly Dictionary<string, Regex?> Compiled = [];
    private static readonly Regex Token = new(@"\{(artist|title|year|track|album|skip)\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>A name without its folder and type, its underscores as spaces and spaces single.</summary>
    public static string Stem(string fileName)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName.Replace('\\', '/').Split('/')[^1]);
        stem = stem.Replace('_', ' ').Replace('–', '-').Replace('—', '-');
        return Regex.Replace(stem, @"\s+", " ").Trim();
    }

    /// <summary>What the name says, by the first of <see cref="Patterns"/> it fits; else the name is the title.</summary>
    public static NameTags Guess(string fileName)
    {
        foreach (string pattern in Patterns)
            if (Match(pattern, fileName) is { } tags)
                return tags;
        string stem = Stem(fileName);
        return new NameTags(null, stem.Length > 0 ? stem : null);
    }

    /// <summary>
    /// The pattern most of <paramref name="fileNames"/> fit (the more particular when as many fit),
    /// for Fill tags from file name; the plainest when none do.
    /// </summary>
    public static string BestPattern(IEnumerable<string> fileNames)
    {
        var names = fileNames.ToList();
        string best = Patterns[^1];
        int most = 0;
        foreach (string pattern in Patterns)
        {
            int fit = names.Count(n => Match(pattern, n) is not null);
            if (fit > most)
            {
                best = pattern;
                most = fit;
            }
        }
        return best;
    }

    /// <summary>What the name says by <paramref name="pattern"/>, or null if it doesn't fit (or the pattern has nothing to fill).</summary>
    public static NameTags? Match(string pattern, string fileName)
    {
        if (RegexFor(pattern) is not { } regex)
            return null;
        Match m = regex.Match(Stem(fileName));
        if (!m.Success)
            return null;
        string? Text(string name) => m.Groups[name].Success && m.Groups[name].Value.Trim() is { Length: > 0 } v ? v : null;
        int? year = Text("year") is { } y ? int.Parse(y, CultureInfo.InvariantCulture) : null;
        if (year is < 1890 or > 2039)
            return null;
        int? track = Text("track") is { } t ? int.Parse(t, CultureInfo.InvariantCulture) : null;
        if (track is 0)
            return null;
        var tags = new NameTags(Text("artist"), Text("title"), year, track, Text("album"));
        return tags.IsEmpty ? null : tags;
    }

    /// <summary>Whether a pattern can be used: it has something to fill, and each part once.</summary>
    public static bool IsValid(string pattern) => RegexFor(pattern) is not null;

    private static Regex? RegexFor(string pattern)
    {
        lock (Compiled)
        {
            if (Compiled.TryGetValue(pattern, out Regex? known))
                return known;
            Regex? made = Build(pattern);
            Compiled[pattern] = made;
            return made;
        }
    }

    private static Regex? Build(string pattern)
    {
        string text = Regex.Replace(pattern.Replace('_', ' ').Replace('–', '-'), @"\s+", " ").Trim();
        var matches = Token.Matches(text);
        if (matches.Count == 0)
            return null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var regex = new StringBuilder("^");
        int at = 0;
        for (int i = 0; i < matches.Count; i++)
        {
            Match token = matches[i];
            regex.Append(Literal(text[at..token.Index]));
            string name = token.Groups[1].Value.ToLowerInvariant();
            if (name != "skip" && !seen.Add(name))
                return null;   // {artist} twice
            int next = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            string after = text[(token.Index + token.Length)..next];
            regex.Append(name switch
            {
                "year" => @"(?<year>\d{4})",
                // "04 Title": two or three digits, so a title that starts with a number ("7 Come 11") stays whole
                "track" => after == " " ? @"(?<track>\d{2,3})" : @"(?<track>\d{1,3})",
                "skip" => @".+?",
                _ when i == matches.Count - 1 && after.Length == 0 => $"(?<{name}>.+)",
                _ => $"(?<{name}>.+?)",
            });
            at = token.Index + token.Length;
        }
        regex.Append(Literal(text[at..])).Append('$');
        if (seen.Count == 0)
            return null;   // only {skip}
        return new Regex(regex.ToString(), RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    /// <summary>The words and dashes between the parts, as they are; a space matches one or more.</summary>
    private static string Literal(string text) => Regex.Escape(text).Replace(@"\ ", @"\s+");
}
