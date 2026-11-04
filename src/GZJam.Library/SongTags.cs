using System.Globalization;
using System.Text;

namespace GZJam.Library;

/// <summary>
/// A tag from a song file beyond the ones GZJam has its own columns for (#119): the composer, a
/// mood, the dance in a custom tag. Read from the file; written to it only when the DJ edits it
/// with writing to the files on (#129).
/// </summary>
public sealed record SongTag(string Name, string Value)
{
    /// <summary>The tag as a search filter: its name's letters and digits, lowercase ("Band leader" is <c>bandleader:</c>).</summary>
    public string Key => SongTags.KeyOf(Name);
}

/// <summary>A tag found in the library's songs, by <see cref="SongTag.Key"/>, with the name most songs give it, and how many have it.</summary>
public sealed record TagName(string Key, string Name, int Count);

/// <summary>
/// Reading the tags a song file holds beyond the usual ones (#119), whatever the format writes them
/// as: ID3 frames and TXXX in MP3, Vorbis comments in FLAC, Ogg and Opus, iTunes' atoms and
/// freeform tags in M4A, APE and WMA tags. The same tag in different formats becomes one (TCOM,
/// COMPOSER and ©wrt are all "Composer"). Tags for programs rather than people (MusicBrainz IDs,
/// ReplayGain, the encoder) are left out, as are lyrics and anything very long.
/// </summary>
public static class SongTags
{
    /// <summary>Longer values are left out: lyrics, cue sheets and rip logs, not something to show in a column.</summary>
    internal const int MaxValueLength = 500;

    private const int MaxNameLength = 40;

    /// <summary>The most extra tags kept for one song.</summary>
    private const int MaxTags = 40;

    /// <summary>A tag's name as a filter: letters and digits only, lowercase.</summary>
    public static string KeyOf(string name)
    {
        var key = new StringBuilder(name.Length);
        foreach (char ch in name)
        {
            if (char.IsLetterOrDigit(ch))
                key.Append(char.ToLowerInvariant(ch));
        }
        return key.ToString();
    }

    /// <summary>
    /// Names by key: what a tag is called whichever way a format spells it, or null for a tag that
    /// isn't shown (GZJam reads it as its own column, or it's for programs, not people).
    /// </summary>
    private static readonly Dictionary<string, string?> Names = new(StringComparer.Ordinal)
    {
        // Read already, each in its own column
        ["title"] = null, ["artist"] = null, ["album"] = null, ["albumartist"] = null, ["genre"] = null,
        ["comment"] = null, ["description"] = null, ["date"] = null, ["year"] = null, ["tracknumber"] = null,
        ["track"] = null, ["tracktotal"] = null, ["totaltracks"] = null, ["bpm"] = null, ["tempo"] = null,
        ["grouping"] = null, ["contentgroup"] = null, ["author"] = null,
        ["albumtitle"] = null, ["beatsperminute"] = null, ["contentgroupdescription"] = null,   // WMA's names
        // The same tag, spelled other ways
        ["composer"] = "Composer", ["writer"] = "Composer",
        ["conductor"] = "Conductor",
        ["remixer"] = "Remixer", ["mixartist"] = "Remixer", ["modifiedby"] = "Remixer", ["remixedby"] = "Remixer",
        ["lyricist"] = "Lyricist",
        ["key"] = "Key", ["initialkey"] = "Key",
        ["mood"] = "Mood",
        ["publisher"] = "Publisher", ["label"] = "Publisher", ["organization"] = "Publisher",
        ["copyright"] = "Copyright",
        ["subtitle"] = "Subtitle",
        ["discnumber"] = "Disc", ["disc"] = "Disc", ["partofset"] = "Disc",
        ["originalartist"] = "Original artist", ["originalalbum"] = "Original album", ["originalalbumtitle"] = "Original album",
        ["originalyear"] = "Original year", ["originaldate"] = "Original year", ["originalreleasedate"] = "Original year",
        ["originalreleaseyear"] = "Original year",
        ["language"] = "Language", ["media"] = "Media", ["work"] = "Work", ["movement"] = "Movement",
        ["discsubtitle"] = "Disc subtitle", ["setsubtitle"] = "Disc subtitle", ["performer"] = "Performer",
        // For programs, or not for a column
        ["lyrics"] = null, ["unsyncedlyrics"] = null, ["isrc"] = null, ["barcode"] = null, ["upc"] = null,
        ["asin"] = null, ["compilation"] = null, ["metadatablockpicture"] = null, ["coverart"] = null,
        ["coverartmime"] = null, ["length"] = null, ["totaltime"] = null, ["cuesheet"] = null, ["log"] = null,
        ["vendor"] = null, ["codinghistory"] = null, ["tool"] = null, ["toolname"] = null, ["toolversion"] = null,
        ["disctotal"] = null, ["totaldiscs"] = null, ["isvbr"] = null, ["deviceconformancetemplate"] = null,
        ["script"] = null, ["releasestatus"] = null, ["releasetype"] = null, ["releasecountry"] = null,
        ["picture"] = null, ["waveform"] = null, ["source"] = null, ["sourcemedia"] = null,
    };

    /// <summary>Names starting with these are for programs: IDs, loudness, encoders, other apps' data.</summary>
    private static readonly string[] ProgramPrefixes =
    [
        "musicbrainz", "replaygain", "acoustid", "musicip", "r128", "itun", "encod", "serato", "traktor",
        "rekordbox", "mediaclass", "mediaprimaryclass", "wmcontent", "wmcollection", "wmfsdk", "provider",
        "uniquefileidentifier", "mcdi", "discogs", "spotify", "amazon", "lastfm", "beatport", "cdtoc",
    ];

    /// <summary>The ID3 frames shown, beyond custom TXXX tags: the rest are read already, or are for programs.</summary>
    private static readonly Dictionary<string, string> Id3Frames = new(StringComparer.Ordinal)
    {
        ["TCOM"] = "Composer", ["TPE3"] = "Conductor", ["TPE4"] = "Remixer", ["TEXT"] = "Lyricist",
        ["TKEY"] = "Key", ["TMOO"] = "Mood", ["TPUB"] = "Publisher", ["TCOP"] = "Copyright",
        ["TIT3"] = "Subtitle", ["TPOS"] = "Disc", ["TOPE"] = "Original artist", ["TOAL"] = "Original album",
        ["TOLY"] = "Original lyricist", ["TDOR"] = "Original year", ["TORY"] = "Original year",
        ["TLAN"] = "Language", ["TMED"] = "Media", ["TSST"] = "Disc subtitle", ["MVNM"] = "Movement",
    };

    /// <summary>The M4A atoms shown, beyond freeform (----) tags.</summary>
    private static readonly Dictionary<string, string> AppleAtoms = new(StringComparer.Ordinal)
    {
        ["©wrt"] = "Composer", ["cprt"] = "Copyright", ["©wrk"] = "Work", ["©mvn"] = "Movement",
        ["©con"] = "Conductor",
    };

    /// <summary>The tags GZJam has fields of its own for, which a song's info shows there (#129): by name, ID3 frame or M4A atom.</summary>
    private static readonly HashSet<string> UsualKeys = new(StringComparer.Ordinal)
    {
        "title", "artist", "album", "albumartist", "genre", "comment", "description", "date", "year",
        "tracknumber", "track", "bpm", "tempo", "grouping", "contentgroup", "author", "albumtitle",
        "beatsperminute", "contentgroupdescription",
    };

    private static readonly HashSet<string> UsualFrames = new(StringComparer.Ordinal)
    {
        "TIT2", "TPE1", "TALB", "TPE2", "TCON", "TYER", "TDRC", "TDAT", "TRCK", "TBPM", "TIT1", "GRP1",
    };

    private static readonly HashSet<string> UsualAtoms = new(StringComparer.Ordinal)
    {
        "©nam", "©ART", "©alb", "aART", "©gen", "©cmt", "©day", "trkn", "tmpo", "©grp", "gnre",
    };

    /// <summary>Names for the ID3 frames and M4A atoms only other programs use, in a song's info; the rest go by their IDs.</summary>
    private static readonly Dictionary<string, string> ProgramFrameNames = new(StringComparer.Ordinal)
    {
        ["TSSE"] = "Encoder settings", ["TENC"] = "Encoded by", ["TSOP"] = "Artist sort", ["TSOA"] = "Album sort",
        ["TSOT"] = "Title sort", ["TSO2"] = "Album artist sort", ["TSOC"] = "Composer sort", ["TSRC"] = "ISRC",
        ["TLEN"] = "Length", ["TFLT"] = "File type", ["TCMP"] = "Compilation", ["TDTG"] = "Tagging time",
        ["TDRL"] = "Release time", ["TDEN"] = "Encoding time", ["TOWN"] = "Owner", ["TOFN"] = "Original file name",
        ["©too"] = "Encoder", ["©enc"] = "Encoded by", ["soar"] = "Artist sort", ["sonm"] = "Title sort",
        ["soal"] = "Album sort", ["soaa"] = "Album artist sort", ["soco"] = "Composer sort", ["©lyr"] = "Lyrics",
        ["desc"] = "Description",
    };

    /// <summary>How a file names a tag: by a name of its own (TXXX, Vorbis, freeform M4A, APE, WMA), or by an ID3 frame's or M4A atom's ID.</summary>
    private enum Naming { Named, Id3Frame, AppleAtom }

    /// <summary>The extra tags in a file, sorted by name. The first spelling found of a tag wins.</summary>
    internal static IReadOnlyList<SongTag> Read(TagLib.File file)
    {
        var found = new Dictionary<string, SongTag>(StringComparer.Ordinal);
        Visit(file, (naming, name, values) =>
        {
            if (found.Count >= MaxTags || Shown(naming, name) is not { } shown)
                return;
            string key = KeyOf(shown);
            if (key.Length == 0 || found.ContainsKey(key) || Value(values) is not { } value)
                return;
            found[key] = new SongTag(shown, value);
        });
        return [.. found.Values.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The longest value shown of a tag the library leaves out (lyrics, a rip log), in a song's info: longer ones are cut off with "…".</summary>
    internal const int MaxOtherValueLength = 400;

    /// <summary>
    /// The tags in a file that the library leaves out (#129): the ones only other programs use
    /// (MusicBrainz IDs, ReplayGain, the encoder, sort names), lyrics and other long ones, by the
    /// names the file gives them. For a song's info, with <em>Show all tags</em>. Sorted by name.
    /// </summary>
    public static IReadOnlyList<SongTag> ReadLeftOut(TagLib.File file)
    {
        var kept = Read(file).ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal);
        var leftOut = new List<SongTag>();
        Visit(file, (naming, name, values) =>
        {
            name = name?.Trim().TrimEnd('\0');
            if (string.IsNullOrEmpty(name) || leftOut.Count >= 100 || Value(values, int.MaxValue) is not { } value)
                return;
            if (Shown(naming, name) is { } shown)
            {
                if (kept.TryGetValue(KeyOf(shown), out string? keptValue) && keptValue == value)
                    return;   // among the song's other tags already
            }
            else if (naming switch
            {
                Naming.Id3Frame => UsualFrames.Contains(name),
                Naming.AppleAtom => UsualAtoms.Contains(name),
                _ => UsualKeys.Contains(KeyOf(name)),
            })
            {
                return;   // one of GZJam's own fields
            }
            if (naming != Naming.Named)
                name = ProgramFrameNames.GetValueOrDefault(name, name);
            if (value.Length > MaxOtherValueLength)
                value = value[..MaxOtherValueLength].TrimEnd() + "…";
            if (!leftOut.Any(t => t.Name == name && t.Value == value))
                leftOut.Add(new SongTag(name, value));
        });
        return [.. leftOut.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The name a tag goes by among a song's other tags, or null if it isn't one.</summary>
    private static string? Shown(Naming naming, string? name) => naming switch
    {
        Naming.Id3Frame => Id3Frames.GetValueOrDefault(name ?? ""),
        Naming.AppleAtom => AppleAtoms.GetValueOrDefault(name ?? ""),
        _ => ShownName(name),
    };

    /// <summary>Every text tag in a file, whatever its format: how it's named, its name, its values.</summary>
    private static void Visit(TagLib.File file, Action<Naming, string?, IEnumerable<string?>> add)
    {
        try
        {
            if (file.GetTag(TagLib.TagTypes.Id3v2) is TagLib.Id3v2.Tag id3)
            {
                foreach (var frame in id3.GetFrames<TagLib.Id3v2.TextInformationFrame>())
                {
                    if (frame is TagLib.Id3v2.UserTextInformationFrame user)
                        add(Naming.Named, user.Description, user.Text);
                    else
                        add(Naming.Id3Frame, frame.FrameId.ToString(TagLib.StringType.Latin1), frame.Text);
                }
                foreach (TagLib.Id3v2.Frame frame in id3.GetFrames("MVNM"))   // TagLib# doesn't know it
                {
                    if (frame is TagLib.Id3v2.UnknownFrame unknown)
                        add(Naming.Id3Frame, "MVNM", [TagReader.FrameText(unknown.Data)]);
                }
            }
            if (file.GetTag(TagLib.TagTypes.Xiph) is TagLib.Ogg.XiphComment xiph)
            {
                foreach (string name in xiph)
                    add(Naming.Named, name, xiph.GetField(name));
            }
            if (file.GetTag(TagLib.TagTypes.Apple) is TagLib.Mpeg4.AppleTag apple)
            {
                foreach (TagLib.Mpeg4.Box box in apple)
                {
                    string type = box.BoxType.ToString(TagLib.StringType.Latin1);
                    if (type == "----")
                        add(Naming.Named, FreeformName(box), DataTexts(box));
                    else
                        add(Naming.AppleAtom, type, DataTexts(box));
                }
            }
            if (file.GetTag(TagLib.TagTypes.Ape) is TagLib.Ape.Tag ape)
            {
                foreach (string name in ape)
                {
                    if (ape.GetItem(name) is { Type: TagLib.Ape.ItemType.Text } item)
                        add(Naming.Named, name, item.ToStringArray());
                }
            }
            if (file.GetTag(TagLib.TagTypes.Asf) is TagLib.Asf.Tag asf)
            {
                foreach (TagLib.Asf.ContentDescriptor descriptor in asf)
                {
                    if (descriptor.Type == TagLib.Asf.DataType.Unicode)
                        add(Naming.Named, descriptor.Name.StartsWith("WM/", StringComparison.OrdinalIgnoreCase) ? descriptor.Name[3..] : descriptor.Name,
                            [descriptor.ToString()]);
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IndexOutOfRangeException or NullReferenceException)
        {
            // A damaged tag: what was read before it is kept, like the usual tags.
        }
    }

    /// <summary>A freeform M4A tag's name (----:com.apple.iTunes:MOOD is "MOOD").</summary>
    private static string? FreeformName(TagLib.Mpeg4.Box box) =>
        (box as TagLib.Mpeg4.AppleAnnotationBox)?.Children
            .OfType<TagLib.Mpeg4.AppleAdditionalInfoBox>()
            .FirstOrDefault(b => b.BoxType.ToString(TagLib.StringType.Latin1) == "name")?.Text;

    /// <summary>The text in an M4A tag's data boxes; pictures and numbers have none.</summary>
    private static IEnumerable<string?> DataTexts(TagLib.Mpeg4.Box box) =>
        (box as TagLib.Mpeg4.AppleAnnotationBox)?.Children
            .OfType<TagLib.Mpeg4.AppleDataBox>()
            .Where(d => d.Flags == (uint)TagLib.Mpeg4.AppleDataBox.FlagType.ContainsText)
            .Select(d => d.Text) ?? [];

    /// <summary>
    /// What a tag is shown as: its usual name if it has one ("Composer"), else as the file has it,
    /// tidied ("BAND_LEADER" is "Band leader"); null if it isn't shown.
    /// </summary>
    internal static string? ShownName(string? name)
    {
        name = name?.Trim().TrimEnd('\0');
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
            return null;
        string key = KeyOf(name);
        if (key.Length == 0 || !key.Any(char.IsLetter))
            return null;
        if (Names.TryGetValue(key, out string? known))
            return known;
        if (ProgramPrefixes.Any(p => key.StartsWith(p, StringComparison.Ordinal))
            || key.EndsWith("sort", StringComparison.Ordinal) || key.EndsWith("sortorder", StringComparison.Ordinal) || key.Length > 4 && key.EndsWith("id", StringComparison.Ordinal))
            return null;
        string tidy = string.Join(' ', name.Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries));
        bool hasUpper = tidy.Any(char.IsUpper), hasLower = tidy.Any(char.IsLower);
        return hasUpper && hasLower ? tidy : char.ToUpperInvariant(tidy[0]) + tidy[1..].ToLowerInvariant();
    }

    /// <summary>A tag's values as one ("Count Basie; Joe Williams"), or null if it has none worth showing.</summary>
    private static string? Value(IEnumerable<string?> values, int maxLength = MaxValueLength)
    {
        var kept = new List<string>();
        foreach (string? value in values)
        {
            string? clean = value?.Trim().Trim('\0', '﻿').Trim();
            if (!string.IsNullOrEmpty(clean) && !kept.Contains(clean, StringComparer.OrdinalIgnoreCase))
                kept.Add(clean);
        }
        string joined = string.Join("; ", kept);
        return joined.Length == 0 || joined.Length > maxLength ? null : joined;
    }

    // ───────────── Written to the file (#129) ─────────────

    /// <summary>
    /// The name a tag the DJ types is shown by ("composer" is "Composer", "BAND_LEADER" is "Band
    /// leader"), or null if it can't be one of a song's other tags: one of GZJam's own fields
    /// (Title, BPM), or one for programs (a MusicBrainz ID).
    /// </summary>
    public static string? NameFor(string typed) => ShownName(typed);

    /// <summary>New tags' names in WMA, where its own differ from the ones GZJam shows.</summary>
    private static readonly Dictionary<string, string> AsfNames = new(StringComparer.Ordinal)
    {
        ["disc"] = "WM/PartOfSet", ["key"] = "WM/InitialKey", ["lyricist"] = "WM/Writer", ["remixer"] = "WM/ModifiedBy",
        ["originalalbum"] = "WM/OriginalAlbumTitle", ["originalyear"] = "WM/OriginalReleaseYear",
    };

    /// <summary>New tags' names in Vorbis comments, where the usual one isn't the name in capitals.</summary>
    private static readonly Dictionary<string, string> XiphNames = new(StringComparer.Ordinal)
    {
        ["disc"] = "DISCNUMBER", ["originalyear"] = "ORIGINALDATE",
    };

    /// <summary>
    /// Writes one of a song's other tags (<paramref name="name"/> as shown: "Composer", "Dance")
    /// into the file: every spelling the file has of it changes (or goes, with a null value);
    /// a tag it hasn't got goes in its <paramref name="main"/> kind of tag (ID3, Vorbis comments,
    /// iTunes' atoms...), by its usual name there. Saved by the caller.
    /// </summary>
    internal static void Write(TagLib.File file, TagLib.TagTypes main, string name, string? value)
    {
        string key = KeyOf(name);
        bool Is(Naming naming, string? id) => Shown(naming, id) is { } shown && KeyOf(shown) == key;
        bool found = false;

        if (file.GetTag(TagLib.TagTypes.Id3v2) is TagLib.Id3v2.Tag id3)
        {
            foreach (var frame in id3.GetFrames<TagLib.Id3v2.TextInformationFrame>().ToList())
            {
                bool same = frame is TagLib.Id3v2.UserTextInformationFrame user
                    ? Is(Naming.Named, user.Description)
                    : Is(Naming.Id3Frame, frame.FrameId.ToString(TagLib.StringType.Latin1));
                if (!same)
                    continue;
                found = true;
                if (value is null)
                    id3.RemoveFrame(frame);
                else
                    frame.Text = [value];
            }
            if (Is(Naming.Id3Frame, "MVNM"))
            {
                foreach (TagLib.Id3v2.Frame frame in id3.GetFrames("MVNM").ToList())
                {
                    found = true;
                    id3.RemoveFrame(frame);
                    if (value is not null)
                        id3.AddFrame(TagWriter.TextFrame(id3, "MVNM", value));
                }
            }
        }
        if (file.GetTag(TagLib.TagTypes.Xiph) is TagLib.Ogg.XiphComment xiph)
        {
            foreach (string field in xiph.Cast<string>().ToList())
            {
                if (!Is(Naming.Named, field))
                    continue;
                found = true;
                if (value is null)
                    xiph.RemoveField(field);
                else
                    xiph.SetField(field, value);
            }
        }
        if (file.GetTag(TagLib.TagTypes.Apple) is TagLib.Mpeg4.AppleTag apple)
        {
            foreach (TagLib.Mpeg4.Box box in apple.Cast<TagLib.Mpeg4.Box>().ToList())
            {
                string type = box.BoxType.ToString(TagLib.StringType.Latin1);
                if (type == "----")
                {
                    if (FreeformName(box) is not { } freeform || !Is(Naming.Named, freeform))
                        continue;
                    found = true;
                    apple.SetDashBox(FreeformMean(box) ?? "com.apple.iTunes", freeform, value ?? "");
                }
                else if (Is(Naming.AppleAtom, type))
                {
                    found = true;
                    if (value is null)
                        apple.ClearData(box.BoxType);
                    else
                        apple.SetText(box.BoxType, value);
                }
            }
        }
        if (file.GetTag(TagLib.TagTypes.Ape) is TagLib.Ape.Tag ape)
        {
            foreach (string item in ape.Cast<string>().ToList())
            {
                if (!Is(Naming.Named, item))
                    continue;
                found = true;
                if (value is null)
                    ape.RemoveItem(item);
                else
                    ape.SetValue(item, value);
            }
        }
        if (file.GetTag(TagLib.TagTypes.Asf) is TagLib.Asf.Tag asf)
        {
            foreach (string descriptor in asf.Cast<TagLib.Asf.ContentDescriptor>().Where(d => d.Type == TagLib.Asf.DataType.Unicode)
                         .Select(d => d.Name).Distinct(StringComparer.Ordinal).ToList())
            {
                if (!Is(Naming.Named, descriptor.StartsWith("WM/", StringComparison.OrdinalIgnoreCase) ? descriptor[3..] : descriptor))
                    continue;
                found = true;
                if (value is null)
                    asf.RemoveDescriptors(descriptor);
                else
                    asf.SetDescriptorString(value, descriptor);
            }
        }
        if (found || value is null)
            return;

        // New to the file: in its main kind of tag, by the usual name there.
        switch (file.GetTag(main, create: true))
        {
            case TagLib.Id3v2.Tag tag:
                if (Id3Frames.FirstOrDefault(f => KeyOf(f.Value) == key).Key is { } frameId && frameId != "MVNM")
                    tag.SetTextFrame(frameId, value);
                else
                    TagLib.Id3v2.UserTextInformationFrame.Get(tag, name.ToUpperInvariant(), create: true).Text = [value];
                break;
            case TagLib.Ogg.XiphComment tag:
                tag.SetField(XiphNames.GetValueOrDefault(key, name.ToUpperInvariant().Replace(' ', '_')), value);
                break;
            case TagLib.Mpeg4.AppleTag tag:
                if (AppleAtoms.FirstOrDefault(a => KeyOf(a.Value) == key).Key is { } atom)
                    tag.SetText(TagLib.ByteVector.FromString(atom, TagLib.StringType.Latin1), value);
                else
                    tag.SetDashBox("com.apple.iTunes", name.ToUpperInvariant(), value);
                break;
            case TagLib.Ape.Tag tag:
                tag.SetValue(name, value);
                break;
            case TagLib.Asf.Tag tag:
                tag.SetDescriptorString(value, AsfNames.GetValueOrDefault(key, Names.ContainsKey(key) ? "WM/" + name.Replace(" ", "") : name));
                break;
            default:
                throw new NotSupportedException($"No tag of the kind {main} to write {name} in");
        }
    }

    /// <summary>A freeform M4A tag's "mean": whose tag it is (com.apple.iTunes, mostly).</summary>
    private static string? FreeformMean(TagLib.Mpeg4.Box box) =>
        (box as TagLib.Mpeg4.AppleAnnotationBox)?.Children
            .OfType<TagLib.Mpeg4.AppleAdditionalInfoBox>()
            .FirstOrDefault(b => b.BoxType.ToString(TagLib.StringType.Latin1) == "mean")?.Text;

    // ───────────── In the library ─────────────

    private const char NameEnd = '\u001F', TagEnd = '\u001E';

    /// <summary>A song's extra tags as kept in the library: each name, then its value.</summary>
    internal static string? Encode(IReadOnlyList<SongTag> tags) =>
        tags.Count == 0 ? null : string.Join(TagEnd, tags.Select(t => $"{t.Name}{NameEnd}{t.Value}"));

    /// <summary>The values alone, for the search index: plain words find them, not their names ("mood" isn't every song with a Mood).</summary>
    internal static string? SearchText(IReadOnlyList<SongTag> tags) =>
        tags.Count == 0 ? null : string.Join('\n', tags.Select(t => t.Value));

    internal static IReadOnlyList<SongTag> Decode(string? encoded)
    {
        if (string.IsNullOrEmpty(encoded))
            return [];
        var tags = new List<SongTag>();
        foreach (string part in encoded.Split(TagEnd))
        {
            int end = part.IndexOf(NameEnd);
            if (end > 0)
                tags.Add(new SongTag(part[..end], part[(end + 1)..]));
        }
        return tags;
    }

    /// <summary>The value of the tag with <paramref name="key"/> in a song's kept tags, if it has one.</summary>
    internal static string? ValueOf(string? encoded, string key) =>
        Decode(encoded).FirstOrDefault(t => t.Key == key)?.Value;

    /// <summary>
    /// Whether the tag <paramref name="key"/> holds the typed words, as the search index matches a
    /// field: each word starts a word of the value, in any case and with or without accents; as a
    /// <paramref name="phrase"/>, the words together, in order, the last one a prefix.
    /// </summary>
    internal static bool Matches(string? encoded, string key, string typed, bool phrase)
    {
        if (ValueOf(encoded, key) is not { } value)
            return false;
        string[] words = Words(typed), have = Words(value);
        if (words.Length == 0)
            return false;
        if (!phrase)
            return words.All(w => have.Any(h => h.StartsWith(w, StringComparison.Ordinal)));
        for (int start = 0; start + words.Length <= have.Length; start++)
        {
            bool all = true;
            for (int i = 0; i < words.Length && all; i++)
                all = i == words.Length - 1 ? have[start + i].StartsWith(words[i], StringComparison.Ordinal) : have[start + i] == words[i];
            if (all)
                return true;
        }
        return false;
    }

    /// <summary>The words in a text as the search index splits them: letters and digits, lowercase, without accents.</summary>
    private static string[] Words(string text)
    {
        var plain = new StringBuilder(text.Length);
        foreach (char ch in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                plain.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }
        return plain.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}
