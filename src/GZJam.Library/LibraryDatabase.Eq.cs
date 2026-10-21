namespace GZJam.Library;

/// <summary>
/// A deck's tone controls for a recording, in dB: 0 is flat. Low and high are shelves, mid is a
/// bell around 1 kHz.
/// </summary>
public readonly record struct TrackEq(double Low, double Mid, double High)
{
    /// <summary>The most a band can cut, and boost.</summary>
    public const double MinDb = -24, MaxDb = 6;

    public static TrackEq Flat => default;

    public bool IsFlat => Low == 0 && Mid == 0 && High == 0;
}

public sealed partial class LibraryDatabase
{
    /// <summary>The EQ the DJ set for a file (flat if never set).</summary>
    public TrackEq GetEq(string path)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT low_db, mid_db, high_db FROM eq WHERE path = $path;";
        cmd.Parameters.AddWithValue("$path", path);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new TrackEq(r.GetDouble(0), r.GetDouble(1), r.GetDouble(2)) : TrackEq.Flat;
    }

    /// <summary>Saves a file's EQ (flat: forgets the file).</summary>
    public void SetEq(string path, TrackEq eq)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = eq.IsFlat
            ? "DELETE FROM eq WHERE path = $path;"
            : """
              INSERT INTO eq (path, low_db, mid_db, high_db) VALUES ($path, $low, $mid, $high)
                  ON CONFLICT(path) DO UPDATE SET low_db = excluded.low_db, mid_db = excluded.mid_db, high_db = excluded.high_db;
              """;
        cmd.Parameters.AddWithValue("$path", path);
        cmd.Parameters.AddWithValue("$low", eq.Low);
        cmd.Parameters.AddWithValue("$mid", eq.Mid);
        cmd.Parameters.AddWithValue("$high", eq.High);
        cmd.ExecuteNonQuery();
    }
}
