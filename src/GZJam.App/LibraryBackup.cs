using System.IO.Compression;
using System.Text.Json;
using GZJam.Library;
using Microsoft.Data.Sqlite;

namespace GZJam.App;

/// <summary>What a backup holds, as it says of itself: when, from which GZJam, and how much.</summary>
public sealed record BackupInfo(DateTime Created, string GZJamVersion, int Songs, int Playlists, int Nights, int MarkedSongs,
    IReadOnlyList<string> MusicFolders);

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
