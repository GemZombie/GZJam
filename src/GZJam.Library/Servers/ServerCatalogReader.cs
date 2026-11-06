namespace GZJam.Library.Servers;

/// <summary>
/// Reads every song on a server, for its catalog (#290): a search for nothing, a page of 500 at a
/// time, which Navidrome and most Subsonic servers answer with everything. A server that answers it
/// with nothing is read album by album instead (slower: a call for each).
/// </summary>
public static class ServerCatalogReader
{
    public const int PageSize = 500;

    /// <param name="progress">The songs read so far.</param>
    public static async Task<List<ServerSong>> ReadAsync(SubsonicClient client, IProgress<int>? progress = null, CancellationToken cancel = default)
    {
        var songs = new List<ServerSong>();
        var seen = new HashSet<string>();
        for (int offset = 0; ; offset += PageSize)
        {
            var page = (await client.SearchAsync("", songs: PageSize, albums: 0, artists: 0, cancel, offset).ConfigureAwait(false)).Songs;
            foreach (ServerSong song in page)
                if (seen.Add(song.Id))
                    songs.Add(song);
            progress?.Report(songs.Count);
            if (page.Count < PageSize)
                break;
        }
        if (songs.Count > 0)
            return songs;

        // No answer to an empty search: album by album.
        for (int offset = 0; ; offset += PageSize)
        {
            var albums = await client.GetAlbumListAsync("alphabeticalByName", PageSize, offset, cancel).ConfigureAwait(false);
            foreach (ServerAlbum album in albums)
            {
                foreach (ServerSong song in await client.GetAlbumSongsAsync(album.Id, cancel).ConfigureAwait(false))
                    if (seen.Add(song.Id))
                        songs.Add(song);
                progress?.Report(songs.Count);
            }
            if (albums.Count < PageSize)
                break;
        }
        return songs;
    }
}
