using System.IO.Compression;
using System.Text.Json;
using GZJam.Library;
using Microsoft.Data.Sqlite;

namespace GZJam.App;

/// <summary>What a backup holds, as it says of itself: when, from which GZJam, and how much.</summary>
public sealed record BackupInfo(DateTime Created, string GZJamVersion, int Songs, int Playlists, int Nights, int MarkedSongs,
    IReadOnlyList<string> MusicFolders)
{
    /// <summary>Restored where the music lives somewhere else (#112): each music folder moved, as said once it's open.</summary>
    public IReadOnlyList<string>? Moved { get; init; }
}

/// <summary>
/// Backing up and restoring the library (#84): everything the DJ has given their songs (hearts,
/// labels, cues, trims, EQ, BPMs, playlists, History) and the waveforms and analysis, so a
/// restore needs nothing worked out again, in one ".gzjam" file with the settings. It's a zip:
/// <c>library.db</c>, <c>settings.json</c> and <c>backup.json</c> (what it holds). The music
/// files aren't in it: GZJam never changes them. Restoring is done at the next start, before
/// the library is opened, and the library it replaces is kept aside, as starting over does.
/// </summary>
internal static class LibraryBackup
{
    public const string Extension = ".gzjam";

    /// <summary>Automatic copies, beside the library: one a day GZJam is opened, the last few kept.</summary>
    public const string AutomaticFolder = "backups";
    public const int AutomaticKept = 5;

    /// <summary>Where a restore waits for the next start, beside the library.</summary>
    private const string PendingFolder = "restore";

    private const string LibraryEntry = "library.db";
    private const string SettingsEntry = "settings.json";
    private const string InfoEntry = "backup.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>"GZJam backup 2026-10-25.gzjam"</summary>
    public static string SuggestedName(DateTime now) => $"GZJam backup {now:yyyy-MM-dd}{Extension}";

    /// <summary>
    /// Backs up the library (while it's in use) and <paramref name="settingsJson"/> to
    /// <paramref name="destination"/>, replacing it whole or not at all. Returns what it holds.
    /// </summary>
    public static BackupInfo Create(LibraryDatabase database, string settingsJson, string destination)
    {
        string work = Directory.CreateTempSubdirectory("gzjam-backup-").FullName;
        try
        {
            string copy = Path.Combine(work, LibraryEntry);
            database.BackUpTo(copy);
            BackupInfo info = Describe(copy, DateTime.Now, About.Version);

            string temp = destination + ".tmp";
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(copy, LibraryEntry, CompressionLevel.Optimal);
                Write(zip, SettingsEntry, settingsJson);
                Write(zip, InfoEntry, JsonSerializer.Serialize(info, JsonOptions));
            }
            File.Move(temp, destination, overwrite: true);
            return info;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(work);
        }
    }

    private static void Write(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open());
        writer.Write(text);
    }

    /// <summary>Counts what a library copy holds.</summary>
    private static BackupInfo Describe(string databasePath, DateTime created, string version)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        c.Open();
        int Count(string sql)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        var folders = new List<string>();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT path FROM roots ORDER BY path;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                folders.Add(r.GetString(0));
        }
        return new BackupInfo(created, version,
            Count("SELECT COUNT(*) FROM tracks;"),
            Count("SELECT COUNT(*) FROM playlists;"),
            Count("SELECT COUNT(*) FROM sessions;"),
            Count("SELECT COUNT(*) FROM (SELECT path FROM marks UNION SELECT path FROM track_labels UNION SELECT path FROM cues);"),
            folders);
    }

    /// <summary>What a backup holds, read from it.</summary>
    /// <exception cref="InvalidDataException">It isn't a GZJam backup, or it's damaged.</exception>
    public static BackupInfo Read(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            if (zip.GetEntry(LibraryEntry) is null || zip.GetEntry(InfoEntry) is not { } entry)
                throw new InvalidDataException("It isn't a GZJam backup.");
            using var reader = new StreamReader(entry.Open());
            return JsonSerializer.Deserialize<BackupInfo>(reader.ReadToEnd(), JsonOptions)
                ?? throw new InvalidDataException("It isn't a GZJam backup.");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new InvalidDataException("It isn't a GZJam backup, or it's damaged.", ex);
        }
    }

    /// <summary>
    /// Readies a backup to be restored at the next start: unpacks it beside the library, and
    /// checks this GZJam can open it.
    /// </summary>
    /// <exception cref="InvalidDataException">It isn't a GZJam backup, or it's damaged.</exception>
    /// <exception cref="LibraryTooNewException">It's from a newer GZJam, which this one can't read.</exception>
    public static void Stage(string backupPath, string dataDirectory)
    {
        string pending = Path.Combine(dataDirectory, PendingFolder);
        TryDelete(pending);
        Directory.CreateDirectory(pending);
        try
        {
            using (var zip = ZipFile.OpenRead(backupPath))
            {
                if (zip.GetEntry(LibraryEntry) is not { } library || zip.GetEntry(InfoEntry) is not { } info)
                    throw new InvalidDataException("It isn't a GZJam backup.");
                library.ExtractToFile(Path.Combine(pending, LibraryEntry));
                info.ExtractToFile(Path.Combine(pending, InfoEntry));
                zip.GetEntry(SettingsEntry)?.ExtractToFile(Path.Combine(pending, SettingsEntry));
            }
            string staged = Path.Combine(pending, LibraryEntry);
            if (LibraryDatabase.CheckCanOpen(staged) is { } tooNew)
                throw tooNew;
            if (!LooksLikeALibrary(staged))
                throw new InvalidDataException("Its library is damaged.");
        }
        catch
        {
            SqliteConnection.ClearAllPools();
            TryDelete(pending);
            throw;
        }
    }

    private static bool LooksLikeALibrary(string path)
    {
        try
        {
            using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA quick_check; SELECT COUNT(*) FROM tracks;";
            using var r = cmd.ExecuteReader();
            return r.Read() && r.GetString(0) == "ok";
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    // ───────────── Restoring where the music lives somewhere else (#112) ─────────────

    /// <summary>The backup's music folders that aren't on this computer.</summary>
    public static IReadOnlyList<string> MissingMusicFolders(BackupInfo info) => info.MusicFolders.Where(f => !Directory.Exists(f)).ToList();

    /// <summary>The readied backup's songs in one of its music folders, by path.</summary>
    public static IReadOnlyList<string> StagedSongs(string dataDirectory, string musicFolder)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataDirectory, PendingFolder, LibraryEntry),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT t.path FROM tracks t JOIN roots r ON r.id = t.root_id WHERE r.path = $root;";
        cmd.Parameters.AddWithValue("$root", musicFolder);
        using var r = cmd.ExecuteReader();
        var songs = new List<string>();
        while (r.Read())
            songs.Add(r.GetString(0));
        return songs;
    }

    /// <summary>How many of a music folder's <paramref name="songs"/> are in their places under <paramref name="to"/>.</summary>
    public static int CountFound(IReadOnlyList<string> songs, string from, string to) =>
        songs.Count(song => LibraryDatabase.MovedPath(song, from, to) is { } moved && File.Exists(moved));

    /// <summary>
    /// Where a music folder that isn't on this computer likely is now: a folder of the same name
    /// (among <paramref name="lookIn"/>, the Music folder, the home folder, each drive and outside
    /// disk) with the backup's songs in it, the most of them. Null if none has any.
    /// </summary>
    public static string? Suggest(string from, IReadOnlyList<string> songs, IEnumerable<string> lookIn)
    {
        string name = from.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
        if (name.Length == 0 || songs.Count == 0)
            return null;
        var sample = songs.Where((_, i) => i % Math.Max(1, songs.Count / 40) == 0).Take(40).ToList();
        var places = new List<string>(lookIn);
        string music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        places.AddRange([music, home]);
        if (name.Equals("Music", StringComparison.OrdinalIgnoreCase) || name.Equals("My Music", StringComparison.OrdinalIgnoreCase))
            places.Add(Path.Combine(music, ".."));   // the Music folder itself, as a folder of that name
        foreach (string disks in new[] { "/Volumes", $"/media/{Environment.UserName}", $"/run/media/{Environment.UserName}", "/mnt" })
            places.AddRange(Children(disks));
        try
        {
            places.AddRange(DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.RootDirectory.FullName));
        }
        catch (IOException)
        {
        }

        var candidates = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (string place in places.Where(p => p.Length > 0))
        {
            string full = Path.GetFullPath(place);
            if (Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar)).Equals(name, StringComparison.OrdinalIgnoreCase))
                candidates.Add(full);
            candidates.Add(Path.Combine(full, name));
        }
        return candidates.Where(Directory.Exists)
            .Select(c => (Folder: c, Found: CountFound(sample, from, c)))
            .Where(c => c.Found > 0)
            .OrderByDescending(c => c.Found)
            .Select(c => c.Folder)
            .FirstOrDefault();
    }

    private static IEnumerable<string> Children(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.GetDirectories(folder) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Moves the readied backup's music folders where they are on this computer (the library's
    /// paths, and the folder its Settings had open), and notes it for saying once it's open.
    /// </summary>
    public static void MoveStaged(string dataDirectory, IReadOnlyList<(string From, string To)> moves, IReadOnlyList<string> notes)
    {
        string pending = Path.Combine(dataDirectory, PendingFolder);
        if (moves.Count > 0)
            LibraryDatabase.MoveMusicFolders(Path.Combine(pending, LibraryEntry), moves);

        string settings = Path.Combine(pending, SettingsEntry);
        if (moves.Count > 0 && File.Exists(settings) && System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(settings)) is System.Text.Json.Nodes.JsonObject json
            && json["SelectedRootPath"]?.GetValue<string>() is { } open && moves.FirstOrDefault(m => m.From == open) is { To: { } to })
        {
            json["SelectedRootPath"] = to;
            File.WriteAllText(settings, json.ToJsonString(JsonOptions));
        }

        string infoPath = Path.Combine(pending, InfoEntry);
        BackupInfo info = JsonSerializer.Deserialize<BackupInfo>(File.ReadAllText(infoPath), JsonOptions)!;
        File.WriteAllText(infoPath, JsonSerializer.Serialize(info with { Moved = notes }, JsonOptions));
        SqliteConnection.ClearAllPools();
    }

    /// <summary>Lets go of a readied restore the DJ thought better of.</summary>
    public static void Unstage(string dataDirectory)
    {
        SqliteConnection.ClearAllPools();
        TryDelete(Path.Combine(dataDirectory, PendingFolder));
    }

    /// <summary>
    /// If a restore was readied: moves the library aside, puts the backup's in its place, and its
    /// settings in place of these (kept aside too). Returns what was restored and where the old
    /// library went; otherwise null. Called as GZJam starts, before the settings are read.
    /// </summary>
    public static (BackupInfo Restored, string Kept)? ApplyPending(string databasePath, string settingsFile)
    {
        string dataDirectory = Path.GetDirectoryName(Path.GetFullPath(databasePath))!;
        string pending = Path.Combine(dataDirectory, PendingFolder);
        string staged = Path.Combine(pending, LibraryEntry);
        if (!File.Exists(staged))
            return null;

        BackupInfo info = JsonSerializer.Deserialize<BackupInfo>(File.ReadAllText(Path.Combine(pending, InfoEntry)), JsonOptions)!;
        string kept = LibraryReset.MoveAside(databasePath, "before restoring a backup");
        File.Move(staged, databasePath);
        string settings = Path.Combine(pending, SettingsEntry);
        if (File.Exists(settings))
        {
            if (File.Exists(settingsFile))
                File.Copy(settingsFile, Path.Combine(kept, SettingsEntry), overwrite: true);
            Directory.CreateDirectory(Path.GetDirectoryName(settingsFile)!);
            File.Move(settings, settingsFile, overwrite: true);
        }
        TryDelete(pending);
        return (info, kept);
    }

    /// <summary>
    /// The automatic copy: if none has been made today, backs up into the "backups" folder beside
    /// the library, and lets go of the oldest beyond <see cref="AutomaticKept"/>. Returns the new
    /// copy's path, or null if today's was already made.
    /// </summary>
    public static string? KeepAutomatic(LibraryDatabase database, string settingsJson, DateTime now)
    {
        string folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(database.DatabasePath))!, AutomaticFolder);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"GZJam automatic backup {now:yyyy-MM-dd}{Extension}");
        if (File.Exists(path))
            return null;
        Create(database, settingsJson, path);
        foreach (string old in Directory.GetFiles(folder, "GZJam automatic backup *" + Extension).Order(StringComparer.Ordinal)
                     .SkipLast(AutomaticKept))
            File.Delete(old);
        return path;
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Couldn't delete {directory}", ex);
        }
    }
}
