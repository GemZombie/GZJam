using GZJam.Library.Servers;

namespace GZJam.Library;

/// <summary>A server song on this computer whose song moved or was renamed on the server: its ID there before, and the song now.</summary>
public sealed record ServerMove(string OldId, ServerSong Song, string Path, bool OwnCopy);

public sealed partial class LibraryDatabase
{
    // ───────────── Songs moved on the server (#286) ─────────────

    /// <summary>
    /// The server's songs on this computer that moved or were renamed on the server, by its song
    /// list just read: one whose path there changed under the same ID, or one whose ID is gone
    /// (Navidrome's IDs come from the path) and exactly one new song matches it: the same file
    /// size, and the same file name or the same title and artist. A song gone from the server with
    /// no match is left as it is.
    /// </summary>
    public IReadOnlyList<ServerMove> FindServerMoves(long serverId)
    {
        var copies = GetServerSongs(serverId);
        if (copies.Count == 0)
            return [];
        var catalog = SearchServerCatalog(serverId, "", null).Songs;
        var byId = catalog.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var linked = copies.Select(c => c.SongId).ToHashSet(StringComparer.Ordinal);
        var newBySize = catalog.Where(s => !linked.Contains(s.Id) && s.Size is > 0).ToLookup(s => s.Size!.Value);
        var gone = copies.Where(c => !byId.ContainsKey(c.SongId)).ToList();
        var tags = gone.Count == 0 ? [] : FindTracks(gone.Select(c => c.Path));
        var moves = new List<ServerMove>();
        var taken = new HashSet<string>(StringComparer.Ordinal);

        foreach (ServerSongCopy copy in copies)
        {
            if (byId.TryGetValue(copy.SongId, out ServerSong? song))
            {
                if (copy.ServerPath is { Length: > 0 } was && song.Path is { Length: > 0 } now && !SamePlace(was, now))
                    moves.Add(new ServerMove(copy.SongId, song, copy.Path, copy.OwnCopy));
                continue;
            }
            if (copy.Size is not > 0 || copy.ServerPath is null && !tags.ContainsKey(copy.Path))
                continue;
            Track? here = tags.GetValueOrDefault(copy.Path);
            string? name = copy.ServerPath is { } path ? FileName(path) : null;
            var matches = newBySize[copy.Size.Value].Where(s => !taken.Contains(s.Id)
                && (name is not null && s.Path is { } p && string.Equals(FileName(p), name, StringComparison.OrdinalIgnoreCase)
                    || here is not null && Same(here.Title, s.Title) && Same(here.Artist, s.Artist))).ToList();
            if (matches.Count != 1)
                continue;
            taken.Add(matches[0].Id);
            moves.Add(new ServerMove(copy.SongId, matches[0], copy.Path, copy.OwnCopy));
        }
        return moves;

        static bool SamePlace(string a, string b) => string.Equals(a.Replace('\\', '/'), b.Replace('\\', '/'), StringComparison.Ordinal);
        static string FileName(string path) => path.Replace('\\', '/').Split('/')[^1];
        static bool Same(string? a, string? b) =>
            !string.IsNullOrWhiteSpace(a) && string.Equals(a.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A server song followed to its new place on the server: its new ID and path there, and its
    /// file here if that moved too (<paramref name="path"/>; null: it stays). What the DJ chose,
    /// removed, or hearted by its ID follows it.
    /// </summary>
    public void FollowServerMove(long serverId, string oldId, ServerSong song, string? path)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            UPDATE server_songs SET song_id = $new, server_path = $serverPath, path = IFNULL($path, path), size = IFNULL($size, size)
            WHERE server_id = $server AND song_id = $old;
            UPDATE OR IGNORE server_choices SET remote_id = $new WHERE server_id = $server AND kind = 'song' AND remote_id = $old;
            UPDATE OR IGNORE server_removed SET song_id = $new WHERE server_id = $server AND song_id = $old;
            UPDATE OR IGNORE server_hearts_out SET song_id = $new WHERE server_id = $server AND song_id = $old;
            """;
        cmd.Parameters.AddWithValue("$server", serverId);
        cmd.Parameters.AddWithValue("$old", oldId);
        cmd.Parameters.AddWithValue("$new", song.Id);
        cmd.Parameters.AddWithValue("$serverPath", (object?)song.Path ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$path", (object?)path ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$size", (object?)song.Size ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        tx.Commit();
    }
}
