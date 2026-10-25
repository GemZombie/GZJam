namespace GZJam.Library;

/// <summary>
/// A deck's tone controls for a recording, in dB: 0 is flat. Low and high are shelves, mid is a
/// bell around <paramref name="MidHz"/>. <paramref name="Gain"/> is the DJ's adjustment on top of
/// loudness matching. <paramref name="Even"/> (0 to 100) evens out a recording whose quiet and
/// loud passages are far apart: a compressor, off at 0.
/// </summary>
public readonly record struct TrackEq(double Low, double Mid, double High, double Gain = 0, double MidHz = TrackEq.DefaultMidHz, double Even = 0)
{
    /// <summary>The most a band (or the gain) can cut, and boost.</summary>
    public const double MinDb = -24, MaxDb = 6;

    /// <summary>Where the mid band sits unless the DJ moves it.</summary>
    public const double DefaultMidHz = 1000;

    /// <summary>Where the mid band can go: the third-octave centers a graphic EQ uses, 200 Hz to 5 kHz.</summary>
    public static readonly IReadOnlyList<double> MidFrequencies =
        [200, 250, 315, 400, 500, 630, 800, 1000, 1250, 1600, 2000, 2500, 3150, 4000, 5000];

    /// <summary>The most <see cref="Even"/> goes to.</summary>
    public const double MaxEven = 100;

    public static TrackEq Flat => new(0, 0, 0);

    public bool IsFlat => Low == 0 && Mid == 0 && High == 0 && Gain == 0 && Even == 0 && IsMidAtDefault;

    /// <summary>The mid band is where it starts (a zero frequency is the struct's default, and means the same).</summary>
    public bool IsMidAtDefault => MidHz is DefaultMidHz or 0;

    /// <summary>The mid band's frequency, the default for an unset one.</summary>
    public double MidHzOrDefault => MidHz > 0 ? MidHz : DefaultMidHz;
}

public sealed partial class LibraryDatabase
{
    /// <summary>The EQ the DJ set for a file (flat if never set).</summary>
    public TrackEq GetEq(string path)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT low_db, mid_db, high_db, gain_db, mid_hz, even FROM eq WHERE path = $path;";
        cmd.Parameters.AddWithValue("$path", path);
        using var r = cmd.ExecuteReader();
        return r.Read()
            ? new TrackEq(r.GetDouble(0), r.GetDouble(1), r.GetDouble(2), r.GetDouble(3), r.GetDouble(4), r.GetDouble(5))
            : TrackEq.Flat;
    }

    /// <summary>Saves a file's EQ (flat: forgets the file).</summary>
    public void SetEq(string path, TrackEq eq)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = eq.IsFlat
            ? "DELETE FROM eq WHERE path = $path;"
            : """
              INSERT INTO eq (path, low_db, mid_db, high_db, gain_db, mid_hz, even) VALUES ($path, $low, $mid, $high, $gain, $midHz, $even)
                  ON CONFLICT(path) DO UPDATE SET low_db = excluded.low_db, mid_db = excluded.mid_db, high_db = excluded.high_db,
                      gain_db = excluded.gain_db, mid_hz = excluded.mid_hz, even = excluded.even;
              """;
        cmd.Parameters.AddWithValue("$path", path);
        cmd.Parameters.AddWithValue("$low", eq.Low);
        cmd.Parameters.AddWithValue("$mid", eq.Mid);
        cmd.Parameters.AddWithValue("$high", eq.High);
        cmd.Parameters.AddWithValue("$gain", eq.Gain);
        cmd.Parameters.AddWithValue("$midHz", eq.MidHzOrDefault);
        cmd.Parameters.AddWithValue("$even", eq.Even);
        cmd.ExecuteNonQuery();
    }
}
