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
