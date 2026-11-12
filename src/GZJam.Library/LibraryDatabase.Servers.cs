using GZJam.Library.Servers;
using Microsoft.Data.Sqlite;

namespace GZJam.Library;

public sealed partial class LibraryDatabase
{
    // ───────────── Music servers (#184, #282) ─────────────

    /// <summary>Every server the DJ signed in to, by name.</summary>
    public IReadOnlyList<MusicServer> GetServers()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, address, user_name, can_download, server_type, server_version, checked_utc, playlist_separator
            FROM servers ORDER BY name COLLATE NOCASE, id;
            """;
        using var r = cmd.ExecuteReader();
        var servers = new List<MusicServer>();
        while (r.Read())
            servers.Add(ReadServer(r));
        return servers;
    }

    public MusicServer? GetServer(long id) => GetServers().FirstOrDefault(s => s.Id == id);

    /// <summary>
    /// Adds a server, or, for the same address and user name as one already here, renames that
    /// one. Returns it as stored.
    /// </summary>
    public MusicServer AddServer(string name, string address, string userName)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO servers (name, address, user_name) VALUES ($name, $address, $user)
            ON CONFLICT (address, user_name) DO UPDATE SET name = excluded.name;
            SELECT id FROM servers WHERE address = $address AND user_name = $user;
            """;
        cmd.Parameters.AddWithValue("$name", name.Trim());
        cmd.Parameters.AddWithValue("$address", address);
        cmd.Parameters.AddWithValue("$user", userName);
        long id = (long)cmd.ExecuteScalar()!;
        return GetServer(id)!;
    }

    /// <summary>Changes a server's name, address or user name; what was last checked is forgotten if the address or user changes.</summary>
    public MusicServer UpdateServer(long id, string name, string address, string userName)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            UPDATE servers SET
                can_download   = CASE WHEN address = $address AND user_name = $user THEN can_download END,
                server_type    = CASE WHEN address = $address AND user_name = $user THEN server_type END,
                server_version = CASE WHEN address = $address AND user_name = $user THEN server_version END,
                checked_utc    = CASE WHEN address = $address AND user_name = $user THEN checked_utc END,
                name = $name, address = $address, user_name = $user
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$name", name.Trim());
        cmd.Parameters.AddWithValue("$address", address);
        cmd.Parameters.AddWithValue("$user", userName);
        cmd.ExecuteNonQuery();
        return GetServer(id) ?? throw new InvalidOperationException($"No server {id}.");
    }

    /// <summary>Keeps what a successful check found: whether this account may download, and what the server is.</summary>
    public void SaveServerCheck(long id, ServerCheck check, DateTime nowUtc)
    {
        if (!check.Connected)
            return;
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            UPDATE servers SET can_download = $download, server_type = $type, server_version = $version, checked_utc = $checked
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$download", check.CanDownload is { } can ? (can ? 1 : 0) : DBNull.Value);
        cmd.Parameters.AddWithValue("$type", (object?)check.ServerType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$version", (object?)check.ServerVersion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$checked", nowUtc.Ticks);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Forgets a server. Its password is the caller's to remove from the keychain.</summary>
    public void RemoveServer(long id)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM servers WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private static MusicServer ReadServer(SqliteDataReader r) => new(
        r.GetInt64(0),
        r.GetString(1),
        r.GetString(2),
        r.GetString(3),
        r.IsDBNull(4) ? null : r.GetInt64(4) != 0,
        r.IsDBNull(5) ? null : r.GetString(5),
        r.IsDBNull(6) ? null : r.GetString(6),
        r.IsDBNull(7) ? null : new DateTime(r.GetInt64(7), DateTimeKind.Utc),
        r.IsDBNull(8) ? null : r.GetString(8));

    /// <summary>
    /// What a server's playlist names are split on for folders (#355), or null for names as they
    /// are. Blank is null.
    /// </summary>
    public void SetServerPlaylistSeparator(long id, string? separator)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE servers SET playlist_separator = $separator WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$separator", string.IsNullOrWhiteSpace(separator) ? DBNull.Value : separator.Trim());
        cmd.ExecuteNonQuery();
    }
}
