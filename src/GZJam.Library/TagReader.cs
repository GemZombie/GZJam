using System.Globalization;

namespace GZJam.Library;

/// <summary>Reads tags and duration from an audio file with TagLib#.</summary>
internal static class TagReader
{
    /// <summary>
    /// Which way of reading tags a song was read with. Raise it whenever tags are read better (a
    /// new field, a fix like BPMs stored x10): the next scan reads every song again, rather than
    /// only the files that changed on disk. 1: BPMs stored x10 or x100 scaled back. 2: Grouping.
    /// 3: tag BPMs too slow to be real (0.4, 1) ignored. 4: tags of a file whose audio TagLib
    /// can't find, behind a damaged tag (#220).
    /// </summary>
    public const int Version = 4;

    /// <summary>
    /// The slowest tag BPM taken as real. Below it, a tag is junk left by some tool ("1", "0.4"),
    /// and the BPM GZJam detects is shown instead (#206).
    /// </summary>
    internal const double MinTagBpm = 40;

    /// <summary>
    /// Builds a <see cref="Track"/> for a file. Files with unreadable or missing tags still get a
    /// track (titled by file name) so they're never silently missing from the library.
    /// </summary>
    public static Track Read(long rootId, string rootPath, FileInfo file)
    {
        string folder = RelativeFolder(rootPath, file.DirectoryName!);
        Track Build(TagLib.File? tagFile, TimeSpan duration)
        {
            TagLib.Tag? tag = tagFile?.Tag;
            return new Track
            {
                RootId = rootId,
                Path = file.FullName,
                Folder = folder,
                FileName = file.Name,
                FileSize = file.Length,
                ModifiedUtc = file.LastWriteTimeUtc,
                Title = Clean(tag?.Title),
                Artist = Clean(tag is null ? null : string.Join("; ", tag.Performers)),
                Album = Clean(tag?.Album),
                AlbumArtist = Clean(tag is null ? null : string.Join("; ", tag.AlbumArtists)),
                Genre = Clean(tag is null ? null : string.Join("; ", tag.Genres)),
                Comment = tagFile is null ? null : ReadComment(tagFile),
                Grouping = tagFile is null ? null : ReadGrouping(tagFile),
                Year = tag?.Year > 0 ? (int)tag.Year : null,
                TrackNumber = tag?.Track > 0 ? (int)tag.Track : null,
                TagBpm = tagFile is null ? null : ReadBpm(tagFile),
                Duration = duration,
            };
        }

        try
        {
            using var tagFile = TagLib.File.Create(file.FullName);
            return Build(tagFile, tagFile.Properties?.Duration ?? TimeSpan.Zero);
        }
        catch (TagLib.CorruptFileException)
        {
            // TagLib didn't find the audio near the tags (a damaged tag in front of it, #220): the
            // tags without it, and the length they give, if any. The analysis finds the audio.
            try
            {
                using var tagFile = TagLib.File.Create(file.FullName, TagLib.ReadStyle.None);
                return Build(tagFile, TagLength(tagFile));
            }
            catch (Exception ex) when (Unreadable(ex))
            {
                return Build(null, TimeSpan.Zero);
            }
        }
        catch (Exception ex) when (Unreadable(ex))
        {
            return Build(null, TimeSpan.Zero);
        }
    }

    private static bool Unreadable(Exception ex) =>
        ex is TagLib.CorruptFileException or TagLib.UnsupportedFormatException or IOException or UnauthorizedAccessException;

    /// <summary>The length an ID3 tag says the song is (TLEN, in milliseconds), or zero.</summary>
    private static TimeSpan TagLength(TagLib.File file) =>
        file.GetTag(TagLib.TagTypes.Id3v2) is TagLib.Id3v2.Tag id3
        && id3.GetFrames<TagLib.Id3v2.TextInformationFrame>("TLEN").SelectMany(f => f.Text).FirstOrDefault() is { } text
        && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ms) && ms > 0
            ? TimeSpan.FromMilliseconds(ms)
            : TimeSpan.Zero;

    /// <summary>
    /// ID3 TBPM is text and DJ tools often write decimals ("156.5"), which TagLib's integer
    /// BeatsPerMinute drops. Read the raw frame first.
    /// </summary>
    private static double? ReadBpm(TagLib.File file)
    {
        if (file.GetTag(TagLib.TagTypes.Id3v2) is TagLib.Id3v2.Tag id3)
        {
            foreach (var frame in id3.GetFrames<TagLib.Id3v2.TextInformationFrame>("TBPM"))
            {
                string? text = frame.Text.FirstOrDefault();
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double bpm) && bpm > 0)
                    return Plausible(NormalizeBpm(bpm));
            }
        }
        return file.Tag.BeatsPerMinute > 0 ? Plausible(NormalizeBpm(file.Tag.BeatsPerMinute)) : null;
    }

    private static double? Plausible(double bpm) => bpm >= MinTagBpm ? bpm : null;

    /// <summary>Some taggers store BPM x 10 ("1320" for 132.0) or x 100 ("13200"). Scale those back.</summary>
    internal static double NormalizeBpm(double bpm) =>
        bpm <= 400 ? bpm
        : bpm / 10 is >= 40 and <= 400 ? bpm / 10
        : bpm / 100 is >= 30 and <= 400 ? bpm / 100
        : bpm;

    /// <summary>
    /// The user-visible comment. ID3 files often carry extra COMM frames from iTunes
    /// (iTunNORM volume data, iTunSMPB gapless info), which TagLib may return as "the" comment.
    /// </summary>
    private static string? ReadComment(TagLib.File file)
    {
        if (file.GetTag(TagLib.TagTypes.Id3v2) is TagLib.Id3v2.Tag id3)
        {
            var candidates = id3.GetFrames<TagLib.Id3v2.CommentsFrame>()
                .Where(f => !f.Description.StartsWith("iTun", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.Description.Length > 0)   // prefer the plain, undescribed comment
                .Select(f => Clean(f.Text))
                .Where(text => text is not null && !IsJunkComment(text));
            if (candidates.FirstOrDefault() is { } comment)
                return comment;
            if (file.TagTypes == TagLib.TagTypes.Id3v2 || file.GetTag(TagLib.TagTypes.Id3v1) is null)
                return null;
        }
        string? fallback = Clean(file.Tag.Comment);
        return fallback is null || IsJunkComment(fallback) ? null : fallback;
    }

    /// <summary>
    /// Grouping: in an MP3, iTunes 12.5.2 and later (and Music) write it to a GRP1 frame, which
    /// TagLib# doesn't know; older iTunes and most other apps use TIT1, which TagLib#'s Grouping
    /// reads, as it does M4A's ©grp and the GROUPING comment in FLAC, Ogg and Opus. (Newer iTunes
    /// uses TIT1 for "Work", but its files have a GRP1 too.)
    /// </summary>
    private static string? ReadGrouping(TagLib.File file)
    {
        if (file.GetTag(TagLib.TagTypes.Id3v2) is TagLib.Id3v2.Tag id3)
        {
            foreach (TagLib.Id3v2.Frame frame in id3.GetFrames("GRP1"))
            {
                string? text = frame switch
                {
                    TagLib.Id3v2.TextInformationFrame known => string.Join("; ", known.Text),
                    TagLib.Id3v2.UnknownFrame unknown => FrameText(unknown.Data),
                    _ => null,
                };
                if (Clean(text) is { } grouping)
                    return grouping;
            }
        }
        return Clean(file.Tag.Grouping);
    }

    /// <summary>An ID3v2 text frame's data: its encoding byte, then the text (a NUL between several values).</summary>
    internal static string? FrameText(TagLib.ByteVector data)
    {
        if (data.Count < 2)
            return null;
        TagLib.StringType encoding = data[0] switch
        {
            1 => TagLib.StringType.UTF16,
            2 => TagLib.StringType.UTF16BE,
            3 => TagLib.StringType.UTF8,
            _ => TagLib.StringType.Latin1,
        };
        string text = data.ToString(encoding, 1, data.Count - 1);
        string[] values = text.Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return values.Length == 0 ? null : string.Join("; ", values.Select(v => v.TrimStart('\uFEFF')));
    }

    /// <summary>Leftovers from broken taggers: iTunes frame names and bare language codes.</summary>
    internal static bool IsJunkComment(string text) =>
        text.StartsWith("iTun", StringComparison.OrdinalIgnoreCase) || text is "eng" or "XXX";

    /// <summary>'/'-separated path of <paramref name="directory"/> relative to the root; empty for the root itself.</summary>
    internal static string RelativeFolder(string rootPath, string directory)
    {
        string relative = Path.GetRelativePath(rootPath, directory);
        return relative == "." ? "" : relative.Replace('\\', '/');
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        // Some taggers pad with NULs or whitespace.
        return value.Trim().TrimEnd('\0');
    }
}
