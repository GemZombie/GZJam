using Microsoft.Data.Sqlite;

namespace GZJam.Library;

/// <summary>A jump the DJ set in a song: from the end of one phrase to another (forward skips, backward repeats).</summary>
public readonly record struct SongJump(TimeSpan From, TimeSpan To);

public sealed partial class LibraryDatabase
{
    /// <summary>The most jumps a song can have.</summary>
    public const int MaxJumps = 2;

    /// <summary>A file's jumps, in the order they were set.</summary>
    public IReadOnlyList<SongJump> GetJumps(string path)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT from_ms, to_ms FROM jumps WHERE path = $path ORDER BY slot;";
        cmd.Parameters.AddWithValue("$path", path);
        using var r = cmd.ExecuteReader();
        var jumps = new List<SongJump>();
        while (r.Read())
            jumps.Add(new SongJump(TimeSpan.FromMilliseconds(r.GetInt64(0)), TimeSpan.FromMilliseconds(r.GetInt64(1))));
        return jumps;
    }

    /// <summary>Replaces a file's jumps (none: forgets them).</summary>
    public void SetJumps(string path, IReadOnlyList<SongJump> jumps)
    {
        using var c = Open();
        using var transaction = c.BeginTransaction();
        using (var clear = c.CreateCommand())
        {
            clear.CommandText = "DELETE FROM jumps WHERE path = $path;";
            clear.Parameters.AddWithValue("$path", path);
            clear.ExecuteNonQuery();
        }
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO jumps (path, slot, from_ms, to_ms) VALUES ($path, $slot, $from, $to);";
        cmd.Parameters.AddWithValue("$path", path);
        var slot = cmd.Parameters.Add("$slot", SqliteType.Integer);
        var from = cmd.Parameters.Add("$from", SqliteType.Integer);
        var to = cmd.Parameters.Add("$to", SqliteType.Integer);
        for (int i = 0; i < Math.Min(jumps.Count, MaxJumps); i++)
        {
            slot.Value = i;
            from.Value = (long)jumps[i].From.TotalMilliseconds;
            to.Value = (long)jumps[i].To.TotalMilliseconds;
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
    }
}
