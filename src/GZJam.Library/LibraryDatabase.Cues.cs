using Microsoft.Data.Sqlite;

namespace GZJam.Library;

/// <summary>A cue the DJ marked in a song: a numbered point (1 to 4) to go to at a tap, like the outro.</summary>
public readonly record struct SongCue(int Number, TimeSpan At);

public sealed partial class LibraryDatabase
{
    /// <summary>The most cues a song can have, numbered 1 to this.</summary>
    public const int MaxCues = 4;

    /// <summary>A file's cues, by number.</summary>
    public IReadOnlyList<SongCue> GetCues(string path)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT number, at_ms FROM cues WHERE path = $path ORDER BY number;";
        cmd.Parameters.AddWithValue("$path", path);
        using var r = cmd.ExecuteReader();
        var cues = new List<SongCue>();
        while (r.Read())
            cues.Add(new SongCue(r.GetInt32(0), TimeSpan.FromMilliseconds(r.GetInt64(1))));
        return cues;
    }

    /// <summary>Replaces a file's cues (none: forgets them).</summary>
    public void SetCues(string path, IReadOnlyList<SongCue> cues)
    {
        using var c = Open();
        using var transaction = c.BeginTransaction();
        using (var clear = c.CreateCommand())
        {
            clear.CommandText = "DELETE FROM cues WHERE path = $path;";
            clear.Parameters.AddWithValue("$path", path);
            clear.ExecuteNonQuery();
        }
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO cues (path, number, at_ms) VALUES ($path, $number, $at);";
        cmd.Parameters.AddWithValue("$path", path);
        var number = cmd.Parameters.Add("$number", SqliteType.Integer);
        var at = cmd.Parameters.Add("$at", SqliteType.Integer);
        foreach (SongCue cue in cues.Where(c => c.Number is >= 1 and <= MaxCues).DistinctBy(c => c.Number))
        {
            number.Value = cue.Number;
            at.Value = (long)cue.At.TotalMilliseconds;
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
    }
}
