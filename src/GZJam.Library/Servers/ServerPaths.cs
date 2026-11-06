using System.Text;

namespace GZJam.Library.Servers;

/// <summary>
/// Where a server's song goes on this computer (#283, as settled on #184): the server's own
/// folder layout under <c>&lt;downloads folder&gt;/&lt;server&gt;/</c>, so the library's folder tree
/// looks like the server. A real path drops the server's own mount folder (Navidrome's
/// <c>/music/</c>); a path made up from the tags, or none, becomes
/// <c>{Album Artist}/{Album}/{Track:00} - {Title}</c>. Names are made safe on every system.
/// </summary>
public static class ServerPaths
{
    /// <summary>Longest a path may get on Windows before it needs special handling.</summary>
    private const int LongestPath = 250;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>The song's place under its server's folder, as '/'-separated names, each safe.</summary>
    public static IReadOnlyList<string> RelativeParts(ServerSong song)
    {
        string extension = Extension(song);
        var parts = FromServerPath(song.Path);
        if (parts is null)
        {
            string artist = First(song.AlbumArtist, song.Artist) ?? "Unknown artist";
            string album = First(song.Album) ?? "Unknown album";
            string title = First(song.Title) ?? song.Id;
            string number = song.Track is { } track and > 0
                ? (song.Disc is { } disc and > 1 ? $"{disc}-" : "") + $"{track:00} - "
                : "";
            parts = [artist, album, number + title + extension];
        }
        else if (!parts[^1].EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            parts[^1] += extension;   // a made-up path without the file's type
        return parts.Select(Safe).ToList();
    }

    /// <summary>
    /// Where a song shows in the server's folder tree (#290): its real folders, the server's own
    /// mount folder left off, or Album Artist / Album when the server gives no real path. The
    /// names as the server has them ('/'-separated); a file name too.
    /// </summary>
    public static (string Folder, string FileName) CatalogPlace(ServerSong song)
    {
        if (FromServerPath(song.Path) is { } parts)
            return (string.Join('/', parts[..^1]), parts[^1]);
        string artist = First(song.AlbumArtist, song.Artist) ?? "Unknown artist";
        string album = First(song.Album) ?? "Unknown album";
        return ($"{artist.Replace('/', '-')}/{album.Replace('/', '-')}", (First(song.Title) ?? song.Id) + Extension(song));
    }

    /// <summary>
    /// A server path's folders and file name, the server's own mount folder left off: null if
    /// there's no path, or only a file name (no layout to copy).
    /// </summary>
    private static List<string>? FromServerPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        string text = path.Replace('\\', '/');
        bool absolute = text.StartsWith('/') || (text.Length > 1 && text[1] == ':');
        var parts = text.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(p => p is not ("." or "..")).ToList();
        if (absolute)
        {
            if (parts.Count > 0 && parts[0].EndsWith(':'))
                parts.RemoveAt(0);   // C:
            if (parts.Count > 2)
                parts.RemoveAt(0);   // the folder the server's music is mounted at: /music
        }
        return parts.Count >= 2 ? parts : null;
    }

    private static string Extension(ServerSong song)
    {
        string? suffix = song.Suffix?.Trim().TrimStart('.');
        if (string.IsNullOrEmpty(suffix))
            suffix = System.IO.Path.GetExtension(song.Path ?? "").TrimStart('.');
        return string.IsNullOrEmpty(suffix) ? ".mp3" : "." + suffix.ToLowerInvariant();
    }

    private static string? First(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    /// <summary>
    /// A name every system takes: the characters Windows refuses (<c>&lt; &gt; : " / \ | ? *</c> and
    /// controls) become <c>_</c>, no dot or space at the end, not a name Windows keeps for itself.
    /// </summary>
    public static string Safe(string name)
    {
        var text = new StringBuilder(name.Length);
        foreach (char ch in name.Normalize(NormalizationForm.FormC))
            text.Append(ch < ' ' || ch is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*' ? '_' : ch);
        string safe = text.ToString().Trim().TrimEnd('.', ' ');
        if (safe.Length == 0)
            return "_";
        string stem = safe.Split('.')[0];
        return ReservedNames.Contains(stem) ? "_" + safe : safe;
    }

    /// <summary>A server's folder under the downloads folder, from its name.</summary>
    public static string ServerFolder(string downloadsFolder, string serverName) =>
        System.IO.Path.Combine(downloadsFolder, Safe(serverName));

    /// <summary>
    /// Where to put the song: its place under <paramref name="serverFolder"/>, made shorter if it
    /// would pass Windows' path limit, and given a number ("… (2).mp3") if another song is there
    /// already in any case (<paramref name="taken"/>), since Windows and Macs don't tell
    /// "Count Basie" from "count basie".
    /// </summary>
    public static string Place(string serverFolder, IReadOnlyList<string> parts, Func<string, bool> taken)
    {
        var names = parts.ToList();
        string Join() => System.IO.Path.Combine([serverFolder, .. names]);

        // Too long: shorten the folders first, then the file name, keeping its type.
        for (int limit = 80; Join().Length > LongestPath && limit >= 16; limit -= 16)
            for (int i = 0; i < names.Count; i++)
            {
                bool file = i == names.Count - 1;
                string ext = file ? System.IO.Path.GetExtension(names[i]) : "";
                string stem = file ? names[i][..^ext.Length] : names[i];
                if (stem.Length > limit)
                    names[i] = Safe(stem[..limit]) + ext;
            }

        string path = Join();
        string extension = System.IO.Path.GetExtension(path);
        string without = path[..^extension.Length];
        for (int n = 2; taken(path); n++)
            path = $"{without} ({n}){extension}";
        return path;
    }
}
