using Microsoft.Data.Sqlite;

namespace GZJam.Library;

/// <summary>A DJ session: one gig's worth of plays.</summary>
public sealed record PlaySession(long Id, DateTime StartedUtc, DateTime? LastPlayUtc, int PlayCount, TimeSpan TotalDuration);

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
            SELECT s.id, s.started_utc, MAX(p.played_utc), COUNT(p.id), COALESCE(SUM(p.duration_ms), 0)
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
                TimeSpan.FromMilliseconds(r.GetInt64(4))));
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
        cmd.CommandText = $"SELECT t.*, a.bpm AS detected_bpm FROM tracks t {JoinAnalysis} WHERE t.path = $path;";
        cmd.Parameters.AddWithValue("$path", path);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadTrack(r) : null;
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
