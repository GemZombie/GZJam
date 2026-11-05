using TagLib;

namespace GZJam.Library;

/// <summary>
/// Writes the DJ's tag edits into a song file (#129), when they choose to: only the tags, never
/// the audio. GZJam's own fields go where every player reads them (ID3 in an MP3, Vorbis comments
/// in FLAC and Ogg, iTunes' atoms in M4A, WMA's attributes); another tag goes wherever the file
/// has it already, every spelling of it, or, new, in the file's main kind of tag.
/// </summary>
public static class TagWriter
{
    /// <summary>
    /// Writes <paramref name="changes"/> (by <see cref="SongFields"/> field; null takes a tag off)
    /// into the file. Null if it worked; else why not, for the DJ ("the file is read-only").
    /// </summary>
    public static string? Write(string path, IReadOnlyDictionary<string, string?> changes)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            if (file.Writeable is false || file.PossiblyCorrupt)
                return "GZJam can't safely write this file's tags";
            TagTypes main = MainTag(file);
            file.GetTag(main, create: true);
            var id3 = file.GetTag(TagTypes.Id3v2) as TagLib.Id3v2.Tag;
            Tag tag = file.Tag;
            foreach (var (field, value) in changes)
            {
                switch (field)
                {
                    case SongFields.Title: tag.Title = value; break;
                    case SongFields.Artist: tag.Performers = Values(value); break;
                    case SongFields.Album: tag.Album = value; break;
                    case SongFields.AlbumArtist: tag.AlbumArtists = Values(value); break;
                    case SongFields.Genre: tag.Genres = Values(value); break;
                    case SongFields.Comment: SetComment(file, id3, value); break;
                    case SongFields.Grouping: SetGrouping(file, id3, value); break;
                    case SongFields.Year: tag.Year = (uint)(SongFields.ParseWhole(value) ?? 0); break;
                    case SongFields.Track: tag.Track = (uint)(SongFields.ParseWhole(value) ?? 0); break;
                    case SongFields.Bpm:
                        double? bpm = SongFields.ParseBpm(value);
                        tag.BeatsPerMinute = bpm is { } b ? (uint)Math.Round(b) : 0;
                        id3?.SetTextFrame("TBPM", bpm is { } exact ? SongFields.BpmText(exact) : null);   // as text, so 156.5 stays 156.5
                        break;
                    default:
                        if (SongFields.TagName(field) is { } name)
                            SongTags.Write(file, main, name, value);
                        break;
                }
            }
            file.Save();
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return "the file is read-only";
        }
        catch (IOException)
        {
            return "the file couldn't be written (another program may have it open, or its drive can't be written to)";
        }
        catch (Exception ex) when (ex is UnsupportedFormatException or CorruptFileException or NotImplementedException or NotSupportedException
                                       or ArgumentException or InvalidOperationException)
        {
            return "GZJam can't write this kind of file's tags";
        }
    }

    /// <summary>
    /// A file's own values of <paramref name="fields"/>, as read from it (not the library's edits):
    /// what writing them changes, for Undo. Null if the file can't be read.
    /// </summary>
    public static IReadOnlyDictionary<string, string?>? ReadFields(string path, IEnumerable<string> fields)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            return null;
        Track read = TagReader.Read(0, info.DirectoryName!, info);
        return fields.ToDictionary(f => f, f => SongFields.ValueOf(read, f), StringComparer.Ordinal);
    }

    /// <summary>The kind of tag a file is read by first, where a new tag goes.</summary>
    private static TagTypes MainTag(TagLib.File file) => file switch
    {
        TagLib.Mpeg4.File => TagTypes.Apple,
        TagLib.Asf.File => TagTypes.Asf,
        TagLib.Flac.File or TagLib.Ogg.File => TagTypes.Xiph,
        TagLib.Ape.File or TagLib.WavPack.File => TagTypes.Ape,
        _ => TagTypes.Id3v2,   // MP3, WAV, AIFF, AAC
    };

    /// <summary>One value, as typed ("Count Basie; Joe Williams" stays one, as GZJam reads several).</summary>
    private static string[] Values(string? value) => value is null ? [] : [value];

    /// <summary>
    /// The comment: in an MP3, every comment frame GZJam might show goes (iTunes' own data stays),
    /// so the one written is the one read.
    /// </summary>
    private static void SetComment(TagLib.File file, TagLib.Id3v2.Tag? id3, string? value)
    {
        if (id3 is not null)
        {
            foreach (var frame in id3.GetFrames<TagLib.Id3v2.CommentsFrame>()
                         .Where(f => !f.Description.StartsWith("iTun", StringComparison.OrdinalIgnoreCase)).ToList())
                id3.RemoveFrame(frame);
        }
        file.Tag.Comment = value;
    }

    /// <summary>
    /// Grouping: TagLib#'s (TIT1 in an MP3, as most programs write it), and in an MP3 that has
    /// iTunes' GRP1 frame, that too, since GZJam reads it first.
    /// </summary>
    private static void SetGrouping(TagLib.File file, TagLib.Id3v2.Tag? id3, string? value)
    {
        file.Tag.Grouping = value;
        if (id3 is null)
            return;
        var grp1 = id3.GetFrames("GRP1").ToList();
        if (grp1.Count == 0)
            return;
        foreach (var frame in grp1)
            id3.RemoveFrame(frame);
        if (value is not null)
            id3.AddFrame(TextFrame(id3, "GRP1", value));
    }

    /// <summary>A text frame TagLib# doesn't know (GRP1, MVNM): its encoding, then the text.</summary>
    internal static TagLib.Id3v2.Frame TextFrame(TagLib.Id3v2.Tag id3, string id, string value)
    {
        var data = new ByteVector();
        if (id3.Version >= 4)
        {
            data.Add(3);   // UTF-8
            data.Add(ByteVector.FromString(value, StringType.UTF8));
        }
        else
        {
            data.Add(1);   // UTF-16, with its byte order mark
            data.Add(ByteVector.FromString(value, StringType.UTF16));
        }
        return new TagLib.Id3v2.UnknownFrame(ByteVector.FromString(id, StringType.Latin1), data);
    }
}
