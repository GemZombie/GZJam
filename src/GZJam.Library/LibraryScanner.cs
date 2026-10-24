using System.Collections.Concurrent;

namespace GZJam.Library;

/// <summary>Progress of a scan, reported as it runs.</summary>
/// <param name="FilesFound">Audio files found on disk so far.</param>
/// <param name="FilesToRead">New or changed files that need their tags read.</param>
/// <param name="FilesRead">Of those, how many are done.</param>
public sealed record ScanProgress(string RootPath, int FilesFound, int FilesToRead, int FilesRead, bool Enumerating);

/// <param name="Moved">
/// Songs renamed or moved within the folder: counted here, not as added and removed. Their marks,
/// labels, cues, playlists and plays went with them.
/// </param>
public sealed record ScanResult(int Added, int Updated, int Removed, int Unchanged, TimeSpan Elapsed, int Moved = 0)
{
    public bool ChangedAnything => Added + Updated + Removed + Moved > 0;
}

/// <summary>
/// Brings the index in line with the folders on disk. Incremental: only new or changed files
/// (by size and modified time) have their tags read, so rescans of a large library are quick.
/// Songs read before GZJam got better at reading tags (<see cref="TagReader.Version"/>) are read
/// again too. A song renamed or moved within the folder keeps what the DJ gave it (see
/// <see cref="FindMoves"/>).
/// </summary>
public sealed class LibraryScanner(LibraryDatabase database)
{
    /// <summary>
    /// What GZJam plays: BASS's own formats (MP3, MP2, OGG Vorbis, WAV, AIFF), then FLAC, Opus and
    /// .m4a/.aac (AAC or Apple Lossless) from add-ons and the computer's decoders. Linux can't play
    /// AAC; those songs are listed anyway, and say why when loaded.
    /// </summary>
    public static readonly IReadOnlySet<string> AudioExtensions =
        new HashSet<string>([".mp3", ".mp2", ".ogg", ".wav", ".aif", ".aiff", ".flac", ".opus", ".m4a", ".aac"], StringComparer.OrdinalIgnoreCase);

    private const int BatchSize = 250;

    /// <summary>
    /// True for playable audio. Skips macOS "._name.mp3" metadata files, which carry an audio
    /// extension but contain no audio (common on libraries that have lived on a Mac).
    /// </summary>
    public static bool IsAudioFile(string path) =>
        AudioExtensions.Contains(Path.GetExtension(path)) && !Path.GetFileName(path).StartsWith("._", StringComparison.Ordinal);

    /// <param name="readAllTags">
    /// Read every song's tags again, changed or not: for tags edited by a program that keeps the
    /// file's date (Mp3tag can), which a normal scan wouldn't notice.
    /// </param>
    public async Task<ScanResult> ScanAsync(LibraryRoot root, IProgress<ScanProgress>? progress = null, bool readAllTags = false,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => Scan(root, progress, readAllTags, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="ScanAsync"/>
    public ScanResult Scan(LibraryRoot root, IProgress<ScanProgress>? progress = null, bool readAllTags = false,
        CancellationToken cancellationToken = default)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        var known = database.GetFileStamps(root.Id);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var toRead = new List<FileInfo>();
        var added = new Dictionary<string, (long Size, long ModifiedTicks)>(StringComparer.Ordinal);

        if (Directory.Exists(root.Path))
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            };
            foreach (string path in Directory.EnumerateFiles(root.Path, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsAudioFile(path))
                    continue;

                seen.Add(path);
                var file = new FileInfo(path);
                if (known.TryGetValue(path, out var stamp))
                {
                    if (!readAllTags && stamp.TagVersion >= TagReader.Version
                        && stamp.Size == file.Length && stamp.ModifiedTicks == file.LastWriteTimeUtc.Ticks)
                        continue;
                }
                else
                {
                    added[path] = (file.Length, file.LastWriteTimeUtc.Ticks);
                }
                toRead.Add(file);

                if (seen.Count % 500 == 0)
                    progress?.Report(new ScanProgress(root.Path, seen.Count, toRead.Count, 0, Enumerating: true));
            }
        }
        // A missing root (unplugged drive) is treated as empty only if it really is gone; we
        // don't want to wipe a library because a USB drive is disconnected.
        else
        {
            return new ScanResult(0, 0, 0, known.Count, started.Elapsed);
        }

        progress?.Report(new ScanProgress(root.Path, seen.Count, toRead.Count, 0, Enumerating: false));

        // Read tags in parallel (I/O bound), write each batch in one transaction so the library
        // fills in progressively during a first import.
        int read = 0;
        foreach (FileInfo[] batch in toRead.Chunk(BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tracks = new ConcurrentBag<Track>();
            Parallel.ForEach(batch,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8), CancellationToken = cancellationToken },
                file => tracks.Add(TagReader.Read(root.Id, root.Path, file)));
            database.UpsertTracks(tracks);
            read += batch.Length;
            progress?.Report(new ScanProgress(root.Path, seen.Count, toRead.Count, read, Enumerating: false));
        }

        var missing = known.Where(k => !seen.Contains(k.Key)).ToDictionary(k => k.Key, k => k.Value, StringComparer.Ordinal);
        var moves = FindMoves(missing, added);
        if (missing.Count > 0)
            database.RemoveTracks(missing.Values.Select(m => m.Id), moves);

        return new ScanResult(added.Count - moves.Count, toRead.Count - added.Count, missing.Count - moves.Count,
            seen.Count - toRead.Count, started.Elapsed, moves.Count);
    }

    /// <summary>
    /// Songs gone from one path and found at another: a rename or move keeps a file's size and
    /// modified time, which together almost never match another file's. Where several files share
    /// both (copies of one file), the one with the same file name is the match (its folder was
    /// renamed); any still undecided are left as removed and added, as before.
    /// </summary>
    internal static IReadOnlyList<(string From, string To)> FindMoves(
        IReadOnlyDictionary<string, LibraryDatabase.FileStamp> missing, IReadOnlyDictionary<string, (long Size, long ModifiedTicks)> added)
    {
        var moves = new List<(string From, string To)>();
        if (missing.Count == 0 || added.Count == 0)
            return moves;

        var gone = missing.ToLookup(m => (m.Value.Size, m.Value.ModifiedTicks), m => m.Key);
        foreach (var arrived in added.GroupBy(a => a.Value, a => a.Key))
        {
            var from = gone[arrived.Key].ToList();
            var to = arrived.ToList();
            if (from.Count == 0)
                continue;
            if (from.Count == 1 && to.Count == 1)
            {
                moves.Add((from[0], to[0]));
                continue;
            }
            foreach (string path in to)
            {
                var sameName = from.Where(f => string.Equals(Path.GetFileName(f), Path.GetFileName(path), StringComparison.OrdinalIgnoreCase)).ToList();
                bool unique = sameName.Count == 1
                    && to.Count(t => string.Equals(Path.GetFileName(t), Path.GetFileName(path), StringComparison.OrdinalIgnoreCase)) == 1;
                if (unique)
                    moves.Add((sameName[0], path));
            }
        }
        return moves;
    }
}
