using System.Globalization;
using System.Text;

namespace GZJam.Library;

/// <summary>
/// Copies a playlist's songs (or the queue's, or a night's from History) into a new folder, in
/// order, as <c>01 - Artist - Title.mp3</c>, with an <c>.m3u8</c> playlist beside them (#101): a
/// spare copy of a set on a USB stick, or one to hand to another DJ. Copies only: the songs are
/// never moved or changed, and nothing is overwritten (the folder is new, named after the set).
/// </summary>
public sealed class SongExport
{
    /// <summary>Room left on the drive after the copy, so it isn't filled to the last byte.</summary>
    private const long Spare = 16 * 1024 * 1024;

    /// <summary>Longest file name written, well under every file system's limit (and old players').</summary>
    private const int LongestName = 120;

    private SongExport(string name, string folder, IReadOnlyList<(Track Song, string FileName)> files,
        IReadOnlyList<Track> missing, long bytes, long? freeBytes)
    {
        Name = name;
        Folder = folder;
        Files = files;
        Missing = missing;
        Bytes = bytes;
        FreeBytes = freeBytes;
    }

    /// <summary>The set's name, as given.</summary>
    public string Name { get; }

    /// <summary>The new folder the songs go into.</summary>
    public string Folder { get; }

    /// <summary>The songs found, in order, with the names they're copied under.</summary>
    public IReadOnlyList<(Track Song, string FileName)> Files { get; }

    /// <summary>Songs whose files aren't there (left out).</summary>
    public IReadOnlyList<Track> Missing { get; }

    /// <summary>How much the copy takes.</summary>
    public long Bytes { get; }

    /// <summary>The room on the drive, if it could be told.</summary>
    public long? FreeBytes { get; }

    public bool HasRoom => FreeBytes is not { } free || free >= Bytes + Spare;

    /// <summary>The playlist file written beside the songs.</summary>
    public string PlaylistPath => Path.Combine(Folder, SafeName(Name, "Playlist") + ".m3u8");

    /// <summary>Works out what the copy of <paramref name="songs"/> into a new folder in <paramref name="into"/> would be.</summary>
    public static SongExport Plan(string name, IReadOnlyList<Track> songs, string into)
    {
        into = Path.GetFullPath(into);
        string folder = NewFolder(into, SafeName(name, "GZJam songs"));
        var found = songs.Where(s => File.Exists(s.Path)).ToList();
        int digits = Math.Max(2, found.Count.ToString(CultureInfo.InvariantCulture).Length);
        var files = found.Select((song, i) => (song, FileName(i + 1, digits, song))).ToList();
        long bytes = found.Sum(s => new FileInfo(s.Path).Length);
        return new SongExport(name, folder, files, songs.Where(s => !File.Exists(s.Path)).ToList(), bytes, FreeSpace(into));
    }

    /// <summary>Copies the songs and writes the playlist. On a failure the song being copied is removed and the error thrown.</summary>
    public async Task CopyAsync(IProgress<int>? copied = null, CancellationToken cancel = default)
    {
        Directory.CreateDirectory(Folder);
        for (int i = 0; i < Files.Count; i++)
        {
            var (song, fileName) = Files[i];
            string to = Path.Combine(Folder, fileName);
            try
            {
                await using (var source = new FileStream(song.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true))
                await using (var target = new FileStream(to, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
                    await source.CopyToAsync(target, cancel);
                File.SetLastWriteTimeUtc(to, File.GetLastWriteTimeUtc(song.Path));
            }
            catch
            {
                TryDelete(to);
                throw;
            }
            copied?.Report(i + 1);
        }
        await File.WriteAllTextAsync(PlaylistPath, Playlist(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancel);
    }

    /// <summary>The <c>.m3u8</c>: the songs by their names in the folder, so it plays from wherever the folder is.</summary>
    internal string Playlist()
    {
        var text = new StringBuilder("#EXTM3U\n");
        foreach (var (song, fileName) in Files)
        {
            int seconds = song.Duration > TimeSpan.Zero ? (int)Math.Round(song.Duration.TotalSeconds) : -1;
            string shown = string.IsNullOrWhiteSpace(song.Artist) ? song.DisplayTitle : $"{song.Artist} - {song.DisplayTitle}";
            text.Append(CultureInfo.InvariantCulture, $"#EXTINF:{seconds},{shown}\n{fileName}\n");
        }
        return text.ToString();
    }

    /// <summary>"07 - Count Basie - Jumpin' at the Woodside.mp3".</summary>
    internal static string FileName(int number, int digits, Track song)
    {
        string extension = Path.GetExtension(song.Path).ToLowerInvariant();
        string artist = string.IsNullOrWhiteSpace(song.Artist) ? "" : song.Artist.Trim() + " - ";
        string name = SafeName($"{number.ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0')} - {artist}{song.DisplayTitle}", "Song");
        if (name.Length > LongestName - extension.Length)
            name = name[..(LongestName - extension.Length)].TrimEnd(' ', '.');
        return name + extension;
    }

    /// <summary>
    /// A name any file system takes, Windows' and a USB stick's (FAT32, exFAT) included: no
    /// <c>\ / : * ? " &lt; &gt; |</c> or control characters, and no dot or space at the end.
    /// </summary>
    internal static string SafeName(string name, string fallback)
    {
        var safe = new StringBuilder(name.Length);
        foreach (char c in name)
            safe.Append(char.IsControl(c) || "\\/:*?\"<>|".Contains(c) ? '_' : c);
        string result = safe.ToString().Trim().TrimEnd('.', ' ');
        return result.Length == 0 ? fallback : result;
    }

    /// <summary><paramref name="name"/> in <paramref name="into"/>, or "name (2)" and so on if that's taken.</summary>
    private static string NewFolder(string into, string name)
    {
        string folder = Path.Combine(into, name);
        for (int n = 2; Directory.Exists(folder) || File.Exists(folder); n++)
            folder = Path.Combine(into, $"{name} ({n})");
        return folder;
    }

    /// <summary>The room on the drive holding <paramref name="path"/>: the drive whose root is the longest match.</summary>
    private static long? FreeSpace(string path)
    {
        try
        {
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            DriveInfo? drive = DriveInfo.GetDrives()
                .Where(d => d.IsReady && path.StartsWith(d.RootDirectory.FullName, comparison))
                .MaxBy(d => d.RootDirectory.FullName.Length);
            return drive?.AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
