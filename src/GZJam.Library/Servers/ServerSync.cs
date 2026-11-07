namespace GZJam.Library.Servers;

/// <summary>How a sync is going: songs looked at so far of all chosen, and what's been downloaded.</summary>
/// <param name="Left">Songs still to come that aren't on the computer (as far as GZJam knows before looking at each).</param>
public sealed record SyncProgress(int Songs, int Done, int Downloaded, int Failed, long Bytes, string? Current, int Left = 0);

/// <summary>What a sync did.</summary>
/// <param name="AlreadyHere">Songs already on the computer: downloaded before, or the DJ's own copy.</param>
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

    /// <summary>Why a song wasn't downloaded: the disk would have less than <see cref="KeepFree"/> left.</summary>
    public const string DiskNearlyFull = "The disk is nearly full.";

    /// <summary>Room the downloads may take in all (every server's), in bytes; null: no limit but the disk.</summary>
    public long? Limit { get; init; }

    /// <summary>Space always left free on the disk.</summary>
    public long KeepFree { get; init; } = 1L << 30;

    /// <summary>Asked before each song: a reason to stop now (the room started hearing music), or null to go on.</summary>
    public Func<string?>? StopBefore { get; init; }

    public Func<DateTime> Now { get; init; } = () => DateTime.UtcNow;

    /// <summary>The fastest songs may come, in bytes a second (keeping a whole server, #285); null: as fast as they come.</summary>
    public long? BytesPerSecond { get; init; }

    /// <summary>
    /// Told of each song as it starts coming down (the Downloads window, #304), with the choice
    /// it's for in a sync (a playlist's name, say); returns what's told how it goes, or null.
    /// </summary>
    public Func<ServerSong, string?, IDownloadReport?>? Report { get; init; }

    /// <summary>
    /// Asked when the next song would pass <see cref="Limit"/> (#286): removes downloads that
    /// nothing kept needs (never those in the set given: what this sync keeps), the least
    /// recently played first, until there's room for the bytes asked; returns the songs removed
    /// and their sizes. Null: the sync stops there.
    /// </summary>
    public Func<long, IReadOnlySet<string>, IReadOnlyList<(string SongId, long Bytes)>>? MakeRoom { get; init; }

    public string ServerFolder => ServerPaths.ServerFolder(downloadsFolder, server.Name);

    public async Task<SyncResult> RunAsync(IProgress<SyncProgress>? progress = null, CancellationToken cancel = default)
    {
        var problems = new List<string>();
        var choices = database.GetServerChoices(server.Id);

        // What's wanted: each choice's songs, in order, and every song once.
        var playlists = new List<(ServerChoice Choice, IReadOnlyList<ServerSong> Songs)>();
        var brought = new List<(ServerChoice Choice, List<string> Songs)>();   // each choice's songs, kept with it
        var wanted = new List<ServerSong>();
        var why = new Dictionary<string, string>();   // song ID → the choice it's for (the first, of several)
        List<string> mine = [];
        void Want(IEnumerable<ServerSong> songs, string reason)
        {
            foreach (ServerSong song in songs)
            {
                mine.Add(song.Id);
                if (why.TryAdd(song.Id, reason))
                    wanted.Add(song);
            }
        }
        foreach (ServerChoice choice in choices)
        {
            mine = [];
            try
            {
                switch (choice.Kind)
                {
                    case ServerChoiceKind.Playlist:
                        var songs = await client.GetPlaylistSongsAsync(choice.RemoteId, cancel).ConfigureAwait(false);
                        playlists.Add((choice, songs));
                        Want(songs, choice.Name);
                        break;
                    case ServerChoiceKind.Starred:
                        Want(await StarredSongsAsync(cancel).ConfigureAwait(false), "what's starred");
                        break;
                    case ServerChoiceKind.Album:
                        Want(await client.GetAlbumSongsAsync(choice.RemoteId, cancel).ConfigureAwait(false), choice.Name);
                        break;
                    case ServerChoiceKind.Artist:
                        foreach (string albumId in await client.GetArtistAlbumsAsync(choice.RemoteId, cancel).ConfigureAwait(false))
                            Want(await client.GetAlbumSongsAsync(albumId, cancel).ConfigureAwait(false), choice.Name);
                        break;
                    case ServerChoiceKind.Folder:
                        // From the catalog (the server has no call for a folder's songs): songs added there come once it's read again.
                        var inFolder = database.SearchServerCatalog(server.Id, choice.RemoteId, null, cancel).Songs;
                        if (inFolder.Count == 0)
                            problems.Add($"{choice.Name}: no songs in it in the server's song list.");
                        Want(inFolder, choice.Name);
                        break;
                    case ServerChoiceKind.Song:
                        if (await client.GetSongAsync(choice.RemoteId, cancel).ConfigureAwait(false) is { } one)
                            Want([one], "songs you chose");
                        else
                            problems.Add($"{choice.Name}: no longer on the server.");
                        break;
                }
                brought.Add((choice, mine));
            }
            catch (ServerException e)
            {
                if (e.Outcome is ServerCheckOutcome.Unreachable or ServerCheckOutcome.WrongSignIn)
                    return new SyncResult(0, 0, 0, 0, 0, e.Message, problems);
                problems.Add($"{choice.Name}: {e.Message}");
            }
        }

        // The songs the DJ removed from the computer stay on the server until they're asked for (#290).
        var removed = database.GetRemovedServerSongs(server.Id);
        wanted.RemoveAll(s => removed.Contains(s.Id));
        var known = database.GetServerSongs(server.Id).ToDictionary(s => s.SongId);
        // Never removed to make room: what the lists bring, and songs downloaded on purpose (a
        // song downloaded on the spot, for the queue say, is wanted only by its own choice).
        var keeping = new HashSet<string>();
        foreach ((ServerChoice choice, List<string> songs) in brought)
            keeping.UnionWith(choice.Kind != ServerChoiceKind.Song ? songs
                : songs.Where(id => !(known.TryGetValue(id, out var k) && k.OnTheSpot)));
        var madeRoom = new HashSet<string>();   // removed to make room: not downloaded again now
        var left = wanted.Where(s => !known.ContainsKey(s.Id)).Select(s => s.Id).ToHashSet();   // to come, as far as is known now
        var local = new Dictionary<string, string>();   // song ID → the file here
        (_, long used) = database.GetDownloadedTotals();
        int downloaded = 0, already = 0, failed = 0, unreachable = 0;
        long bytes = 0;
        string? stopped = null;
        Directory.CreateDirectory(ServerFolder);

        for (int i = 0; i < wanted.Count; i++)
        {
            ServerSong song = wanted[i];
            left.Remove(song.Id);
            progress?.Report(new SyncProgress(wanted.Count, i, downloaded, failed, bytes, song.Title, left.Count));
            if (cancel.IsCancellationRequested)
            {
                stopped = "Stopped.";
                break;
            }

            if (madeRoom.Contains(song.Id))
                continue;
            if (known.TryGetValue(song.Id, out ServerSongCopy? copy) && File.Exists(copy.Path))
            {
                local[song.Id] = copy.Path;
                already++;
                if (!copy.OwnCopy)
                    await AddFolderArtAsync(song, copy.Path, cancel).ConfigureAwait(false);   // one downloaded before there was art
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
            if (Limit is { } limit && used + size > limit && MakeRoom is { } makeRoom)
                foreach ((string id, long freed) in makeRoom(used + size - limit, keeping))
                {
                    madeRoom.Add(id);
                    used -= freed;
                }
            if (Limit is { } full && used + size > full)
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
                long got = await DownloadAsync(song, path, cancel, why.GetValueOrDefault(song.Id)).ConfigureAwait(false);
                database.SaveServerSong(new ServerSongCopy(server.Id, song.Id, path, got, song.Path, OwnCopy: false), Now());
                await AddFolderArtAsync(song, path, cancel).ConfigureAwait(false);
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
            catch (OperationCanceledException)
            {
                // Just this song stopped (the Downloads window): the next sync tries it again.
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
        var stillChosen = database.GetServerChoices(server.Id);
        foreach ((ServerChoice choice, IReadOnlyList<ServerSong> songs) in playlists)
        {
            if (!stillChosen.Any(c => c.Kind == choice.Kind && c.RemoteId == choice.RemoteId))
                continue;   // no longer kept (Stop keeping, while this ran): no copy of it made now
            long playlistId = choice.PlaylistId is { } id && database.GetPlaylist(id) is not null
                ? id
                : database.CreatePlaylist(choice.Name).Id;
            database.SetPlaylistEntries(playlistId, songs.Select(s => local.GetValueOrDefault(s.Id)).OfType<string>());
            database.SetServerChoiceSynced(server.Id, choice, playlistId, Now(), songs.Select(s => s.Id));
        }
        foreach ((ServerChoice choice, List<string> songs) in brought.Where(b => b.Choice.Kind != ServerChoiceKind.Playlist))
            if (stillChosen.Any(c => c.Kind == choice.Kind && c.RemoteId == choice.RemoteId))
                database.SetServerChoiceSynced(server.Id, choice, null, Now(), songs);

        return new SyncResult(wanted.Count, downloaded, already, failed, bytes, stopped, problems);
    }

    /// <summary>
    /// One song onto this computer, now: the copy here if there is one (downloaded before, or the
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
            throw new IOException(DiskNearlyFull);
        string path = ServerPaths.Place(ServerFolder, ServerPaths.RelativeParts(song), p => File.Exists(p) || database.IsServerSongPath(p));
        long got = await DownloadAsync(song, path, cancel).ConfigureAwait(false);
        database.SaveServerSong(new ServerSongCopy(server.Id, song.Id, path, got, song.Path, OwnCopy: false, OnTheSpot: true), Now());
        await AddFolderArtAsync(song, path, cancel).ConfigureAwait(false);
        return path;
    }

    /// <summary>
    /// One song into the DJ's own music (#285, Keep a copy in my library), at <paramref name="path"/>
    /// under <paramref name="keepFolder"/>, never over a file there: by way of a .partial file,
    /// with the album's picture if its folder has none, and the DJ's own from then on (never
    /// removed by GZJam). Returns its size.
    /// </summary>
    public async Task<long> KeepAsync(ServerSong song, string path, string keepFolder, CancellationToken cancel = default)
    {
        if (FreeSpace(keepFolder) is { } free && free - (song.Size ?? 0) < KeepFree)
            throw new IOException(DiskNearlyFull);
        long got = await DownloadAsync(song, path, cancel).ConfigureAwait(false);
        database.SaveServerSong(new ServerSongCopy(server.Id, song.Id, path, got, song.Path, OwnCopy: true), Now());
        await AddFolderArtAsync(song, path, cancel, under: keepFolder).ConfigureAwait(false);
        return got;
    }

    /// <summary>The folders whose picture was looked at in this sync: each album's once.</summary>
    private readonly HashSet<string> _artLooked = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What the album's picture is called when GZJam brings it from the server.</summary>
    public const string CoverName = "cover";

    /// <summary>
    /// The album's picture beside a downloaded song (#290): a server often keeps it as a cover.jpg
    /// in the album's folder, which a song's download doesn't bring. If the song has no picture of
    /// its own and its folder here has none, the server's (getCoverArt) is saved there as
    /// cover.jpg, so the song has its art offline and in any other app. Only in GZJam's downloads
    /// folder, or under <paramref name="under"/> for a song kept in the DJ's music (#285); a
    /// picture that can't be had is no problem.
    /// </summary>
    private async Task AddFolderArtAsync(ServerSong song, string path, CancellationToken cancel, string? under = null)
    {
        if (song.CoverArt is not { Length: > 0 } art || Path.GetDirectoryName(path) is not { } folder || !_artLooked.Add(folder))
            return;
        string top = Path.TrimEndingDirectorySeparator(Path.GetFullPath(under ?? ServerFolder)) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(top, StringComparison.OrdinalIgnoreCase))
            return;
        try
        {
            if (AlbumArtFinder.FolderImage(folder) is not null || AlbumArtFinder.Embedded(path) is not null)
                return;
            if (await client.GetCoverArtAsync(art, cancel: cancel).ConfigureAwait(false) is not { Length: > 0 } picture)
                return;
            string file = Path.Combine(folder, CoverName + ImageExtension(picture));
            string temp = file + PartialExtension;
            await File.WriteAllBytesAsync(temp, picture, cancel).ConfigureAwait(false);
            File.Move(temp, file, overwrite: false);
        }
        catch (Exception e) when (e is ServerException or IOException or UnauthorizedAccessException)
        {
            // No picture this time: the song is here all the same.
        }
    }

    /// <summary>".jpg", ".png" or ".webp", from the picture's first bytes.</summary>
    internal static string ImageExtension(byte[] picture) =>
        picture is [0x89, 0x50, 0x4E, 0x47, ..] ? ".png"
        : picture is [(byte)'R', (byte)'I', (byte)'F', (byte)'F', _, _, _, _, (byte)'W', (byte)'E', (byte)'B', (byte)'P', ..] ? ".webp"
        : ".jpg";

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

    /// <summary>
    /// Downloads one song to <paramref name="path"/> by way of a .partial file; returns its size.
    /// Told to <see cref="Report"/> as it goes; stopped there, just this song throws <see cref="OperationCanceledException"/>.
    /// </summary>
    private async Task<long> DownloadAsync(ServerSong song, string path, CancellationToken cancel, string? why = null)
    {
        IDownloadReport? report = Report?.Invoke(song, why);
        using var stop = report is null ? null : CancellationTokenSource.CreateLinkedTokenSource(cancel, report.Skip);
        CancellationToken token = stop?.Token ?? cancel;
        report?.Started(song.Size);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string partial = path + PartialExtension;
        try
        {
            long length;
            await using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                Stream to = BytesPerSecond is { } limit and > 0 ? new ThrottledStream(file, limit) : file;
                await client.DownloadAsync(song.Id, to, report is null ? null : new Bytes(report), cancel: token).ConfigureAwait(false);
                length = file.Length;
            }
            if (song.Size is { } expected and > 0 && length != expected)
                throw new ServerException($"It arrived incomplete ({length:N0} of {expected:N0} bytes).");
            File.Move(partial, path, overwrite: false);
            report?.Ended(DownloadEnd.Arrived);
            return length;
        }
        catch (Exception e)
        {
            TryDelete(partial);
            report?.Ended(e is OperationCanceledException ? DownloadEnd.Stopped : DownloadEnd.Failed, e.Message);
            throw;
        }
    }

    /// <summary>The bytes so far, straight to the report (not by way of a Progress, which would post each one).</summary>
    private sealed class Bytes(IDownloadReport report) : IProgress<long>
    {
        public void Report(long value) => report.Received(value);
    }

    private long? FreeSpace(string? folder = null)
    {
        try
        {
            return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder ?? downloadsFolder))!).AvailableFreeSpace;
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
