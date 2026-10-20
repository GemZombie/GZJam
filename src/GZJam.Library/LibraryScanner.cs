using System.Collections.Concurrent;

namespace GZJam.Library;

/// <summary>Progress of a scan, reported as it runs.</summary>
/// <param name="FilesFound">Audio files found on disk so far.</param>
/// <param name="FilesToRead">New or changed files that need their tags read.</param>
/// <param name="FilesRead">Of those, how many are done.</param>
public sealed record ScanProgress(string RootPath, int FilesFound, int FilesToRead, int FilesRead, bool Enumerating);

public sealed record ScanResult(int Added, int Updated, int Removed, int Unchanged, TimeSpan Elapsed);

/// <summary>
/// Brings the index in line with the folders on disk. Incremental: only new or changed files
/// (by size and modified time) have their tags read, so rescans of a large library are quick.
/// </summary>
public sealed class LibraryScanner(LibraryDatabase database)
{
    /// <summary>Formats BASS plays without add-ons.</summary>
    public static readonly IReadOnlySet<string> AudioExtensions =
        new HashSet<string>([".mp3", ".mp2", ".ogg", ".wav", ".aif", ".aiff"], StringComparer.OrdinalIgnoreCase);

    private const int BatchSize = 250;

    /// <summary>
    /// True for playable audio. Skips macOS "._name.mp3" metadata files, which carry an audio
    /// extension but contain no audio (common on libraries that have lived on a Mac).
    /// </summary>
    public static bool IsAudioFile(string path) =>
        AudioExtensions.Contains(Path.GetExtension(path)) && !Path.GetFileName(path).StartsWith("._", StringComparison.Ordinal);

    public async Task<ScanResult> ScanAsync(LibraryRoot root, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => Scan(root, progress, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public ScanResult Scan(LibraryRoot root, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        var known = database.GetFileStamps(root.Id);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var toRead = new List<FileInfo>();
        int added = 0;

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
                    if (stamp.Size == file.Length && stamp.ModifiedTicks == file.LastWriteTimeUtc.Ticks)
                        continue;
                }
                else
                {
                    added++;
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

        var removed = known.Where(k => !seen.Contains(k.Key)).Select(k => k.Value.Id).ToList();
        if (removed.Count > 0)
            database.DeleteTracks(removed);

        return new ScanResult(added, toRead.Count - added, removed.Count, seen.Count - toRead.Count, started.Elapsed);
    }
}
