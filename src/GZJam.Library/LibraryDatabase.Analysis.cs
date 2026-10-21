using Microsoft.Data.Sqlite;

namespace GZJam.Library;

/// <summary>A saved track analysis, as the analyzer of <paramref name="Version"/> left it.</summary>
/// <param name="Waveform">The overview waveform, in the audio layer's own format.</param>
public sealed record StoredAnalysis(int Version, TimeSpan Length, TimeSpan AudibleStart, TimeSpan AudibleEnd, double? Bpm, byte[]? Waveform);

/// <summary>Identifies one version of a file on disk: when it changes, its analysis is stale.</summary>
public readonly record struct FileVersion(long Size, long ModifiedTicks)
{
    public static FileVersion? Of(string path)
    {
        var file = new FileInfo(path);
        return file.Exists ? new FileVersion(file.Length, file.LastWriteTimeUtc.Ticks) : null;
    }
}

/// <summary>Where the DJ set a song to start and end by hand; null parts are automatic (silence trimming).</summary>
public readonly record struct ManualTrim(TimeSpan? Start, TimeSpan? End)
{
    public bool IsEmpty => Start is null && End is null;
}

/// <summary>A library track still waiting for analysis.</summary>
public sealed record PendingAnalysis(string Path, FileVersion Version, bool HasTagBpm);

public sealed partial class LibraryDatabase
{
    /// <summary>The saved analysis of a file, if it still matches <paramref name="version"/>.</summary>
    public StoredAnalysis? GetAnalysis(string path, FileVersion version)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT version, length_ms, start_ms, end_ms, bpm, waveform FROM analysis
            WHERE path = $path AND file_size = $size AND modified_utc = $mod;
            """;
        cmd.Parameters.AddWithValue("$path", path);
        cmd.Parameters.AddWithValue("$size", version.Size);
        cmd.Parameters.AddWithValue("$mod", version.ModifiedTicks);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return null;
        return new StoredAnalysis(
            r.GetInt32(0),
            TimeSpan.FromMilliseconds(r.GetInt64(1)),
            TimeSpan.FromMilliseconds(r.GetInt64(2)),
            TimeSpan.FromMilliseconds(r.GetInt64(3)),
            r.IsDBNull(4) ? null : r.GetDouble(4),
            r.IsDBNull(5) ? null : (byte[])r.GetValue(5));
    }

    public void SaveAnalysis(string path, FileVersion version, StoredAnalysis analysis)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO analysis (path, file_size, modified_utc, version, length_ms, start_ms, end_ms, bpm, waveform)
            VALUES ($path, $size, $mod, $version, $length, $start, $end, $bpm, $waveform)
            ON CONFLICT(path) DO UPDATE SET
                file_size = excluded.file_size, modified_utc = excluded.modified_utc, version = excluded.version,
                length_ms = excluded.length_ms, start_ms = excluded.start_ms, end_ms = excluded.end_ms,
                bpm = excluded.bpm, waveform = excluded.waveform;
            """;
        cmd.Parameters.AddWithValue("$path", path);
        cmd.Parameters.AddWithValue("$size", version.Size);
        cmd.Parameters.AddWithValue("$mod", version.ModifiedTicks);
        cmd.Parameters.AddWithValue("$version", analysis.Version);
        cmd.Parameters.AddWithValue("$length", (long)analysis.Length.TotalMilliseconds);
        cmd.Parameters.AddWithValue("$start", (long)analysis.AudibleStart.TotalMilliseconds);
        cmd.Parameters.AddWithValue("$end", (long)analysis.AudibleEnd.TotalMilliseconds);
        cmd.Parameters.AddWithValue("$bpm", (object?)analysis.Bpm ?? DBNull.Value);
        cmd.Parameters.Add(new SqliteParameter("$waveform", SqliteType.Blob) { Value = (object?)analysis.Waveform ?? DBNull.Value });
        cmd.ExecuteNonQuery();
    }

    /// <summary>The BPM range the DJ chose for a file, if any.</summary>
    public BpmRange? GetBpmRange(string path)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT min, max FROM bpm_ranges WHERE path = $path;";
        cmd.Parameters.AddWithValue("$path", path);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new BpmRange(r.GetInt32(0), r.GetInt32(1)) : null;
    }

    /// <summary>Chooses a BPM range for a file, or (null) goes back to automatic detection.</summary>
    public void SetBpmRange(string path, BpmRange? range)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = range is null
            ? "DELETE FROM bpm_ranges WHERE path = $path;"
            : "INSERT INTO bpm_ranges (path, min, max) VALUES ($path, $min, $max) ON CONFLICT(path) DO UPDATE SET min = excluded.min, max = excluded.max;";
        cmd.Parameters.AddWithValue("$path", path);
        if (range is { } r)
        {
            cmd.Parameters.AddWithValue("$min", r.Min);
            cmd.Parameters.AddWithValue("$max", r.Max);
        }
        cmd.ExecuteNonQuery();
    }

    /// <summary>Where the DJ set a file to start and end, if they did.</summary>
    public ManualTrim GetTrim(string path)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT start_ms, end_ms FROM trims WHERE path = $path;";
        cmd.Parameters.AddWithValue("$path", path);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return default;
        return new ManualTrim(
            r.IsDBNull(0) ? null : TimeSpan.FromMilliseconds(r.GetInt64(0)),
            r.IsDBNull(1) ? null : TimeSpan.FromMilliseconds(r.GetInt64(1)));
    }

    /// <summary>Saves where the DJ set a file to start and end (both automatic: forgets the file).</summary>
    public void SetTrim(string path, ManualTrim trim)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = trim.IsEmpty
            ? "DELETE FROM trims WHERE path = $path;"
            : """
              INSERT INTO trims (path, start_ms, end_ms) VALUES ($path, $start, $end)
                  ON CONFLICT(path) DO UPDATE SET start_ms = excluded.start_ms, end_ms = excluded.end_ms;
              """;
        cmd.Parameters.AddWithValue("$path", path);
        cmd.Parameters.AddWithValue("$start", trim.Start is { } start ? (long)start.TotalMilliseconds : DBNull.Value);
        cmd.Parameters.AddWithValue("$end", trim.End is { } end ? (long)end.TotalMilliseconds : DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Library tracks with no analysis from analyzer <paramref name="version"/> (or later) for the
    /// file as it is now, in folder order.
    /// </summary>
    public IReadOnlyList<PendingAnalysis> GetPendingAnalysis(int version)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"""
            SELECT t.path, t.file_size, t.modified_utc, t.bpm IS NOT NULL FROM tracks t {JoinAnalysis}
            WHERE a.path IS NULL OR a.version < $version
            ORDER BY t.root_id, t.folder COLLATE NOCASE, t.track_number, t.file_name COLLATE NOCASE;
            """;
        cmd.Parameters.AddWithValue("$version", version);
        using var r = cmd.ExecuteReader();
        var pending = new List<PendingAnalysis>();
        while (r.Read())
            pending.Add(new PendingAnalysis(r.GetString(0), new FileVersion(r.GetInt64(1), r.GetInt64(2)), r.GetBoolean(3)));
        return pending;
    }
}
