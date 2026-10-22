using Microsoft.Data.Sqlite;

namespace GZJam.Testing;

/// <summary>A Mixxx library, laid out as Mixxx's res/schema.xml makes it (the columns GZJam reads).</summary>
public sealed class FakeMixxx(string path)
{
    private readonly SqliteConnection _c = Create(path);

    private static SqliteConnection Create(string path)
    {
        var c = new SqliteConnection($"Data Source={path};Pooling=False;Foreign Keys=False");   // Mixxx runs without them
        c.Open();
        Exec(c, """
            CREATE TABLE track_locations (id INTEGER PRIMARY KEY AUTOINCREMENT, location varchar(512) UNIQUE, filename varchar(512),
                directory varchar(512), filesize INTEGER, fs_deleted INTEGER, needs_verification INTEGER);
            CREATE TABLE library (id INTEGER PRIMARY KEY AUTOINCREMENT, artist varchar(64), title varchar(64), album varchar(64),
                year varchar(16), genre varchar(64), location INTEGER REFERENCES track_locations(location), duration FLOAT,
                samplerate INTEGER, bpm FLOAT, mixxx_deleted INTEGER, played INTEGER, timesplayed INTEGER DEFAULT 0,
                rating INTEGER DEFAULT 0, bpm_lock INTEGER DEFAULT 0, last_played_at DATETIME DEFAULT NULL);
            CREATE TABLE Playlists (id INTEGER PRIMARY KEY, name varchar(48), position INTEGER, hidden INTEGER DEFAULT 0 NOT NULL,
                date_created datetime, date_modified datetime, locked INTEGER DEFAULT 0);
            CREATE TABLE PlaylistTracks (id INTEGER PRIMARY KEY, playlist_id INTEGER REFERENCES Playlists(id),
                track_id INTEGER REFERENCES library(id), position INTEGER, pl_datetime_added);
            CREATE TABLE crates (id INTEGER PRIMARY KEY AUTOINCREMENT, name varchar(48) UNIQUE NOT NULL, count INTEGER DEFAULT 0,
                show INTEGER DEFAULT 1, locked INTEGER DEFAULT 0, autodj_source INTEGER DEFAULT 0);
            CREATE TABLE crate_tracks (crate_id INTEGER NOT NULL REFERENCES crates(id), track_id INTEGER NOT NULL REFERENCES library(id),
                UNIQUE (crate_id, track_id));
            """);
        return c;
    }

    private static void Exec(SqliteConnection c, string sql, params (string, object?)[] args)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private long Id(string sql, params (string, object?)[] args)
    {
        Exec(_c, sql, args);
        using var cmd = _c.CreateCommand();
        cmd.CommandText = "SELECT last_insert_rowid();";
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>A song, its path written the way Mixxx (Qt) does: forward slashes.</summary>
    public long Song(string path, string title, int stars = 0, double bpm = 0, bool locked = false, bool deleted = false, long? size = null)
    {
        string qt = path.Replace('\\', '/');
        long location = Id("INSERT INTO track_locations (location, filename, directory, filesize) VALUES ($l, $f, $d, $s);",
            ("$l", qt), ("$f", Path.GetFileName(qt)), ("$d", Path.GetDirectoryName(qt)), ("$s", size ?? new FileInfo(path).Length));
        return Id("INSERT INTO library (title, artist, location, duration, samplerate, bpm, rating, bpm_lock, mixxx_deleted) " +
                  "VALUES ($t, 'Mixxx Artist', $l, 180.5, 44100, $b, $r, $k, $x);",
            ("$t", title), ("$l", location), ("$b", bpm), ("$r", stars), ("$k", locked ? 1 : 0), ("$x", deleted ? 1 : 0));
    }

    public void Crate(string name, params long[] songs)
    {
        long id = Id("INSERT INTO crates (name) VALUES ($n);", ("$n", name));
        foreach (long song in songs)
            Exec(_c, "INSERT INTO crate_tracks (crate_id, track_id) VALUES ($c, $t);", ("$c", id), ("$t", song));
    }

    /// <summary>A History night: a set log playlist, timestamps as SQLite's CURRENT_TIMESTAMP writes them (UTC).</summary>
    public void Night(string name, params (long Song, DateTime Utc)[] plays)
    {
        long id = Id("INSERT INTO Playlists (name, position, hidden) VALUES ($n, 0, 2);", ("$n", name));
        int position = 1;
        foreach (var (song, utc) in plays)
            Exec(_c, "INSERT INTO PlaylistTracks (playlist_id, track_id, position, pl_datetime_added) VALUES ($p, $t, $o, $d);",
                ("$p", id), ("$t", song), ("$o", position++), ("$d", utc.ToString("yyyy-MM-dd HH:mm:ss")));
    }

    public void Playlist(string name, params long[] songs)
    {
        long id = Id("INSERT INTO Playlists (name, position, hidden) VALUES ($n, 0, 0);", ("$n", name));
        foreach (long song in songs)
            Exec(_c, "INSERT INTO PlaylistTracks (playlist_id, track_id, position, pl_datetime_added) VALUES ($p, $t, 1, '2024-01-01 10:00:00');",
                ("$p", id), ("$t", song));
    }

    public void Close() => _c.Dispose();
}
