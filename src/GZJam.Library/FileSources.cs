namespace GZJam.Library;

/// <summary>What a place in Files is (#318): the computer, one of its places or drives, a USB stick, or a folder in one.</summary>
public enum FilesKind
{
    /// <summary>Not in Files: a music folder, or a server's.</summary>
    None,

    /// <summary>"This computer": its places and drives under it.</summary>
    Computer,

    /// <summary>Desktop, Downloads, Music: the computer's own places.</summary>
    Place,

    /// <summary>One of the computer's own drives.</summary>
    Drive,

    /// <summary>A USB stick (or card, or other removable drive): it comes and goes.</summary>
    Stick,

    /// <summary>A folder in any of those.</summary>
    Folder,
}

/// <summary>A place to start from in Files: its name, where it is, and what it is.</summary>
public sealed record FilePlace(string Name, string Path, FilesKind Kind);

/// <summary>
/// The computer's files, for the library's Files source (#318): its places, its drives and any
/// USB stick plugged in, each a folder to open. Never searched, never added to the library: a
/// folder's songs are read when it's opened.
/// </summary>
public static class FileSources
{
    private static StringComparison PathComparison => OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>The computer's own places that are there: Desktop, Downloads, Music.</summary>
    public static IReadOnlyList<FilePlace> Places()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        (string Name, string Path)[] places =
        [
            ("Desktop", desktop.Length > 0 ? desktop : Path.Combine(home, "Desktop")),
            ("Downloads", Path.Combine(home, "Downloads")),
            ("Music", music.Length > 0 ? music : Path.Combine(home, "Music")),
        ];
        return places.Where(p => Directory.Exists(p.Path)).Select(p => new FilePlace(p.Name, p.Path, FilesKind.Place)).ToList();
    }

    /// <summary>The computer's own drives (Windows: C:, D:…); elsewhere, the places are enough.</summary>
    public static IReadOnlyList<FilePlace> Drives()
    {
        if (!OperatingSystem.IsWindows())
            return [];
        var drives = new List<FilePlace>();
        foreach (DriveInfo drive in SafeDrives())
        {
            try
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
                    continue;
                drives.Add(new FilePlace(DriveName(drive), drive.RootDirectory.FullName, FilesKind.Drive));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // a drive that won't say: left out
            }
        }
        return drives;
    }

    /// <summary>The USB sticks (and cards) plugged in now, by name ("SANDISK (E:)").</summary>
    public static IReadOnlyList<FilePlace> Sticks()
    {
        var sticks = new List<FilePlace>();
        if (OperatingSystem.IsWindows())
        {
            foreach (DriveInfo drive in SafeDrives())
            {
                try
                {
                    if (drive.DriveType == DriveType.Removable && drive.IsReady)
                        sticks.Add(new FilePlace(DriveName(drive), drive.RootDirectory.FullName, FilesKind.Stick));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // pulled out as we looked
                }
            }
            return sticks;
        }
        // Linux mounts a stick under /media/<user> or /run/media/<user>; a Mac under /Volumes.
        string user = Environment.UserName;
        string[] mountPlaces = OperatingSystem.IsMacOS() ? ["/Volumes"] : [$"/media/{user}", $"/run/media/{user}"];
        foreach (string place in mountPlaces)
        {
            try
            {
                if (!Directory.Exists(place))
                    continue;
                foreach (string mount in Directory.EnumerateDirectories(place))
                {
                    var info = new DirectoryInfo(mount);
                    if (info.LinkTarget is not null)
                        continue;   // the Mac's own disk, linked in /Volumes
                    sticks.Add(new FilePlace(info.Name, mount, FilesKind.Stick));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // nothing there to list
            }
        }
        return sticks;
    }

    private static DriveInfo[] SafeDrives()
    {
        try
        {
            return DriveInfo.GetDrives();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>"SANDISK (E:)", or "Local Disk (C:)" for a drive with no name.</summary>
    private static string DriveName(DriveInfo drive)
    {
        string letter = drive.Name.TrimEnd(Path.DirectorySeparatorChar);
        string label = "";
        try
        {
            label = drive.VolumeLabel;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // no name to give
        }
        if (label.Length == 0)
            label = drive.DriveType == DriveType.Removable ? "USB stick" : "Local Disk";
        return $"{label} ({letter})";
    }

    /// <summary>Whether <paramref name="path"/> is in <paramref name="folder"/> or below it.</summary>
    public static bool IsIn(string path, string folder)
    {
        string under = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
        return path.StartsWith(under, PathComparison) || string.Equals(Path.TrimEndingDirectorySeparator(path), Path.TrimEndingDirectorySeparator(folder), PathComparison);
    }

    /// <summary>The folders in <paramref name="folder"/>, by name, hidden ones left out; none if it can't be read.</summary>
    public static IReadOnlyList<FilePlace> Folders(string folder)
    {
        try
        {
            var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
            return Directory.EnumerateDirectories(folder, "*", options)
                .Select(d => new FilePlace(Path.GetFileName(d), d, FilesKind.Folder))
                .Where(d => !d.Name.StartsWith('.'))
                .OrderBy(d => d.Name, NaturalComparer.Instance)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The songs in <paramref name="folder"/> itself (not its folders), by name; none if it can't be read.</summary>
    public static IReadOnlyList<FileInfo> Songs(string folder)
    {
        try
        {
            var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
            return new DirectoryInfo(folder).EnumerateFiles("*", options)
                .Where(f => LibraryScanner.IsAudioFile(f.FullName))
                .OrderBy(f => f.Name, NaturalComparer.Instance)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The songs in <paramref name="folder"/> and every folder in it, each with where it is from there ('/'-separated folder).</summary>
    public static IReadOnlyList<(FileInfo File, string Folder)> SongsBelow(string folder, CancellationToken cancel = default)
    {
        var songs = new List<(FileInfo, string)>();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };
        try
        {
            foreach (FileInfo file in new DirectoryInfo(folder).EnumerateFiles("*", options))
            {
                cancel.ThrowIfCancellationRequested();
                if (LibraryScanner.IsAudioFile(file.FullName))
                    songs.Add((file, TagReader.RelativeFolder(folder, file.DirectoryName!)));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // what could be read
        }
        return songs.OrderBy(s => s.Item2, NaturalComparer.Instance).ThenBy(s => s.Item1.Name, NaturalComparer.Instance).ToList();
    }

    /// <summary>A name safe for a file or folder on any computer: the characters none allows taken out.</summary>
    public static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['<', '>', ':', '"', '/', '\\', '|', '?', '*']).ToHashSet();
        string safe = string.Concat(name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c)).Trim().TrimEnd('.');
        return safe.Length == 0 ? "_" : safe;
    }
}
