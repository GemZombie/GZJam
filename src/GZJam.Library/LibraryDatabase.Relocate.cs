using Microsoft.Data.Sqlite;

namespace GZJam.Library;

/// <summary>
/// A library moved to a computer where the music lives somewhere else (#112): a backup made with
/// the music under <c>C:\Music</c>, restored on a laptop where it's under <c>D:\Music</c> or
/// <c>/home/sam/Music</c>. Everything kept by a song's path follows its music folder there.
/// </summary>
public sealed partial class LibraryDatabase
{
    /// <summary>
    /// Moves each music folder <c>From</c> to <c>To</c> in the library file at
    /// <paramref name="databasePath"/> (not open elsewhere: a backup being restored): the folder
    /// itself, its songs, and everything kept by their paths (<see cref="MovedTables"/>). The
    /// part of each path under the folder keeps its names, with this computer's separator.
    /// Returns how many songs moved.
    /// </summary>
    public static int MoveMusicFolders(string databasePath, IReadOnlyList<(string From, string To)> moves)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString());
        c.Open();
        using var tx = c.BeginTransaction();
        int songs = 0;
        foreach (var (from, to) in moves)
        {
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "UPDATE roots SET path = $to WHERE path = $from;";
                cmd.Parameters.AddWithValue("$from", from);
                cmd.Parameters.AddWithValue("$to", to);
                cmd.ExecuteNonQuery();
            }
            songs += MovePaths(c, "tracks", from, to, keepOne: true);
            foreach (string table in SongDataTables)
                MovePaths(c, table, from, to, keepOne: true);
            foreach (string table in SongListTables)
                MovePaths(c, table, from, to, keepOne: false);
        }
        tx.Commit();
        return songs;
    }

    /// <summary>
    /// Rewrites the paths under <paramref name="from"/> in one table. Where a table keeps one row
    /// per song (<paramref name="keepOne"/>), a row already at the new path gives way.
    /// </summary>
    private static int MovePaths(SqliteConnection c, string table, string from, string to, bool keepOne)
    {
        var paths = new List<string>();
        using (var cmd = c.CreateCommand())
        {
            // A rough match in SQL (any case, either separator); MovedPath decides.
            cmd.CommandText = $"SELECT DISTINCT path FROM {table} WHERE substr(path, 1, $length) = $from COLLATE NOCASE;";
            cmd.Parameters.AddWithValue("$from", from);
            cmd.Parameters.AddWithValue("$length", from.Length);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                paths.Add(r.GetString(0));
        }
        int moved = 0;
        using var update = c.CreateCommand();
        update.CommandText = keepOne
            ? $"DELETE FROM {table} WHERE path = $new; UPDATE {table} SET path = $new WHERE path = $old;"
            : $"UPDATE {table} SET path = $new WHERE path = $old;";
        var oldPath = update.Parameters.Add("$old", SqliteType.Text);
        var newPath = update.Parameters.Add("$new", SqliteType.Text);
        foreach (string path in paths)
        {
            if (MovedPath(path, from, to) is not { } moving || moving == path)
                continue;
            oldPath.Value = path;
            newPath.Value = moving;
            update.ExecuteNonQuery();
            moved++;
        }
        return moved;
    }

    /// <summary>
    /// <paramref name="path"/> under <paramref name="to"/> rather than <paramref name="from"/>, or
    /// null if it isn't under it. A Windows folder (a drive letter, or <c>\</c>) matches in any
    /// case and splits on either separator; any other only on <c>/</c>, as its names may hold a
    /// <c>\</c>. The rest is joined with this computer's separator.
    /// </summary>
    public static string? MovedPath(string path, string from, string to)
    {
        bool windows = from.Contains('\\') || (from.Length >= 2 && from[1] == ':');
        char[] separators = windows ? ['\\', '/'] : ['/'];
        string root = from.TrimEnd(separators);
        if (root.Length == 0)
            root = from;   // "/" itself
        var comparison = windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (path.Equals(root, comparison) || path.Equals(from, comparison))
            return to;
        bool under = path.StartsWith(root, comparison) && path.Length > root.Length
            && (separators.Contains(path[root.Length]) || separators.Contains(root[^1]));
        if (!under)
            return null;
        string[] parts = path[root.Length..].Split(separators, StringSplitOptions.RemoveEmptyEntries);
        return Path.Combine([to, .. parts]);
    }
}
