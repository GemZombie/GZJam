using System.Globalization;

namespace GZJam.Library;

/// <summary>
/// The tags a DJ can edit in a song's info (#129), by name: GZJam's own fields (title, artist,
/// BPM...) and any other tag (<see cref="ForTag"/>). An edit's value is text, as typed: null takes
/// the tag off. Numbers (year, track, BPM) are kept as invariant text ("1957", "156.5").
/// </summary>
public static class SongFields
{
    public const string Title = "title", Artist = "artist", Album = "album", AlbumArtist = "album_artist", Genre = "genre",
        Year = "year", Track = "track", Bpm = "bpm", Grouping = "grouping", Comment = "comment";

    private const string TagPrefix = "tag:";

    /// <summary>GZJam's own fields, in the order a song's info shows them.</summary>
    public static IReadOnlyList<string> Usual { get; } = [Title, Artist, Album, AlbumArtist, Genre, Year, Track, Bpm, Grouping, Comment];

    /// <summary>The field for one of a song's other tags (#119), by the name it's shown with ("Composer", "Dance").</summary>
    public static string ForTag(string name) => TagPrefix + name;

    /// <summary>The other tag's name, if <paramref name="field"/> is one.</summary>
    public static string? TagName(string field) => field.StartsWith(TagPrefix, StringComparison.Ordinal) ? field[TagPrefix.Length..] : null;

    /// <summary>Whether two fields are the same tag: other tags by their <see cref="SongTag.Key"/> ("Dance" and "DANCE" are one).</summary>
    public static bool Same(string a, string b) =>
        a == b || TagName(a) is { } x && TagName(b) is { } y && SongTags.KeyOf(x) == SongTags.KeyOf(y);

    /// <summary>What a field is called, for the DJ: "Album artist", "Dance".</summary>
    public static string Describe(string field) => field switch
    {
        Title => "Title",
        Artist => "Artist",
        Album => "Album",
        AlbumArtist => "Album artist",
        Genre => "Genre",
        Year => "Year",
        Track => "Track",
        Bpm => "BPM",
        Grouping => "Grouping",
        Comment => "Comment",
        _ => TagName(field) ?? field,
    };

    /// <summary>A song's value of a field, as text (as an edit has it); null if it has none.</summary>
    public static string? ValueOf(Track track, string field) => field switch
    {
        Title => track.Title,
        Artist => track.Artist,
        Album => track.Album,
        AlbumArtist => track.AlbumArtist,
        Genre => track.Genre,
        Comment => track.Comment,
        Grouping => track.Grouping,
        Year => track.Year?.ToString(CultureInfo.InvariantCulture),
        Track => track.TrackNumber?.ToString(CultureInfo.InvariantCulture),
        Bpm => track.TagBpm is { } bpm ? BpmText(bpm) : null,
        _ => TagName(field) is { } name ? track.Tag(SongTags.KeyOf(name)) : null,
    };

    /// <summary>A BPM as an edit keeps it, and as it's written to a file: "156.5".</summary>
    public static string BpmText(double bpm) => Math.Round(bpm, 2).ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>A year or track number; null for none (or nonsense).</summary>
    public static int? ParseWhole(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0 ? n : null;

    /// <summary>A BPM; null for none (or nonsense).</summary>
    public static double? ParseBpm(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double bpm) && bpm > 0 && double.IsFinite(bpm) ? bpm : null;
}
