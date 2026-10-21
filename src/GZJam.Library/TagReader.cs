using System.Globalization;

namespace GZJam.Library;

/// <summary>Reads tags and duration from an audio file with TagLib#.</summary>
internal static class TagReader
{
    /// <summary>
    /// Builds a <see cref="Track"/> for a file. Files with unreadable or missing tags still get a
    /// track (titled by file name) so they're never silently missing from the library.
    /// </summary>
    public static Track Read(long rootId, string rootPath, FileInfo file)
    {
        string folder = RelativeFolder(rootPath, file.DirectoryName!);
        try
        {
            using var tagFile = TagLib.File.Create(file.FullName);
            TagLib.Tag tag = tagFile.Tag;
            return new Track
            {
                RootId = rootId,
                Path = file.FullName,
                Folder = folder,
                FileName = file.Name,
                FileSize = file.Length,
                ModifiedUtc = file.LastWriteTimeUtc,
                Title = Clean(tag.Title),
                Artist = Clean(string.Join("; ", tag.Performers)),
                Album = Clean(tag.Album),
                AlbumArtist = Clean(string.Join("; ", tag.AlbumArtists)),
                Genre = Clean(string.Join("; ", tag.Genres)),
                Comment = ReadComment(tagFile),
                Year = tag.Year > 0 ? (int)tag.Year : null,
                TrackNumber = tag.Track > 0 ? (int)tag.Track : null,
                TagBpm = ReadBpm(tagFile),
                Duration = tagFile.Properties?.Duration ?? TimeSpan.Zero,
            };
        }
        catch (Exception ex) when (ex is TagLib.CorruptFileException or TagLib.UnsupportedFormatException or IOException or UnauthorizedAccessException)
        {
            return new Track
            {
                RootId = rootId,
                Path = file.FullName,
                Folder = folder,
                FileName = file.Name,
                FileSize = file.Length,
                ModifiedUtc = file.LastWriteTimeUtc,
            };
        }
    }

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
                    return NormalizeBpm(bpm);
            }
        }
        return file.Tag.BeatsPerMinute > 0 ? NormalizeBpm(file.Tag.BeatsPerMinute) : null;
    }

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
