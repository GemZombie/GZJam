using GZJam.Library.Servers;
using Microsoft.Data.Sqlite;

namespace GZJam.Library;

/// <summary>A server song on this computer: the file it became, or the DJ's own copy of it.</summary>
public sealed record ServerSongCopy(long ServerId, string SongId, string Path, long? Size, string? ServerPath, bool OwnCopy);

/// <summary>What a server's songs to keep on the laptop are: a playlist there, or what's starred.</summary>
public enum ServerChoiceKind
{
    Playlist,
    Starred,
}

/// <summary>
/// Something the DJ chose to keep on the laptop from a server (#283): a playlist (by its ID
/// there), or everything starred. A playlist fills a GZJam playlist, <see cref="PlaylistId"/>.
/// </summary>
public sealed record ServerChoice(ServerChoiceKind Kind, string RemoteId, string Name, long? PlaylistId = null, DateTime? SyncedUtc = null)
{
    /// <summary>The choice that means "everything starred".</summary>
    public static ServerChoice Starred { get; } = new(ServerChoiceKind.Starred, "", "Starred");
}

public sealed partial class LibraryDatabase
{
    // ───────────── Server songs on this computer (#283) ─────────────

    public IReadOnlyList<ServerSongCopy> GetServerSongs(long serverId)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT server_id, song_id, path, size, server_path, own_copy FROM server_songs WHERE server_id = $server;";
        cmd.Parameters.AddWithValue("$server", serverId);
        using var r = cmd.ExecuteReader();
        var songs = new List<ServerSongCopy>();
        while (r.Read())
            songs.Add(new ServerSongCopy(r.GetInt64(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetInt64(3),
                r.IsDBNull(4) ? null : r.GetString(4), r.GetInt64(5) != 0));
        return songs;
    }

    /// <summary>Whether any server song is at <paramref name="path"/> (in any case): for not putting two in one place.</summary>
    public bool IsServerSongPath(string path)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM server_songs WHERE path = $path COLLATE NOCASE LIMIT 1;";
        cmd.Parameters.AddWithValue("$path", path);
        return cmd.ExecuteScalar() is not null;
    }

    public void SaveServerSong(ServerSongCopy song, DateTime nowUtc)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO server_songs (server_id, song_id, path, size, server_path, own_copy, downloaded_utc)
            VALUES ($server, $song, $path, $size, $serverPath, $own, $now)
            ON CONFLICT (server_id, song_id) DO UPDATE SET
                path = excluded.path, size = excluded.size, server_path = excluded.server_path,
                own_copy = excluded.own_copy, downloaded_utc = excluded.downloaded_utc;
            """;
        cmd.Parameters.AddWithValue("$server", song.ServerId);
        cmd.Parameters.AddWithValue("$song", song.SongId);
        cmd.Parameters.AddWithValue("$path", song.Path);
        cmd.Parameters.AddWithValue("$size", (object?)song.Size ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$serverPath", (object?)song.ServerPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$own", song.OwnCopy ? 1 : 0);
        cmd.Parameters.AddWithValue("$now", nowUtc.Ticks);
        cmd.ExecuteNonQuery();
    }

    public void ForgetServerSong(long serverId, string songId)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM server_songs WHERE server_id = $server AND song_id = $song;";
        cmd.Parameters.AddWithValue("$server", serverId);
        cmd.Parameters.AddWithValue("$song", songId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>How many songs GZJam downloaded from servers, and their size: the DJ's own copies don't count.</summary>
    public (int Songs, long Bytes) GetDownloadedTotals(long? serverId = null)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*), COALESCE(SUM(size), 0) FROM server_songs WHERE own_copy = 0 AND ($server IS NULL OR server_id = $server);";
        cmd.Parameters.AddWithValue("$server", (object?)serverId ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        r.Read();
        return (r.GetInt32(0), r.GetInt64(1));
    }

    /// <summary>
    /// A song already in the library that is the server's <paramref name="song"/>: the same title,
    /// artist and file size, outside <paramref name="notUnder"/> (the downloads folder). Null if none.
    /// </summary>
    public string? FindOwnCopy(ServerSong song, string notUnder)
    {
        if (song.Size is not { } size || string.IsNullOrWhiteSpace(song.Title))
            return null;
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT path FROM tracks
            WHERE file_size = $size AND title = $title COLLATE NOCASE AND IFNULL(artist, '') = $artist COLLATE NOCASE
              AND substr(path, 1, $length) <> $under COLLATE NOCASE
            LIMIT 1;
            """;
        string under = Path.TrimEndingDirectorySeparator(notUnder) + Path.DirectorySeparatorChar;
        cmd.Parameters.AddWithValue("$size", size);
        cmd.Parameters.AddWithValue("$title", song.Title.Trim());
        cmd.Parameters.AddWithValue("$artist", song.Artist?.Trim() ?? "");
        cmd.Parameters.AddWithValue("$under", under);
        cmd.Parameters.AddWithValue("$length", under.Length);
        return cmd.ExecuteScalar() as string;
    }

    // ───────────── What to keep on the laptop ─────────────

    public IReadOnlyList<ServerChoice> GetServerChoices(long serverId)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT kind, remote_id, name, playlist_id, synced_utc FROM server_choices WHERE server_id = $server
            ORDER BY kind = 'playlist', name COLLATE NOCASE;
            """;
        cmd.Parameters.AddWithValue("$server", serverId);
        using var r = cmd.ExecuteReader();
        var choices = new List<ServerChoice>();
        while (r.Read())
            choices.Add(new ServerChoice(r.GetString(0) == "starred" ? ServerChoiceKind.Starred : ServerChoiceKind.Playlist,
                r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetInt64(3),
                r.IsDBNull(4) ? null : new DateTime(r.GetInt64(4), DateTimeKind.Utc)));
        return choices;
    }

    /// <summary>
    /// What to keep on the laptop from a server, all at once. A choice kept from before keeps its
    /// GZJam playlist and when it was synced; one no longer chosen is forgotten (its playlist and
    /// songs stay).
    /// </summary>
    public void SetServerChoices(long serverId, IEnumerable<ServerChoice> choices)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        var keep = choices.ToList();
        using (var clear = c.CreateCommand())
        {
            clear.CommandText = "CREATE TEMP TABLE IF NOT EXISTS chosen (kind TEXT, remote_id TEXT); DELETE FROM chosen;";
            clear.ExecuteNonQuery();
        }
        foreach (ServerChoice choice in keep)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = """
                INSERT INTO server_choices (server_id, kind, remote_id, name) VALUES ($server, $kind, $remote, $name)
                ON CONFLICT (server_id, kind, remote_id) DO UPDATE SET name = excluded.name;
                INSERT INTO chosen VALUES ($kind, $remote);
                """;
            cmd.Parameters.AddWithValue("$server", serverId);
            cmd.Parameters.AddWithValue("$kind", KindText(choice.Kind));
            cmd.Parameters.AddWithValue("$remote", choice.RemoteId);
            cmd.Parameters.AddWithValue("$name", choice.Name);
            cmd.ExecuteNonQuery();
        }
        using (var prune = c.CreateCommand())
        {
            prune.CommandText = """
                DELETE FROM server_choices WHERE server_id = $server
                  AND NOT EXISTS (SELECT 1 FROM chosen WHERE chosen.kind = server_choices.kind AND chosen.remote_id = server_choices.remote_id);
                DROP TABLE chosen;
                """;
            prune.Parameters.AddWithValue("$server", serverId);
            prune.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>A choice was synced, filling <paramref name="playlistId"/> (for a playlist).</summary>
    public void SetServerChoiceSynced(long serverId, ServerChoice choice, long? playlistId, DateTime nowUtc)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            UPDATE server_choices SET playlist_id = $playlist, synced_utc = $now
            WHERE server_id = $server AND kind = $kind AND remote_id = $remote;
            """;
        cmd.Parameters.AddWithValue("$server", serverId);
        cmd.Parameters.AddWithValue("$kind", KindText(choice.Kind));
        cmd.Parameters.AddWithValue("$remote", choice.RemoteId);
        cmd.Parameters.AddWithValue("$playlist", (object?)playlistId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", nowUtc.Ticks);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Makes a playlist's entries exactly <paramref name="paths"/>, in order: a server playlist refreshed.</summary>
    public void SetPlaylistEntries(long id, IEnumerable<string> paths)
    {
        using var c = Open();
        using var transaction = c.BeginTransaction();
        WriteEntries(c, id, paths.Select(p => (EntryId: 0L, Path: p)).ToList());
        transaction.Commit();
    }

    private static string KindText(ServerChoiceKind kind) => kind == ServerChoiceKind.Starred ? "starred" : "playlist";
}
