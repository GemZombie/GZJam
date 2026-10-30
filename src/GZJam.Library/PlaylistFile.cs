using System.Globalization;
using System.Text;

namespace GZJam.Library;

/// <summary>
/// Reads an M3U playlist, as nearly every player saves one (MediaMonkey, foobar2000, iTunes /
/// Music, VirtualDJ, Winamp, VLC): one song per line, by absolute path, by path relative to the
/// playlist, or as a file:// address. <c>#EXTM3U</c>, <c>#EXTINF</c> and other # lines are
/// skipped, and so are web addresses (a radio stream isn't a song GZJam can have). Also iTunes'
/// text export (#221): a tab-separated table of the songs, a header first, their paths under
/// Location.
/// </summary>
public static class PlaylistFile
{
    /// <summary>The kinds of file read, for a file picker.</summary>
    public static readonly string[] Patterns = ["*.m3u", "*.m3u8", "*.txt"];

    /// <summary>The songs in <paramref name="path"/>, in order, as full paths. Throws if it can't be read.</summary>
    public static IReadOnlyList<string> Read(string path)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var lines = ReadText(path).Split('\n').Select(l => l.TrimEnd('\r').TrimStart('\uFEFF')).ToList();
        var songs = new List<string>();
        foreach (string line in TableLocations(lines) ?? lines.Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')))
        {
            if (SongPath(line, folder) is { } song)
                songs.Add(song);
        }
        return songs;
    }

    /// <summary>
    /// The paths in a table of songs, as iTunes exports a playlist as text: tab-separated, a
    /// header first, the paths under Location (or, from iTunes in another language, the column
    /// that holds paths). Null if <paramref name="lines"/> isn't such a table.
    /// </summary>
    internal static IEnumerable<string>? TableLocations(IReadOnlyList<string> lines)
    {
        var rows = lines.Where(l => l.Trim().Length > 0).Select(l => l.Split('\t')).ToList();
        if (rows.Count == 0 || rows[0].Length < 2)
            return null;
        string[] header = rows[0];
        var songs = rows.Skip(1).ToList();
        int column = Array.FindIndex(header, h => h.Trim().Equals("Location", StringComparison.OrdinalIgnoreCase));
        if (column < 0)
        {
            column = Enumerable.Range(0, header.Length)
                .Select(c => (Column: c, Paths: songs.Count(r => c < r.Length && LooksLikePath(r[c].Trim()))))
                .Where(c => c.Paths > 0)
                .OrderByDescending(c => c.Paths)
                .Select(c => c.Column)
                .DefaultIfEmpty(-1)
                .First();
            if (column < 0)
                return null;
        }
        return songs.Where(r => column < r.Length).Select(r => r[column].Trim()).Where(p => p.Length > 0);
    }

    private static bool LooksLikePath(string value) =>
        IsWindowsPath(value) || value.StartsWith('/') || value.StartsWith("file:", StringComparison.OrdinalIgnoreCase);

    /// <summary>One line as a full path, or null for a web address or something that isn't a path.</summary>
    internal static string? SongPath(string line, string folder)
    {
        // Only lines written as addresses are read as addresses: a plain path can hold '#' or '%'.
        if (line.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return Uri.TryCreate(line, UriKind.Absolute, out Uri? uri) && uri.IsFile ? uri.LocalPath : null;
        if (line.Contains("://", StringComparison.Ordinal))
            return null;   // http://, a radio stream
        // A playlist written on Windows uses '\'; elsewhere that's an ordinary character. A full
        // Windows path (X:\..., \\server\...) stays as it is, to be found by how it ends (#217).
        if (!OperatingSystem.IsWindows())
        {
            if (IsWindowsPath(line))
                return line;
            line = line.Replace('\\', '/');
        }
        try
        {
            return Path.GetFullPath(Path.IsPathRooted(line) ? line : Path.Combine(folder, line));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>A full path on Windows: on a drive (<c>X:\</c>) or a shared folder (<c>\\server\</c>).</summary>
    private static bool IsWindowsPath(string line) =>
        (line.Length >= 3 && char.IsAsciiLetter(line[0]) && line[1] == ':' && line[2] is '\\' or '/') || line.StartsWith(@"\\", StringComparison.Ordinal);

    /// <summary>
    /// <c>.m3u8</c> is UTF-8. A plain <c>.m3u</c> with no byte-order mark is usually UTF-8 too, but
    /// Windows players often write it in the computer's own code page: if it isn't valid UTF-8,
    /// it's read that way. iTunes writes its text export in UTF-16, with a byte-order mark.
    /// </summary>
    internal static string ReadText(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble))
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.AsSpan().StartsWith(Encoding.Unicode.Preamble))
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.AsSpan().StartsWith(Encoding.BigEndianUnicode.Preamble))
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
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
