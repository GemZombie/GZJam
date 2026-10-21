namespace GZJam.Audio;

/// <summary>
/// A whole-track overview waveform: for each of <see cref="Count"/> equal slices of the track, the
/// peak level of its bass, mids and highs, and how loud it is overall (RMS). Levels are stored as
/// bytes on a square-root scale, so quiet passages keep some detail; <see cref="Decode"/> undoes it.
/// </summary>
public sealed class Waveform
{
    private const byte FormatVersion = 2;
    private const int Bands = 4;

    public Waveform(byte[] low, byte[] mid, byte[] high, byte[] loudness)
    {
        if (low.Length != mid.Length || low.Length != high.Length || low.Length != loudness.Length)
            throw new ArgumentException("Every band needs the same number of slices.");
        Low = low;
        Mid = mid;
        High = high;
        Loudness = loudness;
        Peak = Math.Max(Max(low), Math.Max(Max(mid), Max(high)));
        LoudnessPeak = Max(loudness);
    }

    public int Count => Low.Length;
    public byte[] Low { get; }
    public byte[] Mid { get; }
    public byte[] High { get; }

    /// <summary>RMS level per slice: the shape of the song (quiet intros, breaks, endings).</summary>
    public byte[] Loudness { get; }

    /// <summary>The loudest stored value in any band, for scaling quiet recordings up to fill the view.</summary>
    public byte Peak { get; }

    /// <summary>The loudest slice.</summary>
    public byte LoudnessPeak { get; }

    /// <summary>Converts a level (0..1) to its stored value.</summary>
    public static byte Encode(float level) => (byte)Math.Round(Math.Sqrt(Math.Clamp(level, 0, 1)) * 255);

    /// <summary>Converts a stored value back to a level (0..1).</summary>
    public static double Decode(byte value) => value / 255.0 * (value / 255.0);

    public byte[] ToBytes()
    {
        var bytes = new byte[3 + Bands * Count];
        bytes[0] = FormatVersion;
        bytes[1] = (byte)(Count >> 8);
        bytes[2] = (byte)Count;
        Low.CopyTo(bytes, 3);
        Mid.CopyTo(bytes, 3 + Count);
        High.CopyTo(bytes, 3 + 2 * Count);
        Loudness.CopyTo(bytes, 3 + 3 * Count);
        return bytes;
    }

    /// <summary>Reads what <see cref="ToBytes"/> wrote; null if it's unreadable or an old format (re-analyze).</summary>
    public static Waveform? FromBytes(byte[]? bytes)
    {
        if (bytes is not { Length: >= 3 } || bytes[0] != FormatVersion)
            return null;
        int count = (bytes[1] << 8) | bytes[2];
        if (bytes.Length != 3 + Bands * count)
            return null;
        byte[] Band(int i) => bytes[(3 + i * count)..(3 + (i + 1) * count)];
        return new Waveform(Band(0), Band(1), Band(2), Band(3));
    }

    private static byte Max(byte[] values) => values.Length == 0 ? (byte)0 : values.Max();
}
