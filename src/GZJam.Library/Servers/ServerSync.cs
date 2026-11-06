namespace GZJam.Library.Servers;

/// <summary>How a sync is going: songs looked at so far of all chosen, and what's been downloaded.</summary>
public sealed record SyncProgress(int Songs, int Done, int Downloaded, int Failed, long Bytes, string? Current);

/// <summary>What a sync did.</summary>
/// <param name="AlreadyHere">Songs already on the laptop: downloaded before, or the DJ's own copy.</param>
/// <param name="Stopped">Why it stopped before the end (asked to, out of room, the server gone), or null if it finished.</param>
/// <param name="Problems">A line for each song that couldn't be downloaded, and each choice that couldn't be read.</param>
public sealed record SyncResult(
    int Songs,
    int Downloaded,
    int AlreadyHere,
    int Failed,
    long Bytes,
    string? Stopped,
    IReadOnlyList<string> Problems)
{
    public bool Finished => Stopped is null;
}

/// <summary>
/// Brings what the DJ chose from a server onto this computer (#283): every song of the chosen
/// playlists and of what's starred (starred songs, and the songs of starred albums and artists),
/// and of the albums, artists and songs downloaded while browsing (#284),
/// downloaded into the server's folder under the downloads folder, as the server has it laid out.
/// A song already here (downloaded before, or the DJ's own copy) isn't downloaded again. Each
/// file arrives as <c>.partial</c> and gets its name only once it's whole, so a sync stopped
/// halfway leaves no broken songs. Then each chosen playlist fills a GZJam playlist of its own,
/// in the server's order.
/// </summary>
public sealed class ServerSync(LibraryDatabase database, SubsonicClient client, MusicServer server, string downloadsFolder)
{
    public const string PartialExtension = ".partial";

    /// <summary>Room the downloads may take in all (every server's), in bytes; null: no limit but the disk.</summary>
    public long? Limit { get; init; }

    /// <summary>Space always left free on the disk.</summary>
    public long KeepFree { get; init; } = 1L << 30;

    /// <summary>Asked before each song: a reason to stop now (the room started hearing music), or null to go on.</summary>
    public Func<string?>? StopBefore { get; init; }

    public Func<DateTime> Now { get; init; } = () => DateTime.UtcNow;

    public string ServerFolder => ServerPaths.ServerFolder(downloadsFolder, server.Name);

    public async Task<SyncResult> RunAsync(IProgress<SyncProgress>? progress = null, CancellationToken cancel = default)
    {
        var problems = new List<string>();
        var choices = database.GetServerChoices(server.Id);

        // What's wanted: each choice's songs, in order, and every song once.
        var playlists = new List<(ServerChoice Choice, IReadOnlyList<ServerSong> Songs)>();
        var wanted = new List<ServerSong>();
        var seen = new HashSet<string>();
        void Want(IEnumerable<ServerSong> songs)
        {
            foreach (ServerSong song in songs)
                if (seen.Add(song.Id))
                    wanted.Add(song);
        }
        foreach (ServerChoice choice in choices)
        {
            try
            {
                switch (choice.Kind)
                {
                    case ServerChoiceKind.Playlist:
                        var songs = await client.GetPlaylistSongsAsync(choice.RemoteId, cancel).ConfigureAwait(false);
                        playlists.Add((choice, songs));
                        Want(songs);
                        break;
                    case ServerChoiceKind.Starred:
                        Want(await StarredSongsAsync(cancel).ConfigureAwait(false));
                        break;
                    case ServerChoiceKind.Album:
                        Want(await client.GetAlbumSongsAsync(choice.RemoteId, cancel).ConfigureAwait(false));
                        break;
                    case ServerChoiceKind.Artist:
                        foreach (string albumId in await client.GetArtistAlbumsAsync(choice.RemoteId, cancel).ConfigureAwait(false))
                            Want(await client.GetAlbumSongsAsync(albumId, cancel).ConfigureAwait(false));
                        break;
                    case ServerChoiceKind.Song:
                        if (await client.GetSongAsync(choice.RemoteId, cancel).ConfigureAwait(false) is { } one)
                            Want([one]);
                        else
                            problems.Add($"{choice.Name}: no longer on the server.");
                        break;
                }
            }
            catch (ServerException e)
            {
                if (e.Outcome is ServerCheckOutcome.Unreachable or ServerCheckOutcome.WrongSignIn)
                    return new SyncResult(0, 0, 0, 0, 0, e.Message, problems);
                problems.Add($"{choice.Name}: {e.Message}");
            }
        }

        // The songs the DJ removed from the laptop stay on the server until they're asked for (#290).
        var removed = database.GetRemovedServerSongs(server.Id);
        wanted.RemoveAll(s => removed.Contains(s.Id));
        var known = database.GetServerSongs(server.Id).ToDictionary(s => s.SongId);
        var local = new Dictionary<string, string>();   // song ID → the file here
        (_, long used) = database.GetDownloadedTotals();
        int downloaded = 0, already = 0, failed = 0, unreachable = 0;
        long bytes = 0;
        string? stopped = null;
        Directory.CreateDirectory(ServerFolder);

        for (int i = 0; i < wanted.Count; i++)
        {
            ServerSong song = wanted[i];
            progress?.Report(new SyncProgress(wanted.Count, i, downloaded, failed, bytes, song.Title));
            if (cancel.IsCancellationRequested)
            {
                stopped = "Stopped.";
                break;
            }

            if (known.TryGetValue(song.Id, out ServerSongCopy? copy) && File.Exists(copy.Path))
            {
                local[song.Id] = copy.Path;
                already++;
                continue;
            }
            if (database.FindOwnCopy(song, downloadsFolder) is { } own)
            {
                database.SaveServerSong(new ServerSongCopy(server.Id, song.Id, own, song.Size, song.Path, OwnCopy: true), Now());
                local[song.Id] = own;
                already++;
                continue;
            }

            if (StopBefore?.Invoke() is { } reason)
            {
                stopped = reason;
                break;
            }
            long size = song.Size ?? 0;
            if (Limit is { } limit && used + size > limit)
            {
                stopped = "The room set aside for downloaded songs is full.";
                break;
            }
            if (FreeSpace() is { } free && free - size < KeepFree)
            {
                stopped = "The disk is nearly full.";
                break;
            }

            string path = ServerPaths.Place(ServerFolder, ServerPaths.RelativeParts(song),
                p => File.Exists(p) || database.IsServerSongPath(p));
            try
            {
                long got = await DownloadAsync(song, path, cancel).ConfigureAwait(false);
                database.SaveServerSong(new ServerSongCopy(server.Id, song.Id, path, got, song.Path, OwnCopy: false), Now());
                local[song.Id] = path;
                downloaded++;
                bytes += got;
                used += got;
                unreachable = 0;
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                stopped = "Stopped.";
                break;
            }
            catch (Exception e) when (e is ServerException or IOException or UnauthorizedAccessException)
            {
                failed++;
                problems.Add($"{song.Title ?? song.Id}: {e.Message}");
                if (e is ServerException { Outcome: ServerCheckOutcome.Unreachable } && ++unreachable >= 3)
                {
                    stopped = "The server stopped answering.";
                    break;
                }
            }
        }
        progress?.Report(new SyncProgress(wanted.Count, wanted.Count, downloaded, failed, bytes, null));

        // Each chosen playlist, as a GZJam playlist, in the server's order: the songs here so far.
        foreach ((ServerChoice choice, IReadOnlyList<ServerSong> songs) in playlists)
        {
            long playlistId = choice.PlaylistId is { } id && database.GetPlaylist(id) is not null
                ? id
                : database.CreatePlaylist(choice.Name).Id;
            database.SetPlaylistEntries(playlistId, songs.Select(s => local.GetValueOrDefault(s.Id)).OfType<string>());
            database.SetServerChoiceSynced(server.Id, choice, playlistId, Now());
        }
        foreach (ServerChoice choice in choices.Where(c => c.Kind == ServerChoiceKind.Starred))
            database.SetServerChoiceSynced(server.Id, choice, null, Now());

        return new SyncResult(wanted.Count, downloaded, already, failed, bytes, stopped, problems);
    }

    /// <summary>
    /// One song onto this laptop, now: the copy here if there is one (downloaded before, or the
    /// DJ's own), else downloaded. Returns where it is.
    /// </summary>
    public async Task<string> GetOneAsync(ServerSong song, CancellationToken cancel = default)
    {
        database.ForgetServerSongsRemoved(server.Id, [song.Id]);   // asked for: no longer left on the server
        if (database.GetServerSongs(server.Id).FirstOrDefault(s => s.SongId == song.Id) is { } copy && File.Exists(copy.Path))
            return copy.Path;
        if (database.FindOwnCopy(song, downloadsFolder) is { } own)
        {
            database.SaveServerSong(new ServerSongCopy(server.Id, song.Id, own, song.Size, song.Path, OwnCopy: true), Now());
            return own;
        }
        if (FreeSpace() is { } free && free - (song.Size ?? 0) < KeepFree)
            throw new IOException("The disk is nearly full.");
        string path = ServerPaths.Place(ServerFolder, ServerPaths.RelativeParts(song), p => File.Exists(p) || database.IsServerSongPath(p));
        long got = await DownloadAsync(song, path, cancel).ConfigureAwait(false);
        database.SaveServerSong(new ServerSongCopy(server.Id, song.Id, path, got, song.Path, OwnCopy: false, OnTheSpot: true), Now());
        return path;
    }

    /// <summary>Starred songs, then the songs of starred albums, then those of starred artists' albums.</summary>
    private async Task<List<ServerSong>> StarredSongsAsync(CancellationToken cancel)
    {
        ServerStarred starred = await client.GetStarredAsync(cancel).ConfigureAwait(false);
        var songs = new List<ServerSong>(starred.Songs);
        var albums = starred.Albums.Select(a => a.Id).ToList();
        foreach ((string artistId, _) in starred.Artists)
            albums.AddRange(await client.GetArtistAlbumsAsync(artistId, cancel).ConfigureAwait(false));
        foreach (string albumId in albums.Distinct())
            songs.AddRange(await client.GetAlbumSongsAsync(albumId, cancel).ConfigureAwait(false));
        return songs;
    }

    /// <summary>Downloads one song to <paramref name="path"/> by way of a .partial file; returns its size.</summary>
    private async Task<long> DownloadAsync(ServerSong song, string path, CancellationToken cancel)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string partial = path + PartialExtension;
        try
        {
            long length;
            await using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await client.DownloadAsync(song.Id, file, cancel: cancel).ConfigureAwait(false);
                length = file.Length;
            }
            if (song.Size is { } expected and > 0 && length != expected)
                throw new ServerException($"It arrived incomplete ({length:N0} of {expected:N0} bytes).");
            File.Move(partial, path, overwrite: false);
            return length;
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    private long? FreeSpace()
    {
        try
        {
            return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(downloadsFolder))!).AvailableFreeSpace;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Clears .partial files left by a sync GZJam didn't get to finish (it closed, the laptop slept).</summary>
    public static int ClearLeftovers(string downloadsFolder)
    {
        if (!Directory.Exists(downloadsFolder))
            return 0;
        int cleared = 0;
        foreach (string partial in Directory.EnumerateFiles(downloadsFolder, "*" + PartialExtension, SearchOption.AllDirectories))
            if (TryDelete(partial))
                cleared++;
        return cleared;
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
