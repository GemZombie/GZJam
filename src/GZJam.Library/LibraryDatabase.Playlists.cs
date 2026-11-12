using Microsoft.Data.Sqlite;

namespace GZJam.Library;

/// <summary>A playlist: <paramref name="Count"/> entries (songs, a song perhaps more than once), <paramref name="Length"/> in all.</summary>
/// <param name="FollowsServer">The server whose playlist this is a kept copy of (#290): each sync makes it the server's again. Null for the DJ's own.</param>
/// <param name="FolderId">The folder it's in (#354); null at the top.</param>
/// <param name="Depth">How many folders deep: 0 at the top.</param>
public sealed record PlaylistInfo(long Id, string Name, int Count, TimeSpan Length, string? FollowsServer = null,
    long? FolderId = null, int Depth = 0)
{
    public bool IsFromServer => FollowsServer is not null;

    /// <summary>Its name, and for a server's, that it follows the server.</summary>
    public string Tip => FollowsServer is { } server
        ? $"{Name}{Environment.NewLine}Kept from {server}: each sync makes it the server's again, so change it there (or copy it into a playlist of your own)."
        : Name;
}

/// <summary>A folder of playlists (#354), in another or at the top (<paramref name="ParentId"/> null).</summary>
/// <param name="Depth">How many folders it's in: 0 at the top.</param>
/// <param name="Count">How many playlists it holds, all the way down.</param>
public sealed record PlaylistFolder(long Id, long? ParentId, string Name, int Depth, int Count);

/// <summary>
/// Playlists: a set planned before the night, in order, saved by name. Unlike labels (what a song
/// is, in no order), a playlist is a plan: ordered, and a song can be in it twice. Entries are kept
/// by path, never in the music files; a song missing from the library is kept but not shown.
/// </summary>
public sealed partial class LibraryDatabase
{
    /// <summary>Every playlist, alphabetically, with how many songs and how long, and where it is among the folders.</summary>
    public IReadOnlyList<PlaylistInfo> GetPlaylists()
    {
        using var c = Open();
        var depths = FolderDepths(ReadFolders(c));
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT p.id, p.name, COUNT(e.id), COALESCE(SUM(t.duration_ms), 0),
                   (SELECT s.name FROM server_choices sc JOIN servers s ON s.id = sc.server_id
                    WHERE sc.playlist_id = p.id AND sc.kind = 'playlist' LIMIT 1),
                   p.folder_id
            FROM playlists p LEFT JOIN playlist_entries e ON e.playlist_id = p.id LEFT JOIN tracks t ON t.path = e.path
            GROUP BY p.id ORDER BY p.name COLLATE NOCASE;
            """;
        using var r = cmd.ExecuteReader();
        var playlists = new List<PlaylistInfo>();
        while (r.Read())
        {
            long? folder = r.IsDBNull(5) ? null : r.GetInt64(5);
            playlists.Add(new PlaylistInfo(r.GetInt64(0), r.GetString(1), r.GetInt32(2), TimeSpan.FromMilliseconds(r.GetInt64(3)),
                r.IsDBNull(4) ? null : r.GetString(4), folder, folder is { } f && depths.TryGetValue(f, out int d) ? d + 1 : 0));
        }
        return playlists;
    }

    // ───────────── Folders (#354) ─────────────

    /// <summary>Every folder of playlists, alphabetically, each with how deep it is and how many playlists it holds, all the way down.</summary>
    public IReadOnlyList<PlaylistFolder> GetPlaylistFolders()
    {
        using var c = Open();
        var folders = ReadFolders(c);
        var depths = FolderDepths(folders);
        var direct = new Dictionary<long, int>();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT folder_id, COUNT(*) FROM playlists WHERE folder_id IS NOT NULL GROUP BY folder_id;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                direct[r.GetInt64(0)] = r.GetInt32(1);
        }
        // Each folder's own playlists count in it and in every folder it's in.
        var parents = folders.ToDictionary(f => f.Id, f => f.ParentId);
        var all = new Dictionary<long, int>();
        foreach (var (id, own) in direct)
        {
            int steps = 0;
            for (long? at = id; at is { } here && steps++ <= folders.Count; at = parents.GetValueOrDefault(here))
                all[here] = all.GetValueOrDefault(here) + own;
        }
        return folders
            .Select(f => new PlaylistFolder(f.Id, f.ParentId, f.Name, depths.GetValueOrDefault(f.Id), all.GetValueOrDefault(f.Id)))
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Makes a folder of playlists, in <paramref name="parentId"/> or at the top. A name another
    /// folder there has gets a number. Returns it.
    /// </summary>
    public PlaylistFolder CreatePlaylistFolder(string name, long? parentId = null)
    {
        name = CleanName(name);
        using var c = Open();
        string unique = UniqueFolderName(c, name, parentId, except: null);
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO playlist_folders (parent_id, name) VALUES ($parent, $name); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$parent", (object?)parentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", unique);
        long id = (long)cmd.ExecuteScalar()!;
        return new PlaylistFolder(id, parentId, unique, FolderDepths(ReadFolders(c)).GetValueOrDefault(id), 0);
    }

    /// <summary>Renames a folder; a name another folder beside it has gets a number. Returns the name as stored.</summary>
    public string RenamePlaylistFolder(long id, string name)
    {
        name = CleanName(name);
        using var c = Open();
        long? parent = ReadFolders(c).FirstOrDefault(f => f.Id == id).ParentId;
        string unique = UniqueFolderName(c, name, parent, except: id);
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE playlist_folders SET name = $name WHERE id = $id;";
        cmd.Parameters.AddWithValue("$name", unique);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        return unique;
    }

    /// <summary>
    /// Moves a folder into another, or to the top (null). Not into itself or a folder inside it:
    /// then nothing moves, and false. A name taken there gets a number.
    /// </summary>
    public bool MovePlaylistFolder(long id, long? parentId)
    {
        using var c = Open();
        var folders = ReadFolders(c);
        for (long? at = parentId; at is { } here; at = folders.FirstOrDefault(f => f.Id == here).ParentId)
        {
            if (here == id)
                return false;
        }
        string name = folders.FirstOrDefault(f => f.Id == id).Name ?? "";
        string unique = UniqueFolderName(c, name, parentId, except: id);
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE playlist_folders SET parent_id = $parent, name = $name WHERE id = $id;";
        cmd.Parameters.AddWithValue("$parent", (object?)parentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", unique);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        return true;
    }

    /// <summary>Deletes a folder with everything in it: its folders and their playlists. The songs stay in the library.</summary>
    public void DeletePlaylistFolder(long id)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM playlist_folders WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Moves a playlist into a folder, or to the top (null). A name taken there gets a number; returns the name as stored.</summary>
    public string MovePlaylist(long id, long? folderId)
    {
        using var c = Open();
        string name;
        using (var read = c.CreateCommand())
        {
            read.CommandText = "SELECT name FROM playlists WHERE id = $id;";
            read.Parameters.AddWithValue("$id", id);
            name = read.ExecuteScalar() as string ?? "";
        }
        string unique = UniqueName(c, name, except: id, folderId);
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE playlists SET folder_id = $folder, name = $name WHERE id = $id;";
        cmd.Parameters.AddWithValue("$folder", (object?)folderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", unique);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        return unique;
    }

    /// <summary>The names of the folders a folder is in, from the top down, and its own: "Setlists", "2026".</summary>
    public IReadOnlyList<string> PlaylistFolderPath(long? folderId)
    {
        using var c = Open();
        var folders = ReadFolders(c);
        var path = new List<string>();
        for (long? at = folderId; at is { } here && path.Count <= folders.Count; )
        {
            var folder = folders.FirstOrDefault(f => f.Id == here);
            if (folder.Name is null)
                break;
            path.Insert(0, folder.Name);
            at = folder.ParentId;
        }
        return path;
    }

    private static List<(long Id, long? ParentId, string Name)> ReadFolders(SqliteConnection c)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, parent_id, name FROM playlist_folders;";
        using var r = cmd.ExecuteReader();
        var folders = new List<(long, long?, string)>();
        while (r.Read())
            folders.Add((r.GetInt64(0), r.IsDBNull(1) ? null : r.GetInt64(1), r.GetString(2)));
        return folders;
    }

    /// <summary>How many folders each folder is in (0 at the top).</summary>
    private static Dictionary<long, int> FolderDepths(List<(long Id, long? ParentId, string Name)> folders)
    {
        var parents = folders.ToDictionary(f => f.Id, f => f.ParentId);
        var depths = new Dictionary<long, int>();
        foreach (var (id, _, _) in folders)
        {
            int depth = 0;
            for (long? at = parents[id]; at is { } here && depth <= folders.Count; at = parents.GetValueOrDefault(here))
                depth++;
            depths[id] = depth;
        }
        return depths;
    }

    /// <summary><paramref name="name"/>, or with a number after it if another folder beside it has it already.</summary>
    private static string UniqueFolderName(SqliteConnection c, string name, long? parentId, long? except)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM playlist_folders WHERE name = $name AND parent_id IS $parent AND id IS NOT $except;";
        var nameParameter = cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$parent", (object?)parentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$except", (object?)except ?? DBNull.Value);
        string candidate = name;
        for (int n = 2; cmd.ExecuteScalar() is not null; n++)
            nameParameter.Value = candidate = $"{name} {n}";
        return candidate;
    }

    public PlaylistInfo? GetPlaylist(long id) => GetPlaylists().FirstOrDefault(p => p.Id == id);

    /// <summary>The names of the playlists a file is in, alphabetically: for its song's info (#129).</summary>
    public IReadOnlyList<string> GetPlaylistsWith(string path)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT p.name FROM playlists p JOIN playlist_entries e ON e.playlist_id = p.id
            WHERE e.path = $path ORDER BY p.name COLLATE NOCASE;
            """;
        cmd.Parameters.AddWithValue("$path", path);
        using var r = cmd.ExecuteReader();
        var names = new List<string>();
        while (r.Read())
            names.Add(r.GetString(0));
        return names;
    }

    /// <summary>
    /// Makes an empty playlist, in <paramref name="folderId"/> or at the top. A name already taken
    /// there gets a number ("Saturday 2"), so a playlist is never silently merged into another;
    /// the same name in another folder is fine (#354). Returns it.
    /// </summary>
    public PlaylistInfo CreatePlaylist(string name, long? folderId = null)
    {
        name = CleanName(name);
        using var c = Open();
        string unique = UniqueName(c, name, except: null, folderId);
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO playlists (name, folder_id) VALUES ($name, $folder); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$name", unique);
        cmd.Parameters.AddWithValue("$folder", (object?)folderId ?? DBNull.Value);
        long id = (long)cmd.ExecuteScalar()!;
        int depth = folderId is { } f && FolderDepths(ReadFolders(c)).TryGetValue(f, out int d) ? d + 1 : 0;
        return new PlaylistInfo(id, unique, 0, TimeSpan.Zero, FolderId: folderId, Depth: depth);
    }

    /// <summary>Renames a playlist; a name another one in its folder has gets a number. Returns the name as stored.</summary>
    public string RenamePlaylist(long id, string name)
    {
        name = CleanName(name);
        using var c = Open();
        long? folder;
        using (var read = c.CreateCommand())
        {
            read.CommandText = "SELECT folder_id FROM playlists WHERE id = $id;";
            read.Parameters.AddWithValue("$id", id);
            folder = read.ExecuteScalar() is long f ? f : null;
        }
        string unique = UniqueName(c, name, except: id, folder);
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE playlists SET name = $name WHERE id = $id;";
        cmd.Parameters.AddWithValue("$name", unique);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        return unique;
    }

    /// <summary>Deletes a playlist. Its songs stay in the library, untouched.</summary>
    public void DeletePlaylist(long id)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM playlists WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>A playlist's entries in order: each entry's id and its file.</summary>
    public IReadOnlyList<(long EntryId, string Path)> GetPlaylistEntries(long id)
    {
        using var c = Open();
        return ReadEntries(c, id);
    }

    /// <summary>Adds files to a playlist at <paramref name="index"/> (0: the top; null or past the end: the end).</summary>
    public void AddToPlaylist(long id, IEnumerable<string> paths, int? index = null)
    {
        using var c = Open();
        using var transaction = c.BeginTransaction();
        var entries = ReadEntries(c, id).ToList();
        int at = Math.Clamp(index ?? entries.Count, 0, entries.Count);
        entries.InsertRange(at, paths.Select(p => (EntryId: 0L, Path: p)));
        WriteEntries(c, id, entries);
        transaction.Commit();
    }

    /// <summary>Takes entries out of a playlist (the files are untouched).</summary>
    public void RemoveFromPlaylist(long id, IEnumerable<long> entryIds)
    {
        var remove = entryIds.ToHashSet();
        using var c = Open();
        using var transaction = c.BeginTransaction();
        WriteEntries(c, id, ReadEntries(c, id).Where(e => !remove.Contains(e.EntryId)).ToList());
        transaction.Commit();
    }

    /// <summary>
    /// Moves entries, keeping their order among themselves, to before the entry that was at
    /// <paramref name="index"/> (counting before the move; past the end: to the end).
    /// </summary>
    public void MovePlaylistEntries(long id, IEnumerable<long> entryIds, int index)
    {
        var moving = entryIds.ToHashSet();
        using var c = Open();
        using var transaction = c.BeginTransaction();
        var entries = ReadEntries(c, id).ToList();
        int at = Math.Clamp(index, 0, entries.Count);
        int before = entries.Take(at).Count(e => moving.Contains(e.EntryId));
        var moved = entries.Where(e => moving.Contains(e.EntryId)).ToList();
        entries.RemoveAll(e => moving.Contains(e.EntryId));
        entries.InsertRange(Math.Clamp(at - before, 0, entries.Count), moved);
        WriteEntries(c, id, entries);
        transaction.Commit();
    }

    private static List<(long EntryId, string Path)> ReadEntries(SqliteConnection c, long id)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, path FROM playlist_entries WHERE playlist_id = $id ORDER BY position;";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        var entries = new List<(long, string)>();
        while (r.Read())
            entries.Add((r.GetInt64(0), r.GetString(1)));
        return entries;
    }

    /// <summary>Numbers the entries 0, 1, 2… in this order; new ones (id 0) are added. Entries not listed are removed.</summary>
    private static void WriteEntries(SqliteConnection c, long id, IReadOnlyList<(long EntryId, string Path)> entries)
    {
        var keep = entries.Where(e => e.EntryId != 0).Select(e => e.EntryId).ToList();
        using (var delete = c.CreateCommand())
        {
            delete.CommandText = $"DELETE FROM playlist_entries WHERE playlist_id = $id AND id NOT IN ({string.Join(',', keep.DefaultIfEmpty(-1))});";
            delete.Parameters.AddWithValue("$id", id);
            delete.ExecuteNonQuery();
        }
        using var update = c.CreateCommand();
        update.CommandText = "UPDATE playlist_entries SET position = $position WHERE id = $entry;";
        var updatePosition = update.Parameters.Add("$position", SqliteType.Integer);
        var updateEntry = update.Parameters.Add("$entry", SqliteType.Integer);
        using var insert = c.CreateCommand();
        insert.CommandText = "INSERT INTO playlist_entries (playlist_id, position, path) VALUES ($id, $position, $path);";
        insert.Parameters.AddWithValue("$id", id);
        var insertPosition = insert.Parameters.Add("$position", SqliteType.Integer);
        var insertPath = insert.Parameters.Add("$path", SqliteType.Text);
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].EntryId != 0)
            {
                (updatePosition.Value, updateEntry.Value) = (i, entries[i].EntryId);
                update.ExecuteNonQuery();
            }
            else
            {
                (insertPosition.Value, insertPath.Value) = (i, entries[i].Path);
                insert.ExecuteNonQuery();
            }
        }
    }

    private static string CleanName(string name)
    {
        name = name.Trim();
        if (name.Length == 0)
            throw new ArgumentException("A playlist needs a name.", nameof(name));
        return name;
    }

    /// <summary><paramref name="name"/>, or with a number after it if another playlist in the folder has it already.</summary>
    private static string UniqueName(SqliteConnection c, string name, long? except, long? folderId)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM playlists WHERE name = $name AND id IS NOT $except AND folder_id IS $folder;";
        var nameParameter = cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$except", (object?)except ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$folder", (object?)folderId ?? DBNull.Value);
        string candidate = name;
        for (int n = 2; cmd.ExecuteScalar() is not null; n++)
            nameParameter.Value = candidate = $"{name} {n}";
        return candidate;
    }
}
