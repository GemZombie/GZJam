namespace GZJam.Library;

/// <summary>Which picture a song shows, as last found: <see cref="Key"/> names the small copy, null for none.</summary>
public readonly record struct StoredArt(string? Key);

public sealed partial class LibraryDatabase
{
    /// <summary>
    /// The picture found for a file, if it still matches the file (<paramref name="version"/>),
    /// its folder (<paramref name="folderModifiedTicks"/>) and the way pictures are found now.
    /// </summary>
    public StoredArt? GetArt(string path, FileVersion version, long folderModifiedTicks)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT art_key FROM art
            WHERE path = $path AND file_size = $size AND modified_utc = $mod AND folder_modified = $folder AND version = $version;
            """;
        cmd.Parameters.AddWithValue("$path", path);
        cmd.Parameters.AddWithValue("$size", version.Size);
        cmd.Parameters.AddWithValue("$mod", version.ModifiedTicks);
        cmd.Parameters.AddWithValue("$folder", folderModifiedTicks);
        cmd.Parameters.AddWithValue("$version", AlbumArtFinder.Version);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return null;
        return new StoredArt(r.IsDBNull(0) ? null : r.GetString(0));
    }

    public void SaveArt(string path, FileVersion version, long folderModifiedTicks, StoredArt art)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO art (path, file_size, modified_utc, folder_modified, version, art_key)
            VALUES ($path, $size, $mod, $folder, $version, $key)
            ON CONFLICT(path) DO UPDATE SET
                file_size = excluded.file_size, modified_utc = excluded.modified_utc,
                folder_modified = excluded.folder_modified, version = excluded.version, art_key = excluded.art_key;
            """;
        cmd.Parameters.AddWithValue("$path", path);
        cmd.Parameters.AddWithValue("$size", version.Size);
        cmd.Parameters.AddWithValue("$mod", version.ModifiedTicks);
        cmd.Parameters.AddWithValue("$folder", folderModifiedTicks);
        cmd.Parameters.AddWithValue("$version", AlbumArtFinder.Version);
        cmd.Parameters.AddWithValue("$key", (object?)art.Key ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
}
