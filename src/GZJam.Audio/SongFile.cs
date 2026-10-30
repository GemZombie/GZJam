using ManagedBass;

namespace GZJam.Audio;

/// <summary>
/// Opens a song for decoding, for the decks, the headphone preview and analysis alike. BASS looks
/// for a file's audio only near its start (16 KB). An MP3 whose audio starts further in, behind a
/// damaged tag nothing reads as one (#220), is opened again from where its audio really starts.
/// Only files BASS refuses are searched: looking further into every file would make it likelier
/// that a FLAC or a broken file is taken for MP3 and played as noise.
/// </summary>
internal static class SongFile
{
    private const BassFlags Flags = BassFlags.Decode | BassFlags.Float | BassFlags.Prescan;   // Prescan: exact length and seeking for VBR MP3s

    /// <summary>How far into a file MP3 audio is looked for: past a damaged tag holding album art.</summary>
    private const int LookWithin = 8 * 1024 * 1024;

    /// <summary>MPEG frames in a row, alike, that are taken as the audio rather than chance bytes.</summary>
    private const int FramesInARow = 4;

    /// <summary>A decoding stream for <paramref name="path"/>. Throws, saying why, if it can't be played.</summary>
    public static int Open(string path)
    {
        int stream = Bass.CreateStream(path, 0, 0, Flags);
        if (stream != 0)
            return stream;
        Errors error = Bass.LastError;
        if (error == Errors.FileFormat && Mp3AudioStart(path) is { } start)
        {
            stream = Bass.CreateStream(path, start, 0, Flags);
            if (stream != 0)
                return stream;
        }
        throw AudioException.CouldNotOpen(path, error);
    }

    /// <summary>
    /// Where MP3 audio starts in <paramref name="path"/>: past the ID3 tags readable as tags, the
    /// first of <see cref="FramesInARow"/> Layer III frames, one after another, alike. Null if none.
    /// </summary>
    internal static long? Mp3AudioStart(string path)
    {
        byte[] data;
        try
        {
            using var file = File.OpenRead(path);
            data = new byte[(int)Math.Min(file.Length, LookWithin)];
            file.ReadExactly(data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        int at = 0;
        while (at + 10 <= data.Length && data[at] == 'I' && data[at + 1] == 'D' && data[at + 2] == '3')
        {
            int size = (data[at + 6] & 0x7F) << 21 | (data[at + 7] & 0x7F) << 14 | (data[at + 8] & 0x7F) << 7 | (data[at + 9] & 0x7F);
            at += 10 + size + ((data[at + 5] & 0x10) != 0 ? 10 : 0);   // a footer, if flagged
        }
        for (; at + 4 <= data.Length; at++)
        {
            if (FramesFrom(data, at))
                return at;
        }
        return null;
    }

    private static bool FramesFrom(byte[] data, int at)
    {
        int kind = -1;
        for (int i = 0; i < FramesInARow; i++)
        {
            if (FrameLength(data, at) is not var (length, thisKind) || (kind >= 0 && thisKind != kind))
                return false;
            kind = thisKind;
            at += length;
        }
        return true;
    }

    private static readonly int[] Mpeg1Kbps = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320];
    private static readonly int[] Mpeg2Kbps = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160];
    private static readonly int[] Mpeg1Hz = [44100, 48000, 32000];

    /// <summary>
    /// The length of the Layer III frame whose header is at <paramref name="at"/>, and its kind
    /// (MPEG version and sample rate, the same all through a song), or null if there's none there.
    /// </summary>
    private static (int Length, int Kind)? FrameLength(byte[] data, int at)
    {
        if (at < 0 || at + 4 > data.Length || data[at] != 0xFF || (data[at + 1] & 0xE0) != 0xE0)
            return null;
        int version = (data[at + 1] >> 3) & 3;   // 3: MPEG 1, 2: MPEG 2, 0: MPEG 2.5, 1: reserved
        int layer = (data[at + 1] >> 1) & 3;     // 1: Layer III
        int bitrate = data[at + 2] >> 4, rate = (data[at + 2] >> 2) & 3, padding = (data[at + 2] >> 1) & 1;
        if (version == 1 || layer != 1 || bitrate is 0 or 15 || rate == 3)
            return null;
        bool mpeg1 = version == 3;
        int hz = Mpeg1Hz[rate] >> (mpeg1 ? 0 : version == 2 ? 1 : 2);
        int kbps = (mpeg1 ? Mpeg1Kbps : Mpeg2Kbps)[bitrate];
        return ((mpeg1 ? 144_000 : 72_000) * kbps / hz + padding, version << 2 | rate);
    }
}
