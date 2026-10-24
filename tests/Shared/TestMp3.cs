namespace GZJam.Testing;

/// <summary>
/// Writes small but valid MP3 files for tests: a few silent MPEG-1 Layer III frames, with a Xing
/// header declaring a realistic duration, tagged via TagLib#.
/// </summary>
public static class TestMp3
{
    private const int FrameLength = 417;          // 128 kbps, 44.1 kHz, no padding
    private const int SamplesPerFrame = 1152;
    private const int SampleRate = 44100;
    private const int FramesWritten = 8;

    public sealed record Tags(
        string? Title = null, string? Artist = null, string? Album = null, string? Genre = null,
        int? Year = null, int? Track = null, string? Bpm = null, string? Comment = null,
        TimeSpan? Duration = null);

    public static void Write(string path, Tags? tags = null)
    {
        tags ??= new Tags();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, BuildAudio(tags.Duration ?? TimeSpan.FromMinutes(3)));

        if (tags is { Title: null, Artist: null, Album: null, Genre: null, Year: null, Track: null, Bpm: null, Comment: null })
            return;

        using var file = TagLib.File.Create(path);
        var id3 = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2, create: true);
        id3.Title = tags.Title;
        id3.Performers = tags.Artist is null ? [] : [tags.Artist];
        id3.Album = tags.Album;
        id3.Genres = tags.Genre is null ? [] : [tags.Genre];
        id3.Year = (uint)(tags.Year ?? 0);
        id3.Track = (uint)(tags.Track ?? 0);
        id3.Comment = tags.Comment;
        if (tags.Bpm is not null)
            id3.SetTextFrame("TBPM", tags.Bpm);   // raw text, so decimals like "156.5" survive
        file.Save();
    }

    /// <summary>Puts a picture in a file's tag (TagLib# doesn't look inside it, so any bytes do for finding it).</summary>
    public static void EmbedPicture(string path, byte[] picture, TagLib.PictureType type = TagLib.PictureType.FrontCover)
    {
        using var file = TagLib.File.Create(path);
        var id3 = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2, create: true);
        id3.Pictures = [.. id3.Pictures, new TagLib.Picture(new TagLib.ByteVector(picture)) { Type = type, MimeType = "image/png" }];
        file.Save();
    }

    private static byte[] BuildAudio(TimeSpan duration)
    {
        var data = new byte[FrameLength * FramesWritten];
        for (int f = 0; f < FramesWritten; f++)
        {
            int o = f * FrameLength;
            // Sync, MPEG-1, Layer III, no CRC | 128 kbps, 44.1 kHz | mono
            data[o] = 0xFF; data[o + 1] = 0xFB; data[o + 2] = 0x90; data[o + 3] = 0xC0;
        }

        // Xing header in the first frame (after 4-byte header + 17 bytes of mono side info),
        // declaring the total frame count so readers report the intended duration.
        int frames = (int)(duration.TotalSeconds * SampleRate / SamplesPerFrame);
        int x = 4 + 17;
        "Xing"u8.CopyTo(data.AsSpan(x));
        data[x + 7] = 0x01; // flags: frame count present
        data[x + 8] = (byte)(frames >> 24); data[x + 9] = (byte)(frames >> 16);
        data[x + 10] = (byte)(frames >> 8); data[x + 11] = (byte)frames;
        return data;
    }
}
