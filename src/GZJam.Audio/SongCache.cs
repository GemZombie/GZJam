using System.Runtime.InteropServices;

namespace GZJam.Audio;

/// <summary>
/// A song file read whole into native memory, for a deck or the headphone preview to play from
/// (#346): once it's loaded, nothing it plays touches the disk, so a drive that's slow, asleep,
/// busy with an antivirus scan or unplugged can't stop the music. Native, not a .NET array: the
/// garbage collector never moves or scans it. Counted: freed when the last holder lets go.
/// </summary>
internal sealed class SongMemory
{
    private int _holders = 1;   // whoever made it

    private SongMemory(string path, IntPtr data, long length, long size, DateTime modifiedUtc)
    {
        Path = path;
        Data = data;
        Length = length;
        Size = size;
        ModifiedUtc = modifiedUtc;
    }

    public string Path { get; }
    public IntPtr Data { get; }
    public long Length { get; }

    /// <summary>The file's size and time when it was read: a file changed since is read again.</summary>
    public long Size { get; }

    public DateTime ModifiedUtc { get; }

    /// <summary>Reads a whole file, or null if it can't be read or is too big to hold (it plays from disk then).</summary>
    public static SongMemory? Read(string path, long maxBytes)
    {
        IntPtr data = IntPtr.Zero;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0 || info.Length > maxBytes)
                return null;
            DateTime modified = info.LastWriteTimeUtc;
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 1, FileOptions.SequentialScan);
            long length = file.Length;
            data = Marshal.AllocHGlobal((IntPtr)length);
            unsafe
            {
                var span = new Span<byte>((void*)data, checked((int)length));
                file.ReadExactly(span);
            }
            var memory = new SongMemory(path, data, length, info.Length, modified);
            data = IntPtr.Zero;
            return memory;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OverflowException or OutOfMemoryException
                                       or ArgumentException or NotSupportedException)   // not a file: a server's song, say
        {
            return null;
        }
        finally
        {
            if (data != IntPtr.Zero)
                Marshal.FreeHGlobal(data);
        }
    }

    /// <summary>One more holder: false if it's already been freed.</summary>
    public bool Hold()
    {
        int holders;
        do
        {
            holders = Volatile.Read(ref _holders);
            if (holders == 0)
                return false;
        }
        while (Interlocked.CompareExchange(ref _holders, holders + 1, holders) != holders);
        return true;
    }

    /// <summary>A holder lets go; the last one frees the memory.</summary>
    public void Release()
    {
        if (Interlocked.Decrement(ref _holders) == 0)
            Marshal.FreeHGlobal(Data);
    }

    /// <summary>Whether the file is still the one that was read (same size and time).</summary>
    public bool IsCurrent()
    {
        try
        {
            var info = new FileInfo(Path);
            return info.Exists && info.Length == Size && info.LastWriteTimeUtc == ModifiedUtc;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}

/// <summary>
/// Songs read ahead into memory (#346): the ones likely to be loaded next (the top of the queue,
/// auto-play's next song, a song being dragged) are read on a low-priority thread, so loading
/// one onto a deck doesn't wait for the disk either. A song that wasn't read ahead is read when
/// it's loaded, as before. Only a few are kept besides those playing.
/// </summary>
public sealed class SongCache : IDisposable
{
    /// <summary>
    /// Bigger files play from disk, as before: an hour-long DJ mix, say. A four-minute MP3 is
    /// about 8 MB, a FLAC 30 to 50 MB.
    /// </summary>
    internal const long MaxSongBytes = 300L * 1024 * 1024;

    /// <summary>Songs kept read ahead, besides the ones on the decks and in the headphones.</summary>
    internal const int Kept = 6;

    private readonly Lock _lock = new();
    private readonly LinkedList<SongMemory> _kept = [];   // newest first; the cache holds each once
    private readonly Queue<string> _wanted = new();
    private readonly SemaphoreSlim _available = new(0);
    private readonly CancellationTokenSource _stop = new();
    private Func<string, SongMemory?> _read;

    public SongCache() : this(path => SongMemory.Read(path, MaxSongBytes))
    {
    }

    /// <param name="read">Reads a file into memory (tests count the reads).</param>
    internal SongCache(Func<string, SongMemory?> read)
    {
        _read = read;
        // Below normal: the audio thread and the window always come first.
        new Thread(Work) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Songs read ahead" }.Start();
    }

    /// <summary>Reads a file into memory: tests put a slow one here.</summary>
    internal Func<string, SongMemory?> Reader
    {
        get => _read;
        set => _read = value;
    }

    /// <summary>Reads a file as GZJam does (tests wrap it).</summary>
    internal static SongMemory? ReadFile(string path) => SongMemory.Read(path, MaxSongBytes);

    /// <summary>Reads <paramref name="paths"/> ahead, in the background, unless they're kept already.</summary>
    public void ReadAhead(IEnumerable<string> paths)
    {
        lock (_lock)
        {
            foreach (string path in paths)
            {
                if (_wanted.Contains(path, StringComparer.Ordinal))
                    continue;
                _wanted.Enqueue(path);
                _available.Release();
            }
        }
    }

    /// <summary>
    /// The song in memory for a deck to play, held for it (it calls <see cref="SongMemory.Release"/>
    /// when done): kept from reading ahead, or read now. Null if it can't be held (it plays from disk).
    /// </summary>
    internal SongMemory? Take(string path)
    {
        if (Find(path) is { } kept)
            return kept;
        SongMemory? read = _read(path);
        if (read is null)
            return null;
        read.Hold();   // the deck's
        Keep(read);    // the cache's own
        return read;
    }

    /// <summary>A current copy of <paramref name="path"/>, held for the caller, or null.</summary>
    private SongMemory? Find(string path)
    {
        lock (_lock)
        {
            for (var node = _kept.First; node is not null; node = node.Next)
            {
                if (!string.Equals(node.Value.Path, path, StringComparison.Ordinal))
                    continue;
                SongMemory memory = node.Value;
                _kept.Remove(node);
                if (!memory.IsCurrent())
                {
                    memory.Release();   // changed on disk: read it again
                    return null;
                }
                _kept.AddFirst(memory);
                return memory.Hold() ? memory : null;
            }
        }
        return null;
    }

    /// <summary>
    /// A copy of <paramref name="path"/> already in memory (a deck's song, or one read ahead),
    /// held for the caller, who releases it; null if there's none. Nothing is read (#369).
    /// </summary>
    internal SongMemory? Borrow(string path) => Find(path);

    internal bool IsKept(string path)
    {
        lock (_lock)
            return _kept.Any(m => string.Equals(m.Path, path, StringComparison.Ordinal));
    }

    private void Keep(SongMemory memory)
    {
        lock (_lock)
        {
            // Read ahead and loaded at once: the older copy goes.
            for (var node = _kept.First; node is not null;)
            {
                var next = node.Next;
                if (string.Equals(node.Value.Path, memory.Path, StringComparison.Ordinal))
                {
                    _kept.Remove(node);
                    node.Value.Release();
                }
                node = next;
            }
            _kept.AddFirst(memory);
            while (_kept.Count > Kept)
            {
                SongMemory oldest = _kept.Last!.Value;
                _kept.RemoveLast();
                oldest.Release();   // freed now, or when the deck playing it lets go
            }
        }
    }

    private void Work()
    {
        try
        {
            while (true)
            {
                _available.Wait(_stop.Token);
                string path;
                lock (_lock)
                    path = _wanted.Dequeue();
                if (IsKept(path))
                    continue;
                if (_read(path) is { } memory)
                    Keep(memory);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        lock (_lock)
        {
            foreach (SongMemory memory in _kept)
                memory.Release();
            _kept.Clear();
        }
    }
}
