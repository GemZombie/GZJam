using System.Text;
using Microsoft.Data.Sqlite;

namespace GZJam.Library;

/// <summary>
/// The library index: a SQLite database over the user's music folders. The folders on disk stay
/// the source of truth; this only caches tags and holds data the app owns.
/// Thread-safe: every call opens its own pooled connection, and WAL mode lets searches run
/// while a scan is writing.
/// </summary>
public sealed partial class LibraryDatabase
{

    private readonly string _connectionString;

    public LibraryDatabase(string databasePath)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(databasePath))!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
            Pooling = true,
        }.ToString();
        DatabasePath = databasePath;
        CreateSchema();
    }

    public string DatabasePath { get; }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void CreateSchema()
    {
        using var c = Open();
        Execute(c, "PRAGMA journal_mode = WAL;");

        long version = (long)(Scalar(c, "PRAGMA user_version;") ?? 0L);
        if (version == Migrations.Length)
            return;
        if (version > Migrations.Length)
            throw new InvalidOperationException($"Library database is from a newer version of GZJam ({version}).");

        // Apply each upgrade the database hasn't had yet, all or nothing.
        using var tx = c.BeginTransaction();
        for (long step = version; step < Migrations.Length; step++)
            Execute(c, Migrations[step]);
        Execute(c, $"PRAGMA user_version = {Migrations.Length};");
        tx.Commit();
    }

    /// <summary>
    /// Schema upgrades in order; the database's user_version says how many have been applied.
    /// Only ever append: existing databases depend on the earlier steps staying as they are.
    /// </summary>
    private static readonly string[] Migrations =
    [
        // 1: the library index.
        """
            CREATE TABLE roots (
                id   INTEGER PRIMARY KEY,
                path TEXT NOT NULL UNIQUE
            );

            CREATE TABLE tracks (
                id           INTEGER PRIMARY KEY,
                root_id      INTEGER NOT NULL REFERENCES roots(id) ON DELETE CASCADE,
                path         TEXT NOT NULL UNIQUE,
                folder       TEXT NOT NULL,
                file_name    TEXT NOT NULL,
                file_size    INTEGER NOT NULL,
                modified_utc INTEGER NOT NULL,
                title        TEXT,
                artist       TEXT,
                album        TEXT,
                album_artist TEXT,
                genre        TEXT,
                comment      TEXT,
                year         INTEGER,
                track_number INTEGER,
                bpm          REAL,
                duration_ms  INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX ix_tracks_root_folder ON tracks(root_id, folder);

            -- Full-text index over tags *and* folder names, so 'balboa' finds tracks in a Balboa folder.
            CREATE VIRTUAL TABLE tracks_fts USING fts5(
                title, artist, album, album_artist, genre, comment, folder, file_name,
                content = 'tracks', content_rowid = 'id',
                tokenize = 'unicode61 remove_diacritics 2'
            );
            CREATE TRIGGER tracks_ai AFTER INSERT ON tracks BEGIN
                INSERT INTO tracks_fts(rowid, title, artist, album, album_artist, genre, comment, folder, file_name)
                VALUES (new.id, new.title, new.artist, new.album, new.album_artist, new.genre, new.comment, new.folder, new.file_name);
            END;
            CREATE TRIGGER tracks_ad AFTER DELETE ON tracks BEGIN
                INSERT INTO tracks_fts(tracks_fts, rowid, title, artist, album, album_artist, genre, comment, folder, file_name)
                VALUES ('delete', old.id, old.title, old.artist, old.album, old.album_artist, old.genre, old.comment, old.folder, old.file_name);
            END;
            CREATE TRIGGER tracks_au AFTER UPDATE ON tracks BEGIN
                INSERT INTO tracks_fts(tracks_fts, rowid, title, artist, album, album_artist, genre, comment, folder, file_name)
                VALUES ('delete', old.id, old.title, old.artist, old.album, old.album_artist, old.genre, old.comment, old.folder, old.file_name);
                INSERT INTO tracks_fts(rowid, title, artist, album, album_artist, genre, comment, folder, file_name)
                VALUES (new.id, new.title, new.artist, new.album, new.album_artist, new.genre, new.comment, new.folder, new.file_name);
            END;
            """,

        // 2: play history. Plays keep their own copy of the track details, so old sessions stay
        // readable after files are renamed, moved or removed from the library.
        """
            CREATE TABLE sessions (
                id          INTEGER PRIMARY KEY,
                started_utc INTEGER NOT NULL
            );

            CREATE TABLE plays (
                id          INTEGER PRIMARY KEY,
                session_id  INTEGER NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
                played_utc  INTEGER NOT NULL,
                path        TEXT NOT NULL,
                title       TEXT,
                artist      TEXT,
                album       TEXT,
                bpm         REAL,
                duration_ms INTEGER NOT NULL DEFAULT 0,
                deck        TEXT,
                tempo       REAL NOT NULL DEFAULT 0
            );
            CREATE INDEX ix_plays_session ON plays(session_id, played_utc);
            CREATE INDEX ix_plays_path ON plays(path);
            """,

        // 3: track analysis (waveform, audible start and end, detected BPM). Keyed by path, not
        // track id, so it also covers files played from outside the library. The file's size and
        // modified time say whether it still describes the file on disk.
        """
            CREATE TABLE analysis (
                path         TEXT PRIMARY KEY,
                file_size    INTEGER NOT NULL,
                modified_utc INTEGER NOT NULL,
                version      INTEGER NOT NULL,
                length_ms    INTEGER NOT NULL,
                start_ms     INTEGER NOT NULL,
                end_ms       INTEGER NOT NULL,
                bpm          REAL,
                waveform     BLOB
            );
            """,

        // 4: BPM ranges the DJ chose for tracks whose tempo was detected wrongly. Separate from
        // the analysis, which is only a cache: this survives re-analysis and file changes.
        """
            CREATE TABLE bpm_ranges (
                path TEXT PRIMARY KEY,
                min  INTEGER NOT NULL,
                max  INTEGER NOT NULL
            );
            """,

        // 5: The DJ's rating and energy marks per file (see Rating, Energy). User data, like
        // bpm_ranges: kept by path, never written into the music files.
        """
            CREATE TABLE marks (
                path   TEXT PRIMARY KEY,
                rating INTEGER NOT NULL DEFAULT 0,
                energy INTEGER NOT NULL DEFAULT 0
            );
            """,

        // 6: Where the DJ set a song to start or end by hand (e.g. after the applause on a live
        // recording), overriding the automatic silence trim. Null: automatic.
        """
            CREATE TABLE trims (
                path     TEXT PRIMARY KEY,
                start_ms INTEGER,
                end_ms   INTEGER
            );
            """,

        // 7: A deck's EQ for each file, in dB (see TrackEq), e.g. the treble cut that tames one
        // record transfer's hiss. Belongs to the recording, so it's kept per file. Flat: no row.
        """
            CREATE TABLE eq (
                path    TEXT PRIMARY KEY,
                low_db  REAL NOT NULL,
                mid_db  REAL NOT NULL,
                high_db REAL NOT NULL
            );
            """,

        // 8: Loudness matching. The analysis measures each file's loudness and peak (a cache, like
        // the rest of it); the DJ's own gain for a song sits with its EQ (user data).
        """
            ALTER TABLE analysis ADD COLUMN lufs REAL;
            ALTER TABLE analysis ADD COLUMN peak_db REAL;
            ALTER TABLE eq ADD COLUMN gain_db REAL NOT NULL DEFAULT 0;
            """,

        // 9: A name for a session, e.g. "Hot Jam, Oct 14". Null: known by its date.
        """
            ALTER TABLE sessions ADD COLUMN name TEXT;
            """,

        // 10: Labels (Mixxx's crates): "Lindy", "Female vocals". Many per song, kept by path like
        // the other marks; never written into the files.
        """
            CREATE TABLE labels (
                id   INTEGER PRIMARY KEY,
                name TEXT NOT NULL UNIQUE COLLATE NOCASE
            );
            CREATE TABLE track_labels (
                path     TEXT NOT NULL,
                label_id INTEGER NOT NULL REFERENCES labels(id) ON DELETE CASCADE,
                PRIMARY KEY (path, label_id)
            );
            CREATE INDEX ix_track_labels_label ON track_labels(label_id);
            """,

        // 11: Jumps the DJ set in a song (see SongJump), up to two, by path.
        """
            CREATE TABLE jumps (
                path    TEXT NOT NULL,
                slot    INTEGER NOT NULL,
                from_ms INTEGER NOT NULL,
                to_ms   INTEGER NOT NULL,
                PRIMARY KEY (path, slot)
            );
            """,

        // 12: A BPM the DJ typed (or locked in Mixxx and imported), by path: it wins over the tag and detection.
        """
            CREATE TABLE manual_bpm (
                path TEXT PRIMARY KEY,
                bpm  REAL NOT NULL
            );
            """,
    ];

    // ───────────── Roots ─────────────

    public IReadOnlyList<LibraryRoot> GetRoots()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, path FROM roots ORDER BY path COLLATE NOCASE;";
        using var r = cmd.ExecuteReader();
        var roots = new List<LibraryRoot>();
        while (r.Read())
            roots.Add(new LibraryRoot(r.GetInt64(0), r.GetString(1)));
        return roots;
    }

    /// <summary>Adds a root folder, or returns the existing one with the same path.</summary>
    public LibraryRoot AddRoot(string path)
    {
        string fullPath = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO roots(path) VALUES ($path) ON CONFLICT(path) DO NOTHING;
            SELECT id FROM roots WHERE path = $path;
            """;
        cmd.Parameters.AddWithValue("$path", fullPath);
        return new LibraryRoot((long)cmd.ExecuteScalar()!, fullPath);
    }

    /// <summary>Removes a root and forgets its tracks. Files on disk are not touched.</summary>
    public void RemoveRoot(long rootId)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM roots WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", rootId);
        cmd.ExecuteNonQuery();
    }

    // ───────────── Scanning support ─────────────

    internal readonly record struct FileStamp(long Id, long Size, long ModifiedTicks);

    internal Dictionary<string, FileStamp> GetFileStamps(long rootId)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT path, id, file_size, modified_utc FROM tracks WHERE root_id = $root;";
        cmd.Parameters.AddWithValue("$root", rootId);
        using var r = cmd.ExecuteReader();
        var stamps = new Dictionary<string, FileStamp>(StringComparer.Ordinal);
        while (r.Read())
            stamps[r.GetString(0)] = new FileStamp(r.GetInt64(1), r.GetInt64(2), r.GetInt64(3));
        return stamps;
    }

    /// <summary>Inserts or updates tracks (matched by path) in one transaction.</summary>
    internal void UpsertTracks(IEnumerable<Track> tracks)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO tracks (root_id, path, folder, file_name, file_size, modified_utc,
                                title, artist, album, album_artist, genre, comment, year, track_number, bpm, duration_ms)
            VALUES ($root, $path, $folder, $file, $size, $mod,
                    $title, $artist, $album, $album_artist, $genre, $comment, $year, $track, $bpm, $duration)
            ON CONFLICT(path) DO UPDATE SET
                root_id = excluded.root_id, folder = excluded.folder, file_name = excluded.file_name,
                file_size = excluded.file_size, modified_utc = excluded.modified_utc,
                title = excluded.title, artist = excluded.artist, album = excluded.album,
                album_artist = excluded.album_artist, genre = excluded.genre, comment = excluded.comment,
                year = excluded.year, track_number = excluded.track_number, bpm = excluded.bpm,
                duration_ms = excluded.duration_ms;
            """;
        var p = new Dictionary<string, SqliteParameter>();
        foreach (string name in new[] { "$root", "$path", "$folder", "$file", "$size", "$mod", "$title", "$artist",
                     "$album", "$album_artist", "$genre", "$comment", "$year", "$track", "$bpm", "$duration" })
            p[name] = cmd.Parameters.Add(new SqliteParameter { ParameterName = name });
        cmd.Prepare();

        foreach (Track t in tracks)
        {
            p["$root"].Value = t.RootId;
            p["$path"].Value = t.Path;
            p["$folder"].Value = t.Folder;
            p["$file"].Value = t.FileName;
            p["$size"].Value = t.FileSize;
            p["$mod"].Value = t.ModifiedUtc.Ticks;
            p["$title"].Value = Db(t.Title);
            p["$artist"].Value = Db(t.Artist);
            p["$album"].Value = Db(t.Album);
            p["$album_artist"].Value = Db(t.AlbumArtist);
            p["$genre"].Value = Db(t.Genre);
            p["$comment"].Value = Db(t.Comment);
            p["$year"].Value = (object?)t.Year ?? DBNull.Value;
            p["$track"].Value = (object?)t.TrackNumber ?? DBNull.Value;
            p["$bpm"].Value = (object?)t.TagBpm ?? DBNull.Value;
            p["$duration"].Value = (long)t.Duration.TotalMilliseconds;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    internal void DeleteTracks(IEnumerable<long> ids)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM tracks WHERE id = $id;";
        var id = cmd.Parameters.Add(new SqliteParameter { ParameterName = "$id" });
        foreach (long value in ids)
        {
            id.Value = value;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    // ───────────── Queries ─────────────

    public int GetTrackCount()
    {
        using var c = Open();
        return Convert.ToInt32(Scalar(c, "SELECT COUNT(*) FROM tracks;"));
    }

    /// <summary>Number of tracks directly in each folder (not recursive), per root.</summary>
    public IReadOnlyList<(long RootId, string Folder, int Count)> GetFolderCounts()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT root_id, folder, COUNT(*) FROM tracks GROUP BY root_id, folder;";
        using var r = cmd.ExecuteReader();
        var counts = new List<(long, string, int)>();
        while (r.Read())
            counts.Add((r.GetInt64(0), r.GetString(1), r.GetInt32(2)));
        return counts;
    }

    /// <summary>
    /// Finds tracks matching every word of <paramref name="text"/> (as word prefixes) in any tag or
    /// folder name, and any filters in it (<c>bpm:130-150</c>, <c>artist:basie</c>, <c>-rating:no</c>;
    /// see <see cref="SearchQuery"/>), within <paramref name="scope"/> and everything beneath it.
    /// Empty text lists everything in scope. Results are in folder order, as they appear on disk.
    /// </summary>
    public IReadOnlyList<Track> Search(string? text, FolderScope? scope = null, CancellationToken cancellationToken = default)
    {
        var query = SearchQuery.Parse(text);

        var sql = new StringBuilder($"SELECT {TrackColumns} FROM tracks t {TrackJoins} WHERE 1 = 1");
        foreach (string condition in query.Conditions)
            sql.Append(" AND ").Append(condition);
        if (scope is not null)
        {
            sql.Append(" AND t.root_id = $root");
            if (scope.Folder.Length > 0)
                sql.Append(" AND (t.folder = $folder OR substr(t.folder, 1, $prefixLength) = $prefix)");
        }
        sql.Append(" ORDER BY t.root_id, t.folder COLLATE NOCASE, t.track_number, t.file_name COLLATE NOCASE;");

        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql.ToString();
        foreach (var (name, value) in query.Parameters)
            cmd.Parameters.AddWithValue(name, value);
        if (scope is not null)
        {
            cmd.Parameters.AddWithValue("$root", scope.RootId);
            cmd.Parameters.AddWithValue("$folder", scope.Folder);
            cmd.Parameters.AddWithValue("$prefix", scope.Folder + "/");
            cmd.Parameters.AddWithValue("$prefixLength", scope.Folder.Length + 1);
        }

        using var r = cmd.ExecuteReader();
        var results = new List<Track>();
        while (r.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(ReadTrack(r));
        }
        return results;
    }

    /// <summary>
    /// Turns user input into an FTS5 expression: each word becomes a quoted prefix term, all ANDed.
    /// Words are split exactly where the index tokenizer splits them (any non-alphanumeric), so
    /// "o'clock" searches for "o" and "clock" just as the tag was indexed. Only letters and digits
    /// survive, so no input can inject FTS syntax.
    /// </summary>
    internal static string? BuildMatchExpression(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var terms = new List<string>();
        var word = new StringBuilder();
        foreach (char ch in text + " ")
        {
            if (char.IsLetterOrDigit(ch))
            {
                word.Append(ch);
            }
            else if (word.Length > 0)
            {
                terms.Add($"\"{word}\"*");
                word.Clear();
            }
        }
        return terms.Count == 0 ? null : string.Join(' ', terms);
    }

    private static Track ReadTrack(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("id")),
        RootId = r.GetInt64(r.GetOrdinal("root_id")),
        Path = r.GetString(r.GetOrdinal("path")),
        Folder = r.GetString(r.GetOrdinal("folder")),
        FileName = r.GetString(r.GetOrdinal("file_name")),
        FileSize = r.GetInt64(r.GetOrdinal("file_size")),
        ModifiedUtc = new DateTime(r.GetInt64(r.GetOrdinal("modified_utc")), DateTimeKind.Utc),
        Title = GetString(r, "title"),
        Artist = GetString(r, "artist"),
        Album = GetString(r, "album"),
        AlbumArtist = GetString(r, "album_artist"),
        Genre = GetString(r, "genre"),
        Comment = GetString(r, "comment"),
        Year = GetInt(r, "year"),
        TrackNumber = GetInt(r, "track_number"),
        TagBpm = r.IsDBNull(r.GetOrdinal("bpm")) ? null : r.GetDouble(r.GetOrdinal("bpm")),
        DetectedBpm = r.IsDBNull(r.GetOrdinal("detected_bpm")) ? null : r.GetDouble(r.GetOrdinal("detected_bpm")),
        BpmRange = r.IsDBNull(r.GetOrdinal("range_min")) ? null : new BpmRange(r.GetInt32(r.GetOrdinal("range_min")), r.GetInt32(r.GetOrdinal("range_max"))),
        ManualBpm = r.IsDBNull(r.GetOrdinal("manual_bpm")) ? null : r.GetDouble(r.GetOrdinal("manual_bpm")),
        Duration = TimeSpan.FromMilliseconds(r.GetInt64(r.GetOrdinal("duration_ms"))),
        Rating = (Rating)(GetInt(r, "rating") ?? 0),
        Energy = (Energy)(GetInt(r, "energy") ?? 0),
        LastPlayedUtc = r.IsDBNull(r.GetOrdinal("last_played")) ? null : new DateTime(r.GetInt64(r.GetOrdinal("last_played")), DateTimeKind.Utc),
        PlayCount = GetInt(r, "play_count") ?? 0,
        Labels = ReadLabels(GetString(r, "labels")),
    };

    /// <summary>A track row plus what the app knows about it: detected BPM, any chosen BPM range, and the DJ's marks.</summary>
    private const string TrackColumns =
        "t.*, a.bpm AS detected_bpm, r.min AS range_min, r.max AS range_max, mb.bpm AS manual_bpm, m.rating AS rating, m.energy AS energy, " +
        "lp.last_played AS last_played, lp.play_count AS play_count, lb.labels AS labels";
    private const string TrackJoins =
        JoinAnalysis + " LEFT JOIN bpm_ranges r ON r.path = t.path LEFT JOIN manual_bpm mb ON mb.path = t.path LEFT JOIN marks m ON m.path = t.path " +
        "LEFT JOIN (SELECT path, MAX(played_utc) AS last_played, COUNT(*) AS play_count FROM plays GROUP BY path) lp ON lp.path = t.path " +
        "LEFT JOIN (SELECT tl.path, group_concat(l.name, char(31)) AS labels FROM track_labels tl JOIN labels l ON l.id = tl.label_id " +
        "GROUP BY tl.path) lb ON lb.path = t.path";

    /// <summary>Joins a track's analysis, if there is one for the file as it is now.</summary>
    private const string JoinAnalysis =
        "LEFT JOIN analysis a ON a.path = t.path AND a.file_size = t.file_size AND a.modified_utc = t.modified_utc";

    private static string? GetString(SqliteDataReader r, string column)
    {
        int i = r.GetOrdinal(column);
        return r.IsDBNull(i) ? null : r.GetString(i);
    }

    private static int? GetInt(SqliteDataReader r, string column)
    {
        int i = r.GetOrdinal(column);
        return r.IsDBNull(i) ? null : r.GetInt32(i);
    }

    private static object Db(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static void Execute(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }
}
