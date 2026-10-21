using Microsoft.Data.Sqlite;

namespace GZJam.Library;

public sealed partial class LibraryDatabase
{
    /// <summary>The DJ's rating and energy for a file (unrated and unset if never marked).</summary>
    public (Rating Rating, Energy Energy) GetMarks(string path)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT rating, energy FROM marks WHERE path = $path;";
        cmd.Parameters.AddWithValue("$path", path);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ((Rating)r.GetInt32(0), (Energy)r.GetInt32(1)) : (Rating.Unrated, Energy.Unset);
    }

    /// <summary>Rates files, keeping their energy marks.</summary>
    public void SetRating(IEnumerable<string> paths, Rating rating) => SetMark(paths, "rating", (int)rating);

    /// <summary>Marks files' energy, keeping their ratings.</summary>
    public void SetEnergy(IEnumerable<string> paths, Energy energy) => SetMark(paths, "energy", (int)energy);

    private void SetMark(IEnumerable<string> paths, string column, int value)
    {
        using var c = Open();
        using var transaction = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        // An unmarked file has no row, so clearing the last mark removes it.
        cmd.CommandText = $"""
            INSERT INTO marks (path, {column}) VALUES ($path, $value)
                ON CONFLICT(path) DO UPDATE SET {column} = excluded.{column};
            DELETE FROM marks WHERE path = $path AND rating = 0 AND energy = 0;
            """;
        var path = cmd.Parameters.Add("$path", SqliteType.Text);
        cmd.Parameters.AddWithValue("$value", value);
        foreach (string p in paths)
        {
            path.Value = p;
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
    }
}
