using Microsoft.Data.Sqlite;

namespace GZJam.Library;

/// <summary>A label (Mixxx calls them crates): "Lindy", "Balboa", "Female vocals". <paramref name="Count"/> songs carry it.</summary>
public sealed record LabelInfo(long Id, string Name, int Count);

public sealed partial class LibraryDatabase
{
    /// <summary>Separates label names in the library query (the unit separator: never typed).</summary>
    internal const char LabelSeparator = '\u001f';

    /// <summary>Every label, alphabetically, with how many songs carry it.</summary>
    public IReadOnlyList<LabelInfo> GetLabels()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT l.id, l.name, COUNT(tl.path) FROM labels l LEFT JOIN track_labels tl ON tl.label_id = l.id
            GROUP BY l.id ORDER BY l.name COLLATE NOCASE;
            """;
        using var r = cmd.ExecuteReader();
        var labels = new List<LabelInfo>();
        while (r.Read())
            labels.Add(new LabelInfo(r.GetInt64(0), r.GetString(1), r.GetInt32(2)));
        return labels;
    }

    /// <summary>Puts a label on files, creating it if it's new. Returns the label's name as stored (its first spelling wins).</summary>
    public string AddLabel(IEnumerable<string> paths, string name)
    {
        name = name.Trim();
        if (name.Length == 0)
            throw new ArgumentException("A label needs a name.", nameof(name));
        using var c = Open();
        using var transaction = c.BeginTransaction();
        using (var create = c.CreateCommand())
        {
            create.CommandText = "INSERT OR IGNORE INTO labels (name) VALUES ($name);";
            create.Parameters.AddWithValue("$name", name);
            create.ExecuteNonQuery();
        }
        string stored;
        using (var find = c.CreateCommand())
        {
            find.CommandText = "SELECT name FROM labels WHERE name = $name;";
            find.Parameters.AddWithValue("$name", name);
            stored = (string)find.ExecuteScalar()!;
        }
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO track_labels (path, label_id) SELECT $path, id FROM labels WHERE name = $name;
            """;
        var path = cmd.Parameters.Add("$path", SqliteType.Text);
        cmd.Parameters.AddWithValue("$name", name);
        foreach (string p in paths)
        {
            path.Value = p;
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
        return stored;
    }

    /// <summary>Takes a label off files. A label no song carries any more is forgotten.</summary>
    public void RemoveLabel(IEnumerable<string> paths, string name)
    {
        using var c = Open();
        using var transaction = c.BeginTransaction();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM track_labels WHERE path = $path AND label_id = (SELECT id FROM labels WHERE name = $name);";
            var path = cmd.Parameters.Add("$path", SqliteType.Text);
            cmd.Parameters.AddWithValue("$name", name);
            foreach (string p in paths)
            {
                path.Value = p;
                cmd.ExecuteNonQuery();
            }
        }
        using (var tidy = c.CreateCommand())
        {
            tidy.CommandText = "DELETE FROM labels WHERE name = $name AND id NOT IN (SELECT label_id FROM track_labels);";
            tidy.Parameters.AddWithValue("$name", name);
            tidy.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    /// <summary>
    /// Renames a label on every song that carries it. If another label already has the new name
    /// (in any case), the two become one, under that label's spelling. Returns the name it ends up
    /// with, or null if there's no label called <paramref name="name"/>.
    /// </summary>
    public string? RenameLabel(string name, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0)
            throw new ArgumentException("A label needs a name.", nameof(newName));
        using var c = Open();
        using var transaction = c.BeginTransaction();
        long? Find(string n)
        {
            using var find = c.CreateCommand();
            find.CommandText = "SELECT id FROM labels WHERE name = $name;";
            find.Parameters.AddWithValue("$name", n);
            return find.ExecuteScalar() as long?;
        }
        if (Find(name) is not { } id)
            return null;
        if (Find(newName) is { } other && other != id)
        {
            // Already a label by that name: its songs and these share it now.
            using var merge = c.CreateCommand();
            merge.CommandText = """
                INSERT OR IGNORE INTO track_labels (path, label_id) SELECT path, $other FROM track_labels WHERE label_id = $id;
                DELETE FROM track_labels WHERE label_id = $id;
                DELETE FROM labels WHERE id = $id;
                """;
            merge.Parameters.AddWithValue("$id", id);
            merge.Parameters.AddWithValue("$other", other);
            merge.ExecuteNonQuery();
            id = other;
        }
        else
        {
            using var rename = c.CreateCommand();
            rename.CommandText = "UPDATE labels SET name = $name WHERE id = $id;";
            rename.Parameters.AddWithValue("$name", newName);
            rename.Parameters.AddWithValue("$id", id);
            rename.ExecuteNonQuery();
        }
        string stored;
        using (var read = c.CreateCommand())
        {
            read.CommandText = "SELECT name FROM labels WHERE id = $id;";
            read.Parameters.AddWithValue("$id", id);
            stored = (string)read.ExecuteScalar()!;
        }
        transaction.Commit();
        return stored;
    }

    /// <summary>The labels on a file, alphabetically.</summary>
    public IReadOnlyList<string> GetLabels(string path)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT l.name FROM track_labels tl JOIN labels l ON l.id = tl.label_id
            WHERE tl.path = $path ORDER BY l.name COLLATE NOCASE;
            """;
        cmd.Parameters.AddWithValue("$path", path);
        using var r = cmd.ExecuteReader();
        var names = new List<string>();
        while (r.Read())
            names.Add(r.GetString(0));
        return names;
    }

    /// <summary>Splits the label names the library query packs into one column.</summary>
    private static IReadOnlyList<string> ReadLabels(string? packed) => string.IsNullOrEmpty(packed)
        ? []
        : packed.Split(LabelSeparator).Order(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// Search text split into tokens the way the search reads it: on spaces, but keeping quoted
    /// text together (<c>label:"female vocals"</c> is one token).
    /// </summary>
    public static IEnumerable<string> SearchTokens(string text) => SearchQuery.Tokenize(text);
}
