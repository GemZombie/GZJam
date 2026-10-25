using Microsoft.Data.Sqlite;

namespace GZJam.Library;

/// <summary>A DJ session: one gig's worth of plays.</summary>
public sealed record PlaySession(long Id, DateTime StartedUtc, DateTime? LastPlayUtc, int PlayCount, TimeSpan TotalDuration)
{
    /// <summary>What the DJ called it ("Friday Night, Oct 14"), or null.</summary>
    public string? Name { get; init; }
}

/// <summary>A track played to the room, with its details as they were at the time.</summary>
public sealed record PlayRecord(
    long Id, long SessionId, DateTime PlayedUtc, string Path, string? Title, string? Artist, string? Album,
    double? Bpm, TimeSpan Duration, string? Deck, double TempoPercent)
{
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? System.IO.Path.GetFileNameWithoutExtension(Path) : Title;

    /// <summary>A library-style track for dragging back onto a deck or into the queue.</summary>
    public Track ToTrack() => new()
    {
        Path = Path,
        Folder = "",
        FileName = System.IO.Path.GetFileName(Path),
        Title = Title,
        Artist = Artist,
        Album = Album,
        TagBpm = Bpm,
        Duration = Duration,
    };
}

public sealed partial class LibraryDatabase
{
    /// <summary>
    /// The session to record into: the latest one if its last play (or start) was within
    /// <paramref name="continueWithin"/>, so restarting mid-gig doesn't split the night; otherwise a new one.
    /// </summary>
    public PlaySession StartOrContinueSession(DateTime nowUtc, TimeSpan continueWithin)
    {
        if (GetSessions().FirstOrDefault() is { } latest &&
            nowUtc - (latest.LastPlayUtc ?? latest.StartedUtc) <= continueWithin)
            return latest;

        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO sessions(started_utc) VALUES ($started); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$started", nowUtc.Ticks);
        long id = (long)cmd.ExecuteScalar()!;
        return new PlaySession(id, nowUtc, null, 0, TimeSpan.Zero);
    }

    /// <param name="duration">The track's real length if known (the deck measures it); defaults to the tagged length.</param>
    public PlayRecord AddPlay(long sessionId, Track track, DateTime playedUtc, string? deck, double tempoPercent, TimeSpan? duration = null)
    {
        TimeSpan length = duration ?? track.Duration;
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO plays (session_id, played_utc, path, title, artist, album, bpm, duration_ms, deck, tempo)
            VALUES ($session, $played, $path, $title, $artist, $album, $bpm, $duration, $deck, $tempo);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$session", sessionId);
        cmd.Parameters.AddWithValue("$played", playedUtc.Ticks);
        cmd.Parameters.AddWithValue("$path", track.Path);
        cmd.Parameters.AddWithValue("$title", Db(track.Title));
        cmd.Parameters.AddWithValue("$artist", Db(track.Artist));
        cmd.Parameters.AddWithValue("$album", Db(track.Album));
        cmd.Parameters.AddWithValue("$bpm", (object?)track.Bpm ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$duration", (long)length.TotalMilliseconds);
        cmd.Parameters.AddWithValue("$deck", (object?)deck ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tempo", tempoPercent);
        long id = (long)cmd.ExecuteScalar()!;
        return new PlayRecord(id, sessionId, playedUtc, track.Path, track.Title, track.Artist, track.Album,
            track.Bpm, length, deck, tempoPercent);
    }

    /// <summary>All sessions that have plays (plus the newest, even if empty), newest first.</summary>
    public IReadOnlyList<PlaySession> GetSessions()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT s.id, s.started_utc, MAX(p.played_utc), COUNT(p.id), COALESCE(SUM(p.duration_ms), 0), s.name
            FROM sessions s LEFT JOIN plays p ON p.session_id = s.id
            GROUP BY s.id
            HAVING COUNT(p.id) > 0 OR s.id = (SELECT MAX(id) FROM sessions)
            ORDER BY s.started_utc DESC, s.id DESC;
            """;
        using var r = cmd.ExecuteReader();
        var sessions = new List<PlaySession>();
        while (r.Read())
        {
            sessions.Add(new PlaySession(
                r.GetInt64(0),
                new DateTime(r.GetInt64(1), DateTimeKind.Utc),
                r.IsDBNull(2) ? null : new DateTime(r.GetInt64(2), DateTimeKind.Utc),
                r.GetInt32(3),
                TimeSpan.FromMilliseconds(r.GetInt64(4)))
            {
                Name = r.IsDBNull(5) ? null : r.GetString(5),
            });
        }
        return sessions;
    }

    public IReadOnlyList<PlayRecord> GetPlays(long sessionId)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT id, session_id, played_utc, path, title, artist, album, bpm, duration_ms, deck, tempo
            FROM plays WHERE session_id = $session ORDER BY played_utc, id;
            """;
        cmd.Parameters.AddWithValue("$session", sessionId);
        using var r = cmd.ExecuteReader();
        var plays = new List<PlayRecord>();
        while (r.Read())
        {
            plays.Add(new PlayRecord(
                r.GetInt64(0),
                r.GetInt64(1),
                new DateTime(r.GetInt64(2), DateTimeKind.Utc),
                r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(7) ? null : r.GetDouble(7),
                TimeSpan.FromMilliseconds(r.GetInt64(8)),
                r.IsDBNull(9) ? null : r.GetString(9),
                r.GetDouble(10)));
        }
        return plays;
    }

    /// <summary>The library's track at <paramref name="path"/>, if it's still in the library.</summary>
    public Track? FindTrack(string path)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT {TrackColumns} FROM tracks t {TrackJoins} WHERE t.path = $path;";
        cmd.Parameters.AddWithValue("$path", path);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadTrack(r) : null;
    }

    /// <summary>
    /// The song in a file: the library's, with its marks, if it's in the library; otherwise read
    /// from its tags (a file from outside the music folders), without adding it to the library.
    /// </summary>
    public Track TrackForFile(string path) =>
        FindTrack(path) ?? TagReader.Read(0, System.IO.Path.GetDirectoryName(path)!, new FileInfo(path));

    /// <summary>Names a session; an empty name goes back to its date.</summary>
    public void RenameSession(long sessionId, string? name)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE sessions SET name = $name WHERE id = $id;";
        cmd.Parameters.AddWithValue("$name", string.IsNullOrWhiteSpace(name) ? DBNull.Value : name.Trim());
        cmd.Parameters.AddWithValue("$id", sessionId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Whether History has <paramref name="path"/> played within a couple of seconds of <paramref name="playedUtc"/>.</summary>
    public bool HasPlay(string path, DateTime playedUtc)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM plays WHERE path = $path AND played_utc BETWEEN $from AND $to);";
        cmd.Parameters.AddWithValue("$path", path);
        cmd.Parameters.AddWithValue("$from", (playedUtc - TimeSpan.FromSeconds(2)).Ticks);
        cmd.Parameters.AddWithValue("$to", (playedUtc + TimeSpan.FromSeconds(2)).Ticks);
        return Convert.ToInt64(cmd.ExecuteScalar()) == 1;
    }

    /// <summary>Adds a past night to History (from another DJ app): a session starting with its first song.</summary>
    public PlaySession ImportSession(string? name, IReadOnlyList<(Track Track, DateTime PlayedUtc)> plays)
    {
        if (plays.Count == 0)
            throw new ArgumentException("A session needs songs.", nameof(plays));
        using var c = Open();
        using var transaction = c.BeginTransaction();
        long id;
        using (var session = c.CreateCommand())
        {
            session.CommandText = "INSERT INTO sessions (started_utc, name) VALUES ($started, $name); SELECT last_insert_rowid();";
            session.Parameters.AddWithValue("$started", plays[0].PlayedUtc.Ticks);
            session.Parameters.AddWithValue("$name", string.IsNullOrWhiteSpace(name) ? DBNull.Value : name.Trim());
            id = (long)session.ExecuteScalar()!;
        }
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO plays (session_id, played_utc, path, title, artist, album, bpm, duration_ms, deck, tempo)
            VALUES ($session, $played, $path, $title, $artist, $album, $bpm, $duration, NULL, 0);
            """;
        cmd.Parameters.AddWithValue("$session", id);
        var played = cmd.Parameters.Add("$played", SqliteType.Integer);
        var path = cmd.Parameters.Add("$path", SqliteType.Text);
        var title = cmd.Parameters.Add("$title", SqliteType.Text);
        var artist = cmd.Parameters.Add("$artist", SqliteType.Text);
        var album = cmd.Parameters.Add("$album", SqliteType.Text);
        var bpm = cmd.Parameters.Add("$bpm", SqliteType.Real);
        var duration = cmd.Parameters.Add("$duration", SqliteType.Integer);
        foreach (var (track, playedUtc) in plays)
        {
            played.Value = playedUtc.Ticks;
            path.Value = track.Path;
            title.Value = Db(track.Title);
            artist.Value = Db(track.Artist);
            album.Value = Db(track.Album);
            bpm.Value = (object?)track.Bpm ?? DBNull.Value;
            duration.Value = (long)track.Duration.TotalMilliseconds;
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
        return new PlaySession(id, plays[0].PlayedUtc, plays[^1].PlayedUtc, plays.Count,
            TimeSpan.FromTicks(plays.Sum(p => p.Track.Duration.Ticks))) { Name = name };
    }

    public void DeleteSession(long sessionId)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM sessions WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", sessionId);
        cmd.ExecuteNonQuery();
    }
}
