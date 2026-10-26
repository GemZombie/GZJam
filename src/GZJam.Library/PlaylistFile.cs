using System.Globalization;
using System.Text;

namespace GZJam.Library;

/// <summary>
/// Reads an M3U playlist, as nearly every player saves one (MediaMonkey, foobar2000, iTunes /
/// Music, VirtualDJ, Winamp, VLC): one song per line, by absolute path, by path relative to the
/// playlist, or as a file:// address. <c>#EXTM3U</c>, <c>#EXTINF</c> and other # lines are
/// skipped, and so are web addresses (a radio stream isn't a song GZJam can have).
/// </summary>
public static class PlaylistFile
{
    /// <summary>The kinds of file read, for a file picker.</summary>
    public static readonly string[] Patterns = ["*.m3u", "*.m3u8"];

    /// <summary>The songs in <paramref name="path"/>, in order, as full paths. Throws if it can't be read.</summary>
    public static IReadOnlyList<string> Read(string path)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var songs = new List<string>();
        foreach (string raw in ReadText(path).Split('\n'))
        {
            string line = raw.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            if (SongPath(line, folder) is { } song)
                songs.Add(song);
        }
        return songs;
    }

    /// <summary>One line as a full path, or null for a web address or something that isn't a path.</summary>
    internal static string? SongPath(string line, string folder)
    {
        // Only lines written as addresses are read as addresses: a plain path can hold '#' or '%'.
        if (line.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return Uri.TryCreate(line, UriKind.Absolute, out Uri? uri) && uri.IsFile ? uri.LocalPath : null;
        if (line.Contains("://", StringComparison.Ordinal))
            return null;   // http://, a radio stream
        // A playlist written on Windows uses '\'; elsewhere that's an ordinary character.
        if (!OperatingSystem.IsWindows())
            line = line.Replace('\\', '/');
        try
        {
            return Path.GetFullPath(Path.IsPathRooted(line) ? line : Path.Combine(folder, line));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// <c>.m3u8</c> is UTF-8. A plain <c>.m3u</c> with no byte-order mark is usually UTF-8 too, but
    /// Windows players often write it in the computer's own code page: if it isn't valid UTF-8,
    /// it's read that way.
    /// </summary>
    internal static string ReadText(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble))
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return SystemCodePage().GetString(bytes);
        }
    }

    private static Encoding SystemCodePage()
    {
        if (!OperatingSystem.IsWindows())
            return Encoding.Latin1;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try
        {
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Encoding.GetEncoding(1252);
        }
    }
}
