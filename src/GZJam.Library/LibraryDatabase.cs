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
        // For very large libraries: a bigger page cache, sorts in memory rather than a temp file, and
        // the file read through memory mapping. Limits, not allocations: a small library uses little.
        Execute(connection, "PRAGMA cache_size = -32768; PRAGMA temp_store = MEMORY; PRAGMA mmap_size = 268435456;");
        return connection;
    }

    /// <summary>
    /// The oldest GZJam (by how many upgrades it knows) that can read a library this GZJam has
    /// upgraded. Most upgrades only add (a table, a column with a default), and an older GZJam
    /// reads such a library fine; raise this to <see cref="Migrations"/>' length only for an
    /// upgrade older ones can't live with (a column renamed, data moved). 19: the first that
    /// looks at <c>schema_compat</c> at all; GZJam 0.6 and before refuse any newer library.
    /// </summary>
    private const int OldestReader = 19;

    private void CreateSchema()
    {
        using var c = Open();
        Execute(c, "PRAGMA journal_mode = WAL;");

        long version = (long)(Scalar(c, "PRAGMA user_version;") ?? 0L);
        if (version == Migrations.Length)
            return;
        if (version > Migrations.Length)
        {
            // A newer GZJam upgraded it. If it says this one can still read it, open it as it is.
            if (OldestReaderOf(c) is { } oldest && oldest <= Migrations.Length)
                return;
            throw new LibraryTooNewException(version, Migrations.Length);
        }

        // Apply each upgrade the database hasn't had yet, all or nothing.
        using var tx = c.BeginTransaction();
        for (long step = version; step < Migrations.Length; step++)
            Execute(c, Migrations[step]);
        Execute(c, $"PRAGMA user_version = {Migrations.Length};");
        Execute(c, $"UPDATE schema_compat SET oldest_reader = max(oldest_reader, {OldestReader});");
        tx.Commit();
    }

    /// <summary>The oldest GZJam that can read this library, as the newest one to upgrade it said; null if none said.</summary>
    private static long? OldestReaderOf(SqliteConnection c)
    {
        if (Scalar(c, "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'schema_compat';") is null)
            return null;
        return Scalar(c, "SELECT oldest_reader FROM schema_compat;") as long?;
    }

    /// <summary>
    /// Whether this GZJam can open the library at <paramref name="databasePath"/>, looked at
    /// without changing anything: null if it can (or there's none yet), else why not.
    /// </summary>
    public static LibraryTooNewException? CheckCanOpen(string databasePath)
    {
        if (!File.Exists(databasePath))
            return null;
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        c.Open();
        long version = (long)(Scalar(c, "PRAGMA user_version;") ?? 0L);
        if (version <= Migrations.Length || OldestReaderOf(c) is { } oldest && oldest <= Migrations.Length)
            return null;
        return new LibraryTooNewException(version, Migrations.Length);
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

        // 9: A name for a session, e.g. "Friday Night, Oct 14". Null: known by its date.
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

        // 13: Tags that store BPM x10 ("1180" for 118.0) or x100 are read correctly since
        // 2026-10-21 (TagReader.NormalizeBpm), but songs scanned before then kept the raw value,
        // and a rescan skips files that haven't changed. The same rule, once, for the stored tag
        // BPMs and History (issue #26).
        """
            UPDATE tracks SET bpm = bpm / 10 WHERE bpm > 400 AND bpm / 10 BETWEEN 40 AND 400;
            UPDATE tracks SET bpm = bpm / 100 WHERE bpm > 400 AND bpm / 100 BETWEEN 30 AND 400;
            UPDATE plays SET bpm = bpm / 10 WHERE bpm > 400 AND bpm / 10 BETWEEN 40 AND 400;
            UPDATE plays SET bpm = bpm / 100 WHERE bpm > 400 AND bpm / 100 BETWEEN 30 AND 400;
            """,

        // 14: Which way of reading tags each song was read with (TagReader.Version). When it's
        // raised, the next scan reads every song's tags again, not only changed files. 0: before.
        """
            ALTER TABLE tracks ADD COLUMN tag_version INTEGER NOT NULL DEFAULT 0;
            """,

        // 15: Cues the DJ marked in a song (see SongCue), numbered 1 to 4, by path.
        """
            CREATE TABLE cues (
                path   TEXT NOT NULL,
                number INTEGER NOT NULL,
                at_ms  INTEGER NOT NULL,
                PRIMARY KEY (path, number)
            );
            """,

        // 16: Which picture each song shows (see AlbumArtFinder), by path: art_key names the small
        // copy in the art folder, null for none. A cache, like the analysis: the file's size and
        // modified time, and its folder's modified time (a cover.jpg added later), say whether it's
        // still right.
        """
            CREATE TABLE art (
                path            TEXT PRIMARY KEY,
                file_size       INTEGER NOT NULL,
                modified_utc    INTEGER NOT NULL,
                folder_modified INTEGER NOT NULL,
                version         INTEGER NOT NULL,
                art_key         TEXT
            );
            """,

        // 17: Where a song's mid EQ band sits (Hz), and how much it's evened out (0 to 100, a
        // compressor): both set with its EQ, in the EQ panel.
        """
            ALTER TABLE eq ADD COLUMN mid_hz REAL NOT NULL DEFAULT 1000;
            ALTER TABLE eq ADD COLUMN even REAL NOT NULL DEFAULT 0;
            """,

        // 18: Playlists: a set planned in order, saved by name. Entries are by path, like the
        // DJ's other data, numbered from 0 in order; a song can be in one more than once.
        """
            CREATE TABLE playlists (
                id   INTEGER PRIMARY KEY,
                name TEXT NOT NULL UNIQUE COLLATE NOCASE
            );
            CREATE TABLE playlist_entries (
                id          INTEGER PRIMARY KEY,
                playlist_id INTEGER NOT NULL REFERENCES playlists(id) ON DELETE CASCADE,
                position    INTEGER NOT NULL,
                path        TEXT NOT NULL
            );
            CREATE INDEX ix_playlist_entries ON playlist_entries(playlist_id, position);
            """,

        // 19: Which GZJam can still read this library (see OldestReader): a newer GZJam whose
        // upgrades only added things records that, and this one opens its library rather than
        // refusing it.
        """
            CREATE TABLE schema_compat (oldest_reader INTEGER NOT NULL);
            INSERT INTO schema_compat (oldest_reader) VALUES (19);
            """,

        // 20: The song list's order as an index, so listing a very large library walks the index
        // rather than sorting every song first. Only adds: GZJam 19 reads it fine.
        """
            CREATE INDEX ix_tracks_order ON tracks(root_id, folder COLLATE NOCASE, track_number, file_name COLLATE NOCASE);
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

    internal readonly record struct FileStamp(long Id, long Size, long ModifiedTicks, int TagVersion);

    internal Dictionary<string, FileStamp> GetFileStamps(long rootId)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT path, id, file_size, modified_utc, tag_version FROM tracks WHERE root_id = $root;";
        cmd.Parameters.AddWithValue("$root", rootId);
        using var r = cmd.ExecuteReader();
        var stamps = new Dictionary<string, FileStamp>(StringComparer.Ordinal);
        while (r.Read())
            stamps[r.GetString(0)] = new FileStamp(r.GetInt64(1), r.GetInt64(2), r.GetInt64(3), r.GetInt32(4));
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
                                title, artist, album, album_artist, genre, comment, year, track_number, bpm, duration_ms, tag_version)
            VALUES ($root, $path, $folder, $file, $size, $mod,
                    $title, $artist, $album, $album_artist, $genre, $comment, $year, $track, $bpm, $duration, $tag_version)
            ON CONFLICT(path) DO UPDATE SET
                root_id = excluded.root_id, folder = excluded.folder, file_name = excluded.file_name,
                file_size = excluded.file_size, modified_utc = excluded.modified_utc,
                title = excluded.title, artist = excluded.artist, album = excluded.album,
                album_artist = excluded.album_artist, genre = excluded.genre, comment = excluded.comment,
                year = excluded.year, track_number = excluded.track_number, bpm = excluded.bpm,
                duration_ms = excluded.duration_ms, tag_version = excluded.tag_version;
            """;
        var p = new Dictionary<string, SqliteParameter>();
        foreach (string name in new[] { "$root", "$path", "$folder", "$file", "$size", "$mod", "$title", "$artist",
                     "$album", "$album_artist", "$genre", "$comment", "$year", "$track", "$bpm", "$duration", "$tag_version" })
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
            p["$tag_version"].Value = TagReader.Version;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>
    /// Takes songs whose files are gone out of the index, and moves what the DJ gave the renamed or
    /// moved ones to their new paths (see <see cref="MoveSongData"/>), in one transaction.
    /// </summary>
    internal void RemoveTracks(IEnumerable<long> ids, IReadOnlyList<(string From, string To)> moves)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM tracks WHERE id = $id;";
            var id = cmd.Parameters.Add(new SqliteParameter { ParameterName = "$id" });
            foreach (long value in ids)
            {
                id.Value = value;
                cmd.ExecuteNonQuery();
            }
        }
        foreach (var (from, to) in moves)
            MoveSongData(c, from, to);
        tx.Commit();
    }

    /// <summary>
    /// Everything kept by a song's path, other than the index itself and the album art (which
    /// depends on the folder, and is found again). Add a table here when it's keyed by path.
    /// </summary>
    internal static readonly string[] SongDataTables =
        ["marks", "track_labels", "eq", "trims", "cues", "jumps", "manual_bpm", "bpm_ranges", "analysis"];

    /// <summary>Of those, the ones where a song can have any number of rows: never cleared at the new path.</summary>
    private static readonly string[] SongListTables = ["playlist_entries", "plays"];

    /// <summary>
    /// A song renamed or moved: its marks, labels, EQ, trims, cues, jumps, BPMs and analysis go
    /// with it, replacing anything left at the new path by a file that was there before; and its
    /// playlist entries and plays (so the Played column) point to the new path.
    /// </summary>
    private static void MoveSongData(SqliteConnection c, string from, string to)
    {
        using var cmd = c.CreateCommand();
        cmd.Parameters.AddWithValue("$from", from);
        cmd.Parameters.AddWithValue("$to", to);
        foreach (string table in SongDataTables)
        {
            cmd.CommandText = $"DELETE FROM {table} WHERE path = $to; UPDATE {table} SET path = $to WHERE path = $from;";
            cmd.ExecuteNonQuery();
        }
        foreach (string table in SongListTables)
        {
            cmd.CommandText = $"UPDATE {table} SET path = $to WHERE path = $from;";
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Every table a move updates.</summary>
    internal static IEnumerable<string> MovedTables => SongDataTables.Concat(SongListTables);

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

    /// <summary>How far <c>~bpm:</c> searches reach either side unless the DJ chooses: the tempo slider's range.</summary>
    public const double DefaultNearBpm = 0.08;

    /// <summary>
    /// Finds tracks matching every word of <paramref name="text"/> (as word prefixes) in any tag or
    /// folder name, and any filters in it (<c>bpm:130-150</c>, <c>artist:basie</c>, <c>-rating:no</c>;
    /// see <see cref="SearchQuery"/>), within <paramref name="scope"/> and everything beneath it.
    /// Empty text lists everything in scope. Results are in folder order, as they appear on disk.
    /// <paramref name="nearBpm"/> is how far <c>~bpm:</c> reaches either side (a fraction), unless
    /// the search says (<c>~bpm:140/5</c>).
    /// </summary>
    public IReadOnlyList<Track> Search(string? text, FolderScope? scope = null, CancellationToken cancellationToken = default,
        double nearBpm = DefaultNearBpm, long? playlist = null)
    {
        var query = SearchQuery.Parse(text, nearBpm: nearBpm);

        // Within a playlist: its songs in its order, one row per entry (a song can be in it twice).
        var sql = new StringBuilder(playlist is null
            ? $"SELECT {TrackColumns} FROM tracks t {TrackJoins} WHERE 1 = 1"
            : $"SELECT {TrackColumns}, e.id AS entry_id, e.position AS entry_position FROM playlist_entries e " +
              $"JOIN tracks t ON t.path = e.path {TrackJoins} WHERE e.playlist_id = $playlist");
        foreach (string condition in query.Conditions)
            sql.Append(" AND ").Append(condition);
        if (scope is not null)
        {
            sql.Append(" AND t.root_id = $root");
            if (scope.Folder.Length > 0)
                sql.Append(" AND (t.folder = $folder OR substr(t.folder, 1, $prefixLength) = $prefix)");
        }
        sql.Append(playlist is null
            ? " ORDER BY t.root_id, t.folder COLLATE NOCASE, t.track_number, t.file_name COLLATE NOCASE;"
            : " ORDER BY e.position;");

        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql.ToString();
        foreach (var (name, value) in query.Parameters)
            cmd.Parameters.AddWithValue(name, value);
        if (playlist is not null)
            cmd.Parameters.AddWithValue("$playlist", playlist);
        if (scope is not null)
        {
            cmd.Parameters.AddWithValue("$root", scope.RootId);
            cmd.Parameters.AddWithValue("$folder", scope.Folder);
            cmd.Parameters.AddWithValue("$prefix", scope.Folder + "/");
            cmd.Parameters.AddWithValue("$prefixLength", scope.Folder.Length + 1);
        }

        using var r = cmd.ExecuteReader();
        var results = new List<Track>();
        var ordinals = new TrackOrdinals(r);
        int entryId = playlist is null ? -1 : r.GetOrdinal("entry_id"), entryPosition = playlist is null ? -1 : r.GetOrdinal("entry_position");
        while (r.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            Track track = ReadTrack(r, ordinals);
            if (playlist is not null)
                track = track.InPlaylist(r.GetInt64(entryId), r.GetInt32(entryPosition) + 1);
            results.Add(track);
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

    private static Track ReadTrack(SqliteDataReader r) => ReadTrack(r, new TrackOrdinals(r));

    private static Track ReadTrack(SqliteDataReader r, TrackOrdinals o) => new()
    {
        Id = r.GetInt64(o.Id),
        RootId = r.GetInt64(o.RootId),
        Path = r.GetString(o.Path),
        Folder = r.GetString(o.Folder),
        FileName = r.GetString(o.FileName),
        FileSize = r.GetInt64(o.FileSize),
        ModifiedUtc = new DateTime(r.GetInt64(o.ModifiedUtc), DateTimeKind.Utc),
        Title = GetString(r, o.Title),
        Artist = GetString(r, o.Artist),
        Album = GetString(r, o.Album),
        AlbumArtist = GetString(r, o.AlbumArtist),
        Genre = GetString(r, o.Genre),
        Comment = GetString(r, o.Comment),
        Year = GetInt(r, o.Year),
        TrackNumber = GetInt(r, o.TrackNumber),
        TagBpm = GetDouble(r, o.TagBpm),
        DetectedBpm = GetDouble(r, o.DetectedBpm),
        BpmRange = r.IsDBNull(o.RangeMin) ? null : new BpmRange(r.GetInt32(o.RangeMin), r.GetInt32(o.RangeMax)),
        ManualBpm = GetDouble(r, o.ManualBpm),
        Duration = TimeSpan.FromMilliseconds(r.GetInt64(o.Duration)),
        Rating = (Rating)(GetInt(r, o.Rating) ?? 0),
        Energy = (Energy)(GetInt(r, o.Energy) ?? 0),
        LastPlayedUtc = r.IsDBNull(o.LastPlayed) ? null : new DateTime(r.GetInt64(o.LastPlayed), DateTimeKind.Utc),
        PlayCount = GetInt(r, o.PlayCount) ?? 0,
        Labels = ReadLabels(GetString(r, o.Labels)),
    };

    /// <summary>Where each of a track's columns is in a result, looked up once per query rather than once per row.</summary>
    private sealed class TrackOrdinals(SqliteDataReader r)
    {
        public readonly int Id = r.GetOrdinal("id"), RootId = r.GetOrdinal("root_id"), Path = r.GetOrdinal("path"),
            Folder = r.GetOrdinal("folder"), FileName = r.GetOrdinal("file_name"), FileSize = r.GetOrdinal("file_size"),
            ModifiedUtc = r.GetOrdinal("modified_utc"), Title = r.GetOrdinal("title"), Artist = r.GetOrdinal("artist"),
            Album = r.GetOrdinal("album"), AlbumArtist = r.GetOrdinal("album_artist"), Genre = r.GetOrdinal("genre"),
            Comment = r.GetOrdinal("comment"), Year = r.GetOrdinal("year"), TrackNumber = r.GetOrdinal("track_number"),
            TagBpm = r.GetOrdinal("bpm"), DetectedBpm = r.GetOrdinal("detected_bpm"), RangeMin = r.GetOrdinal("range_min"),
            RangeMax = r.GetOrdinal("range_max"), ManualBpm = r.GetOrdinal("manual_bpm"), Duration = r.GetOrdinal("duration_ms"),
            Rating = r.GetOrdinal("rating"), Energy = r.GetOrdinal("energy"), LastPlayed = r.GetOrdinal("last_played"),
            PlayCount = r.GetOrdinal("play_count"), Labels = r.GetOrdinal("labels");
    }

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

    private static string? GetString(SqliteDataReader r, string column) => GetString(r, r.GetOrdinal(column));

    private static string? GetString(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static int? GetInt(SqliteDataReader r, string column) => GetInt(r, r.GetOrdinal(column));

    private static int? GetInt(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt32(i);

    private static double? GetDouble(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDouble(i);

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
