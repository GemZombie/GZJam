namespace GZJam.Library;

/// <summary>
/// What a song's info shows about its file beyond the library (#129): the kind of audio in it, and
/// the tags the library leaves out. Read when the info is opened, never kept.
/// </summary>
/// <param name="Format">"MP3", "FLAC", "AAC", "Apple Lossless"...</param>
/// <param name="Kbps">The bitrate (on average, for a variable one); 0 if the file doesn't say.</param>
/// <param name="Variable">A variable bitrate (an MP3 with a VBR header).</param>
/// <param name="SampleRate">In Hz; 0 if unknown.</param>
/// <param name="Channels">1 mono, 2 stereo; 0 if unknown.</param>
/// <param name="BitsPerSample">For lossless audio (16, 24), if the file says; 0 for the rest.</param>
/// <param name="LeftOut">The tags the library leaves out: for other programs, or too long.</param>
public sealed record SongFile(string Format, int Kbps, bool Variable, int SampleRate, int Channels, int BitsPerSample,
    IReadOnlyList<SongTag> LeftOut)
{
    /// <summary>The audio is exactly as recorded: FLAC, Apple Lossless, WAV...</summary>
    public bool Lossless => Format is "FLAC" or "Apple Lossless" or "WAV" or "AIFF" or "WMA Lossless" or "Monkey's Audio" or "WavPack";

    /// <summary>The file's details, or null if it isn't there or can't be read.</summary>
    public static SongFile? Read(string path)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            TagLib.Properties? properties = file.Properties;
            var codecs = properties?.Codecs.OfType<TagLib.IAudioCodec>().ToList() ?? [];
            string description = string.Join(' ', codecs.Select(c => c.Description));
            var info = new SongFile(
                FormatOf(Path.GetExtension(path), description),
                Math.Max(0, properties?.AudioBitrate ?? 0),
                description.Contains("VBR", StringComparison.Ordinal),
                Math.Max(0, properties?.AudioSampleRate ?? 0),
                Math.Max(0, properties?.AudioChannels ?? 0),
                Math.Max(0, properties?.BitsPerSample ?? 0),
                SongTags.ReadLeftOut(file));
            return info.Lossless ? info : info with { BitsPerSample = 0 };   // a WMA's 16 bits say nothing of its sound
        }
        catch (Exception ex) when (ex is TagLib.CorruptFileException or TagLib.UnsupportedFormatException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The kind of audio, as a DJ knows it, from the file's extension and TagLib#'s description of its codec.</summary>
    internal static string FormatOf(string extension, string codec)
    {
        string ext = extension.TrimStart('.').ToUpperInvariant();
        return ext switch
        {
            "M4A" or "MP4" or "M4B" when codec.Contains("alac", StringComparison.OrdinalIgnoreCase) => "Apple Lossless",
            "M4A" or "MP4" or "M4B" => "AAC",
            "OGG" or "OGA" when codec.Contains("Opus", StringComparison.OrdinalIgnoreCase) => "Opus",
            "OGG" or "OGA" when codec.Contains("FLAC", StringComparison.OrdinalIgnoreCase) => "FLAC",
            "OGG" or "OGA" => "Ogg Vorbis",
            "OPUS" => "Opus",
            "WMA" when codec.Contains("Lossless", StringComparison.OrdinalIgnoreCase) => "WMA Lossless",
            "AIF" or "AIFF" => "AIFF",
            "APE" => "Monkey's Audio",
            "WV" => "WavPack",
            "" => codec.Length > 0 ? codec : "Audio",
            _ => ext,
        };
    }
}
