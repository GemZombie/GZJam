using System.Globalization;
using Microsoft.Data.Sqlite;

namespace GZJam.Library;

/// <summary>A song in Mixxx's library, with what GZJam can use.</summary>
public sealed record MixxxTrack(long Id, string Path, string FileName, long? FileSize, string? Title, string? Artist, string? Album,
    TimeSpan Duration, int Stars, double? Bpm, bool BpmLocked, bool Hidden);

/// <summary>A crate, by name, with its songs' Mixxx ids.</summary>
public sealed record MixxxCrate(string Name, IReadOnlyList<long> TrackIds);

/// <summary>A night in Mixxx's History: the songs played, in order, with when (UTC).</summary>
public sealed record MixxxSession(string Name, IReadOnlyList<(long TrackId, DateTime PlayedUtc)> Plays);

/// <summary>
/// Reads a Mixxx library (mixxxdb.sqlite). Mixxx's database is copied first and only the copy is
/// read, so Mixxx (running or not) is never touched. Layout from Mixxx's res/schema.xml: songs in
/// library + track_locations, crates in crates + crate_tracks, History in playlists marked as set
/// logs (Playlists.hidden = 2), timestamps in UTC.
/// </summary>
public sealed class MixxxLibrary : IDisposable
{
    private const int SetLog = 2;   // Playlists.hidden for History (PlaylistDAO::PLHT_SET_LOG)

    private readonly string _copyFolder;
    private readonly SqliteConnection _db;

    private MixxxLibrary(string databasePath)
    {
        _copyFolder = Directory.CreateTempSubdirectory("gzjam-mixxx-").FullName;
        string copy = System.IO.Path.Combine(_copyFolder, "mixxxdb.sqlite");
        File.Copy(databasePath, copy);
        // Changes Mixxx hasn't folded into the database yet are in the write-ahead log.
        if (File.Exists(databasePath + "-wal"))
            File.Copy(databasePath + "-wal", copy + "-wal");
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = copy, Pooling = false }.ToString());
        _db.Open();
        Path = databasePath;
    }

    /// <summary>The Mixxx database this was read from.</summary>
    public string Path { get; }

    /// <summary>Opens (a copy of) the Mixxx library at <paramref name="databasePath"/>.</summary>
    public static MixxxLibrary Open(string databasePath) => new(databasePath);

    /// <summary>Where Mixxx keeps its library on this computer, if it's there.</summary>
    public static string? FindDefault()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] candidates = OperatingSystem.IsWindows()
            ? [System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mixxx", "mixxxdb.sqlite")]
            : OperatingSystem.IsMacOS()
                ? [System.IO.Path.Combine(home, "Library", "Containers", "org.mixxx.mixxx", "Data", "Library", "Application Support", "Mixxx", "mixxxdb.sqlite"),
                   System.IO.Path.Combine(home, "Library", "Application Support", "Mixxx", "mixxxdb.sqlite")]
                : [System.IO.Path.Combine(home, ".mixxx", "mixxxdb.sqlite"),
                   System.IO.Path.Combine(home, ".var", "app", "org.mixxx.Mixxx", ".mixxx", "mixxxdb.sqlite")];   // Flatpak
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Every song in Mixxx's library.</summary>
    public IReadOnlyList<MixxxTrack> Tracks()
    {
        var columns = Columns("library");
        string Column(string name, string fallback) => columns.Contains(name) ? "l." + name : fallback;

        using var cmd = _db.CreateCommand();
        cmd.CommandText = $"""
            SELECT l.id, tl.location, tl.filename, tl.filesize, l.title, l.artist, l.album, l.duration,
                   {Column("rating", "0")}, l.bpm, {Column("bpm_lock", "0")}, {Column("mixxx_deleted", "0")}
            FROM library l JOIN track_locations tl ON tl.id = l.location
            WHERE tl.location IS NOT NULL;
            """;
        using var r = cmd.ExecuteReader();
        var tracks = new List<MixxxTrack>();
        while (r.Read())
        {
            string path = NativePath(r.GetString(1));
            double bpm = r.IsDBNull(9) ? 0 : r.GetDouble(9);
            tracks.Add(new MixxxTrack(
                r.GetInt64(0),
                path,
                r.IsDBNull(2) ? System.IO.Path.GetFileName(path) : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetInt64(3),
                Text(r, 4), Text(r, 5), Text(r, 6),
                TimeSpan.FromSeconds(r.IsDBNull(7) ? 0 : r.GetDouble(7)),
                r.IsDBNull(8) ? 0 : Math.Clamp(r.GetInt32(8), 0, 5),
                bpm > 0 ? bpm : null,
                !r.IsDBNull(10) && r.GetInt32(10) != 0,
                !r.IsDBNull(11) && r.GetInt32(11) != 0));
        }
        return tracks;
    }

    /// <summary>Crates with their songs.</summary>
    public IReadOnlyList<MixxxCrate> Crates()
    {
        var crates = new Dictionary<long, (string Name, List<long> Tracks)>();
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT id, name FROM crates WHERE name IS NOT NULL AND trim(name) <> '' ORDER BY name;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                crates[r.GetInt64(0)] = (r.GetString(1).Trim(), []);
        }
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT crate_id, track_id FROM crate_tracks;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (crates.TryGetValue(r.GetInt64(0), out var crate))
                    crate.Tracks.Add(r.GetInt64(1));
            }
        }
        return crates.Values.Select(c => new MixxxCrate(c.Name, c.Tracks)).ToList();
    }

    /// <summary>History: Mixxx's set logs, oldest first, each with its songs in the order played.</summary>
    public IReadOnlyList<MixxxSession> History()
    {
        if (!Columns("PlaylistTracks").Contains("pl_datetime_added"))
            return [];
        var sessions = new Dictionary<long, (string Name, List<(long, DateTime)> Plays)>();
        using var cmd = _db.CreateCommand();
        cmd.CommandText = $"""
            SELECT p.id, p.name, pt.track_id, pt.pl_datetime_added
            FROM Playlists p JOIN PlaylistTracks pt ON pt.playlist_id = p.id
            WHERE p.hidden = {SetLog} AND pt.pl_datetime_added IS NOT NULL
            ORDER BY p.id, pt.position;
            """;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            if (!TryUtc(r.GetValue(3), out DateTime playedUtc))
                continue;
            long id = r.GetInt64(0);
            if (!sessions.TryGetValue(id, out var session))
                sessions[id] = session = (Text(r, 1) ?? "", []);
            session.Plays.Add((r.GetInt64(2), playedUtc));
        }
        return sessions.Values
            .Select(s => new MixxxSession(s.Name, s.Plays))
            .OrderBy(s => s.Plays[0].PlayedUtc)
            .ToList();
    }

    private HashSet<string> Columns(string table)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var r = cmd.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (r.Read())
            columns.Add(r.GetString(1));
        return columns;
    }

    /// <summary>Mixxx (Qt) writes paths with forward slashes, on Windows too.</summary>
    private static string NativePath(string path) =>
        System.IO.Path.DirectorySeparatorChar == '\\' ? path.Replace('/', '\\') : path;

    private static string? Text(SqliteDataReader r, int i) =>
        r.IsDBNull(i) || string.IsNullOrWhiteSpace(r.GetString(i)) ? null : r.GetString(i).Trim();

    /// <summary>SQLite's CURRENT_TIMESTAMP ("2024-03-14 21:05:33", UTC), or an ISO date-time.</summary>
    private static bool TryUtc(object value, out DateTime utc)
    {
        utc = default;
        if (value is not string text)
            return false;
        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc))
            return false;
        utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return true;
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_copyFolder, recursive: true); }
        catch (IOException) { /* best effort: it's in the temp folder */ }
    }
}
