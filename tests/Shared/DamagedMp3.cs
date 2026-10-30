using System.Text;

namespace GZJam.Testing;

/// <summary>
/// An MP3 like one a DJ sent (#220): a good ID3 tag, then a second one whose first bytes are
/// damaged (so nothing reads it as a tag) and too long for BASS or TagLib to look past, then the
/// two-second tone in Formats/tone.mp3.
/// </summary>
public static class DamagedMp3
{
    /// <summary>How much of the damaged tag there is: more than BASS (16 KB) and TagLib look through.</summary>
    public const int DamagedLength = 80_000;

    /// <summary>
    /// Writes it to <paramref name="path"/>, with the length in the good tag if given (in the DJ's
    /// file it's in the damaged one); returns where its audio starts.
    /// </summary>
    public static long Write(string path, string title, string artist, int? lengthMs = null)
    {
        byte[] tag = Id3([("TIT2", title), ("TPE1", artist),
            .. lengthMs is { } ms ? new[] { ("TLEN", ms.ToString(System.Globalization.CultureInfo.InvariantCulture)) } : []]);
        var damaged = new byte[DamagedLength];
        new Random(220).NextBytes(damaged);
        for (int i = 0; i < damaged.Length; i++)
            damaged[i] &= 0xFE;   // no 0xFF: nothing in it reads as the start of an MPEG frame, as in the DJ's file
        damaged[0] = 0x21; damaged[1] = 0xAD; damaged[2] = 0x33;   // "ID3", damaged
        byte[] audio = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Formats", "tone.mp3"));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [.. tag, .. damaged, .. audio]);
        return tag.Length + damaged.Length;
    }

    /// <summary>An ID3v2.3 tag of text frames.</summary>
    private static byte[] Id3((string Id, string Text)[] frames)
    {
        var body = new List<byte>();
        foreach (var (id, text) in frames)
        {
            byte[] data = [0, .. Encoding.Latin1.GetBytes(text)];   // 0: Latin-1
            body.AddRange(Encoding.ASCII.GetBytes(id));
            body.AddRange([(byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length, 0, 0]);
            body.AddRange(data);
        }
        int size = body.Count;
        return [(byte)'I', (byte)'D', (byte)'3', 3, 0, 0,
            (byte)((size >> 21) & 0x7F), (byte)((size >> 14) & 0x7F), (byte)((size >> 7) & 0x7F), (byte)(size & 0x7F), .. body];
    }
}
