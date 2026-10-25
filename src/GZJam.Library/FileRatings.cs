using System.Collections.Concurrent;
using System.Globalization;

namespace GZJam.Library;

/// <summary>
/// Star ratings that other players keep in the song files themselves (MediaMonkey, foobar2000,
/// Windows Media Player and others), brought over as a one-time import rather than on every
/// scan: a rating set in GZJam always stands, and only unrated songs are filled in. Planned first
/// (reading the files takes a while in a big library), then applied. The files are only read.
/// <list type="bullet">
/// <item>MP3 (and WAV / AIFF with an ID3 tag): the <c>POPM</c> frame, 0-255, or a
/// <c>TXXX:RATING</c> / <c>TXXX:FMPS_Rating</c> frame.</item>
/// <item>FLAC, Ogg Vorbis and Opus: a <c>RATING</c> or <c>FMPS_RATING</c> comment.</item>
/// <item>AAC / Apple Lossless: iTunes keeps ratings in its own library, not the file: nothing to read.</item>
/// </list>
/// Mapped as the Mixxx import does (<see cref="RatingFromStars"/>).
/// </summary>
public sealed class FileRatings
{
    private static readonly HashSet<string> Readable = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".flac", ".ogg", ".oga", ".opus", ".wav", ".aif", ".aiff" };

    private readonly List<(string Path, Rating Rating)> _ratings = [];

    private FileRatings()
    {
    }

    /// <summary>Unrated songs whose files were read.</summary>
    public int SongsRead { get; private init; }

    /// <summary>Of those, songs given 4 or 5 stars: they become ♥.</summary>
    public int Loved => _ratings.Count(r => r.Rating == Rating.Loved);

    /// <summary>Songs given 1 or 2 stars: they get a broken heart.</summary>
    public int Rejected => _ratings.Count(r => r.Rating == Rating.Rejected);

    /// <summary>Songs given 3 stars, which stay unrated.</summary>
    public int ThreeStars { get; private init; }

    public int Count => _ratings.Count;

    /// <summary>The Mixxx import's mapping: 4-5 stars ♥, 1-2 a broken heart, 3 (or none) unrated.</summary>
    public static Rating RatingFromStars(int stars) => stars switch { >= 4 => Rating.Loved, 1 or 2 => Rating.Rejected, _ => Rating.Unrated };

    /// <summary>
    /// Reads the files of GZJam's unrated songs. <paramref name="progress"/> hears (songs read,
    /// songs to read) now and then.
    /// </summary>
    public static FileRatings Plan(LibraryDatabase gz, IProgress<(int Done, int Total)>? progress = null, CancellationToken cancel = default)
    {
        var songs = gz.Search(null)
            .Where(t => t.Rating == Rating.Unrated && Readable.Contains(Path.GetExtension(t.Path)))
            .Select(t => t.Path)
            .ToList();
        var found = new ConcurrentBag<(string, int)>();
        int done = 0;
        progress?.Report((0, songs.Count));
        // A few at once: most of the time goes on waiting for the disk.
        Parallel.ForEach(songs, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancel }, path =>
        {
            if (Read(path) is { } stars)
                found.Add((path, stars));
            int n = Interlocked.Increment(ref done);
            if (n % 250 == 0 || n == songs.Count)
                progress?.Report((n, songs.Count));
        });

        var plan = new FileRatings
        {
            SongsRead = songs.Count,
            ThreeStars = found.Count(f => f.Item2 == 3),
        };
        foreach (var (path, stars) in found.OrderBy(f => f.Item1, StringComparer.Ordinal))
        {
            if (RatingFromStars(stars) is var rating and not Rating.Unrated)
                plan._ratings.Add((path, rating));
        }
        return plan;
    }

    /// <summary>Gives the songs their hearts. Returns how many; one rated in GZJam since the plan keeps its rating.</summary>
    public int Apply(LibraryDatabase gz)
    {
        var still = _ratings.Where(r => gz.GetMarks(r.Path).Rating == Rating.Unrated).ToList();
        foreach (var group in still.GroupBy(r => r.Rating))
            gz.SetRating(group.Select(r => r.Path), group.Key);
        return still.Count;
    }

    /// <summary>The stars (1-5) a file's tags give it, or null for none (or unreadable).</summary>
    public static int? Read(string path)
    {
        try
        {
            using var file = TagLib.File.Create(path, TagLib.ReadStyle.None);
            if (file.GetTag(TagLib.TagTypes.Id3v2) is TagLib.Id3v2.Tag id3)
            {
                foreach (var popm in id3.GetFrames<TagLib.Id3v2.PopularimeterFrame>())
                {
                    if (StarsFromPopm(popm.Rating) is { } stars)
                        return stars;
                }
                foreach (var txxx in id3.GetFrames<TagLib.Id3v2.UserTextInformationFrame>())
                {
                    if (StarsFromText(txxx.Description, txxx.Text.FirstOrDefault()) is { } stars)
                        return stars;
                }
            }
            if (file.GetTag(TagLib.TagTypes.Xiph) is TagLib.Ogg.XiphComment xiph)
            {
                foreach (string field in new[] { "RATING", "FMPS_RATING" })
                {
                    if (StarsFromText(field, xiph.GetFirstField(field)) is { } stars)
                        return stars;
                }
            }
            return null;
        }
        catch (Exception ex) when (ex is TagLib.CorruptFileException or TagLib.UnsupportedFormatException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// A POPM rating, 1-255 (0 is "not rated"). Players use their own steps: Windows Media Player
    /// and MediaMonkey write 1 / 64 / 128 / 196 / 255 for 1-5 stars, others spread them evenly.
    /// </summary>
    internal static int? StarsFromPopm(byte rating) => rating switch
    {
        0 => null,
        < 32 => 1,
        < 96 => 2,
        < 160 => 3,
        < 224 => 4,
        _ => 5,
    };

    /// <summary>
    /// A rating written as text, in a field named <c>RATING</c> or <c>FMPS_RATING</c> (any case).
    /// FMPS is 0.0-1.0. RATING depends on the player: 1-5 stars, 0-100 (MediaMonkey), or 0.0-1.0.
    /// </summary>
    internal static int? StarsFromText(string? field, string? text)
    {
        if (text is null || !double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || value <= 0)
            return null;
        double stars;
        if (string.Equals(field, "FMPS_RATING", StringComparison.OrdinalIgnoreCase))
            stars = value <= 1 ? value * 5 : double.NaN;
        else if (string.Equals(field, "RATING", StringComparison.OrdinalIgnoreCase))
            stars = value < 1 || value <= 1 && text.Contains('.') ? value * 5   // "0.8", "1.0"
                : value <= 5 ? value
                : value <= 100 ? value / 20
                : double.NaN;
        else
            return null;
        return double.IsNaN(stars) ? null : Math.Clamp((int)Math.Round(stars, MidpointRounding.AwayFromZero), 1, 5);
    }
}
