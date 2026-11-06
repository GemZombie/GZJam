using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GZJam.Library.Servers;
using Microsoft.Data.Sqlite;

namespace GZJam.Library;

/// <summary>A search of a server's catalog: the songs found, and whether some of the search was left out.</summary>
public sealed record CatalogSearch(IReadOnlyList<ServerSong> Songs, IReadOnlyList<string> LeftOut);

/// <summary>One of a server's playlists, as last read: its name and its songs' IDs there, in order.</summary>
public sealed record ServerPlaylistCopy(string Id, string Name, IReadOnlyList<string> SongIds);

public sealed partial class LibraryDatabase
{
    // ───────────── A server's catalog (#290) ─────────────

    /// <summary>Puts <paramref name="songs"/> in place of the server's catalog, all at once.</summary>
    public void ReplaceServerCatalog(long serverId, IEnumerable<ServerSong> songs, DateTime nowUtc)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        using (var clear = c.CreateCommand())
        {
            clear.CommandText = "DELETE FROM server_catalog WHERE server_id = $server;";
            clear.Parameters.AddWithValue("$server", serverId);
            clear.ExecuteNonQuery();
        }
        using var insert = c.CreateCommand();
        insert.CommandText = """
            INSERT OR REPLACE INTO server_catalog (server_id, song_id, folder, file_name, title, artist, album, album_artist, album_id,
                genre, year, track, disc, bpm, duration_ms, size, path, suffix)
            VALUES ($server, $id, $folder, $file, $title, $artist, $album, $albumArtist, $albumId,
                $genre, $year, $track, $disc, $bpm, $duration, $size, $path, $suffix);
            """;
        string[] names = ["$id", "$folder", "$file", "$title", "$artist", "$album", "$albumArtist", "$albumId",
            "$genre", "$year", "$track", "$disc", "$bpm", "$duration", "$size", "$path", "$suffix"];
        insert.Parameters.AddWithValue("$server", serverId);
        foreach (string name in names)
            insert.Parameters.Add(new SqliteParameter(name, null));
        insert.Prepare();
        static object Db(object? value) => value ?? DBNull.Value;
        foreach (ServerSong song in songs)
        {
            (string folder, string file) = ServerPaths.CatalogPlace(song);
            object?[] values = [song.Id, folder, file, song.Title, song.Artist, song.Album, song.AlbumArtist, song.AlbumId,
                song.Genre, song.Year is > 0 ? song.Year : null, song.Track is > 0 ? song.Track : null, song.Disc, song.Bpm,
                (long)song.Duration.TotalMilliseconds, song.Size, song.Path, song.Suffix];
            for (int i = 0; i < names.Length; i++)
                insert.Parameters[names[i]].Value = Db(values[i]);
            insert.ExecuteNonQuery();
        }
        using (var stamp = c.CreateCommand())
        {
            stamp.CommandText = "UPDATE servers SET catalog_utc = $now WHERE id = $server;";
            stamp.Parameters.AddWithValue("$now", nowUtc.Ticks);
            stamp.Parameters.AddWithValue("$server", serverId);
            stamp.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>One song from the server's catalog, by its ID there; null if it isn't in it.</summary>
    public ServerSong? GetServerCatalogSong(long serverId, string songId) => GetServerCatalogSongs(serverId, [songId]).FirstOrDefault();

    /// <summary>These songs from the server's catalog, in this order; those not in it are left out.</summary>
    public IReadOnlyList<ServerSong> GetServerCatalogSongs(long serverId, IEnumerable<string> songIds)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT song_id, title, artist, album, album_artist, track, disc, year, path, suffix, size, duration_ms, genre, bpm, album_id
            FROM server_catalog WHERE server_id = $server AND song_id = $song;
            """;
        cmd.Parameters.AddWithValue("$server", serverId);
        var song = cmd.Parameters.Add("$song", SqliteType.Text);
        var songs = new List<ServerSong>();
        foreach (string id in songIds)
        {
            song.Value = id;
            using var r = cmd.ExecuteReader();
            if (r.Read())
                songs.Add(ReadCatalogSong(r));
        }
        return songs;
    }

    private static ServerSong ReadCatalogSong(SqliteDataReader r)
    {
        string? Text(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        int? Int(int i) => r.IsDBNull(i) ? null : r.GetInt32(i);
        return new ServerSong(r.GetString(0), Text(1), Text(2), Text(3), Text(4), Int(5), Int(6), Int(7), Text(8), Text(9),
            r.IsDBNull(10) ? null : r.GetInt64(10), TimeSpan.FromMilliseconds(r.GetInt64(11)), Text(12),
            r.IsDBNull(13) ? null : r.GetDouble(13), Text(14));
    }

    // ───────────── Its playlists ─────────────

    /// <summary>The server's playlists, all at once (read with its catalog): those gone from it go here too.</summary>
    public void ReplaceServerPlaylists(long serverId, IEnumerable<ServerPlaylistCopy> playlists)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        using (var clear = c.CreateCommand())
        {
            clear.CommandText = "DELETE FROM server_playlists WHERE server_id = $server;";
            clear.Parameters.AddWithValue("$server", serverId);
            clear.ExecuteNonQuery();
        }
        foreach (ServerPlaylistCopy playlist in playlists)
            SavePlaylist(c, serverId, playlist);
        tx.Commit();
    }

    /// <summary>One playlist as the server has it now (opened in the library).</summary>
    public void SaveServerPlaylist(long serverId, ServerPlaylistCopy playlist)
    {
        using var c = Open();
        SavePlaylist(c, serverId, playlist);
    }

    /// <summary>Between a playlist's song IDs, kept as text.</summary>
    private const char IdSeparator = (char)10;

    private static void SavePlaylist(SqliteConnection c, long serverId, ServerPlaylistCopy playlist)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO server_playlists (server_id, playlist_id, name, song_ids) VALUES ($server, $id, $name, $songs)
            ON CONFLICT (server_id, playlist_id) DO UPDATE SET name = excluded.name, song_ids = excluded.song_ids;
            """;
        cmd.Parameters.AddWithValue("$server", serverId);
        cmd.Parameters.AddWithValue("$id", playlist.Id);
        cmd.Parameters.AddWithValue("$name", playlist.Name);
        cmd.Parameters.AddWithValue("$songs", string.Join(IdSeparator, playlist.SongIds));
        cmd.ExecuteNonQuery();
    }

    /// <summary>The server's playlists as last read, by name.</summary>
    public IReadOnlyList<ServerPlaylistCopy> GetServerPlaylists(long serverId)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT playlist_id, name, song_ids FROM server_playlists WHERE server_id = $server ORDER BY name COLLATE NOCASE;";
        cmd.Parameters.AddWithValue("$server", serverId);
        using var r = cmd.ExecuteReader();
        var playlists = new List<ServerPlaylistCopy>();
        while (r.Read())
            playlists.Add(new ServerPlaylistCopy(r.GetString(0), r.GetString(1),
                r.GetString(2).Split(IdSeparator, StringSplitOptions.RemoveEmptyEntries)));
        return playlists;
    }

    public ServerPlaylistCopy? GetServerPlaylist(long serverId, string playlistId) =>
        GetServerPlaylists(serverId).FirstOrDefault(p => p.Id == playlistId);

    /// <summary>The IDs of an album's songs, from the catalog.</summary>
    public IReadOnlyList<string> GetServerCatalogAlbumSongIds(long serverId, string albumId)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT song_id FROM server_catalog WHERE server_id = $server AND album_id = $album;";
        cmd.Parameters.AddWithValue("$server", serverId);
        cmd.Parameters.AddWithValue("$album", albumId);
        using var r = cmd.ExecuteReader();
        var ids = new List<string>();
        while (r.Read())
            ids.Add(r.GetString(0));
        return ids;
    }

    /// <summary>When the server's catalog was last read in full; null if never.</summary>
    public DateTime? GetServerCatalogTime(long serverId)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT catalog_utc FROM servers WHERE id = $server;";
        cmd.Parameters.AddWithValue("$server", serverId);
        return cmd.ExecuteScalar() is long ticks ? new DateTime(ticks, DateTimeKind.Utc) : null;
    }

    public int GetServerCatalogCount(long serverId)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM server_catalog WHERE server_id = $server;";
        cmd.Parameters.AddWithValue("$server", serverId);
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>The server's folders and how many songs each holds directly, for its tree.</summary>
    public IReadOnlyList<(string Folder, int Count)> GetServerCatalogFolders(long serverId)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT folder, COUNT(*) FROM server_catalog WHERE server_id = $server GROUP BY folder;";
        cmd.Parameters.AddWithValue("$server", serverId);
        using var r = cmd.ExecuteReader();
        var folders = new List<(string, int)>();
        while (r.Read())
            folders.Add((r.GetString(0), r.GetInt32(1)));
        return folders;
    }

    private static readonly Regex CatalogTerm = new("""(-?)(?:([\p{L}_~]+):)?("[^"]*"|\S+)""", RegexOptions.CultureInvariant);

    /// <summary>
    /// The server's songs in <paramref name="folder"/> and below ("": all), matching
    /// <paramref name="text"/>: words (each found in the title, artist, album, genre, folder or
    /// file name), "a phrase", and the filters a server's songs can have: <c>artist:</c>,
    /// <c>album:</c>, <c>genre:</c>, <c>title:</c>, <c>year:1935-1940</c>, <c>bpm:130-150</c>.
    /// The library's own (<c>rating:</c>, <c>label:</c>, <c>played:</c>…) are left out, and named.
    /// In the folders' order, then disc and track.
    /// </summary>
    public CatalogSearch SearchServerCatalog(long serverId, string folder, string? text, CancellationToken cancel = default)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        var where = new StringBuilder("server_id = $server");
        cmd.Parameters.AddWithValue("$server", serverId);
        if (folder.Length > 0)
        {
            where.Append(" AND (folder = $folder OR substr(folder, 1, $length) = $under)");
            cmd.Parameters.AddWithValue("$folder", folder);
            cmd.Parameters.AddWithValue("$under", folder + "/");
            cmd.Parameters.AddWithValue("$length", folder.Length + 1);
        }

        var leftOut = new List<string>();
        int n = 0;
        foreach (Match term in CatalogTerm.Matches(text ?? ""))
        {
            bool not = term.Groups[1].Value == "-";
            string key = term.Groups[2].Value.ToLowerInvariant();
            string value = term.Groups[3].Value.Trim('"');
            if (value.Length == 0 || value == SearchQuery.Or)
                continue;
            string p = "$t" + n++;
            string? condition = key switch
            {
                "" => $"(IFNULL(title,'') || ' ' || IFNULL(artist,'') || ' ' || IFNULL(album,'') || ' ' || IFNULL(album_artist,'') || ' ' || IFNULL(genre,'') || ' ' || folder || ' ' || file_name) LIKE {p}",
                "artist" => $"(IFNULL(artist,'') || ' ' || IFNULL(album_artist,'')) LIKE {p}",
                "album" => $"album LIKE {p}",
                "genre" => $"genre LIKE {p}",
                "title" => $"title LIKE {p}",
                "year" => Range("year", value, p, cmd),
                "bpm" or "~bpm" => Range("bpm", value, p, cmd),
                _ => null,
            };
            if (condition is null)
            {
                leftOut.Add(key + ":");
                continue;
            }
            if (key is "" or "artist" or "album" or "genre" or "title")
                cmd.Parameters.AddWithValue(p, "%" + value.Replace("%", "").Replace("_", "") + "%");
            where.Append(not ? $" AND NOT ({condition})" : $" AND ({condition})");
        }

        cmd.CommandText = $"""
            SELECT song_id, title, artist, album, album_artist, track, disc, year, path, suffix, size, duration_ms, genre, bpm, album_id
            FROM server_catalog WHERE {where}
            ORDER BY folder COLLATE NOCASE, IFNULL(disc, 0), IFNULL(track, 0), file_name COLLATE NOCASE;
            """;
        using var r = cmd.ExecuteReader();
        var songs = new List<ServerSong>();
        while (r.Read())
        {
            cancel.ThrowIfCancellationRequested();
            songs.Add(new ServerSong(r.GetString(0), Text(r, 1), Text(r, 2), Text(r, 3), Text(r, 4), Int(r, 5), Int(r, 6), Int(r, 7),
                Text(r, 8), Text(r, 9), r.IsDBNull(10) ? null : r.GetInt64(10), TimeSpan.FromMilliseconds(r.GetInt64(11)),
                Text(r, 12), r.IsDBNull(13) ? null : r.GetDouble(13), Text(r, 14)));
        }
        return new CatalogSearch(songs, leftOut.Distinct().ToList());

        static string? Text(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
        static int? Int(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt32(i);
    }

    /// <summary>"1935-1940", "130-150", "140" (±4 for a BPM): a range test on <paramref name="column"/>; null if it isn't a number.</summary>
    private static string? Range(string column, string value, string p, SqliteCommand cmd)
    {
        string[] ends = value.Split('-', 2);
        if (!double.TryParse(ends[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double low))
            return null;
        double high = low;
        if (ends.Length == 2 && !double.TryParse(ends[1], NumberStyles.Float, CultureInfo.InvariantCulture, out high))
            return null;
        if (ends.Length == 1 && column == "bpm")
            (low, high) = (low - 4, low + 4);
        cmd.Parameters.AddWithValue(p + "a", Math.Min(low, high));
        cmd.Parameters.AddWithValue(p + "b", Math.Max(low, high));
        return $"{column} BETWEEN {p}a AND {p}b";
    }
}
