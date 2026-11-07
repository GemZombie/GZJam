namespace GZJam.Library;

/// <summary>
/// Watches library roots and raises <see cref="RootChanged"/> once a root has been quiet for a
/// moment after changes, so copying in an album triggers one rescan, not hundreds. While changes
/// keep coming, as songs arriving one at a time in a sync, a root is scanned at most once every
/// <c>minInterval</c>: a song every few seconds over the internet made a scan, a tree refresh and
/// a song list reload each time. A song being downloaded (<c>.partial</c>, see
/// <see cref="Servers.ServerSync.PartialExtension"/>) is no change until it gets its name.
/// </summary>
public sealed class LibraryWatcher : IDisposable
{
    private sealed class Watched(FileSystemWatcher watcher, Timer debounce)
    {
        public FileSystemWatcher Watcher { get; } = watcher;
        public Timer Debounce { get; } = debounce;
        public DateTime LastRaisedUtc { get; set; } = DateTime.MinValue;
    }

    private readonly Dictionary<long, Watched> _watchers = [];
    private readonly TimeSpan _quietPeriod;
    private readonly TimeSpan _minInterval;
    private readonly Lock _lock = new();

    public LibraryWatcher(TimeSpan? quietPeriod = null, TimeSpan? minInterval = null)
    {
        _quietPeriod = quietPeriod ?? TimeSpan.FromSeconds(3);
        _minInterval = minInterval ?? TimeSpan.FromSeconds(15);
    }

    /// <summary>Raised on a thread-pool thread with the root that changed.</summary>
    public event Action<LibraryRoot>? RootChanged;

    public void Watch(LibraryRoot root)
    {
        lock (_lock)
        {
            if (_watchers.ContainsKey(root.Id) || !Directory.Exists(root.Path))
                return;

            Watched? watched = null;
            var timer = new Timer(_ => Quiet(root, watched!));
            var watcher = new FileSystemWatcher(root.Path)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
            void Wait() => timer.Change(_quietPeriod, Timeout.InfiniteTimeSpan);
            watcher.Created += (_, e) => { if (!IsPartial(e.FullPath)) Wait(); };
            watcher.Deleted += (_, e) => { if (!IsPartial(e.FullPath)) Wait(); };
            // A folder's own "changed" only says something in it changed: that file says so itself.
            watcher.Changed += (_, e) => { if (!IsPartial(e.FullPath) && !Directory.Exists(e.FullPath)) Wait(); };
            watcher.Renamed += (_, e) => { if (!IsPartial(e.FullPath)) Wait(); };
            // Buffer overflow means we lost events; a rescan catches everything up.
            watcher.Error += (_, _) => Wait();
            watched = new Watched(watcher, timer);
            watcher.EnableRaisingEvents = true;
            _watchers[root.Id] = watched;
        }
    }

    private static bool IsPartial(string path) => path.EndsWith(Servers.ServerSync.PartialExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>The root has been quiet: raised now, or once <c>minInterval</c> has passed since the last time.</summary>
    private void Quiet(LibraryRoot root, Watched watched)
    {
        lock (_lock)
        {
            if (!_watchers.TryGetValue(root.Id, out Watched? current) || current != watched)
                return;   // no longer watched
            TimeSpan since = DateTime.UtcNow - watched.LastRaisedUtc;
            if (since < _minInterval)
            {
                watched.Debounce.Change(_minInterval - since, Timeout.InfiniteTimeSpan);
                return;
            }
            watched.LastRaisedUtc = DateTime.UtcNow;
        }
        RootChanged?.Invoke(root);
    }

    public void Unwatch(long rootId)
    {
        lock (_lock)
        {
            if (_watchers.Remove(rootId, out Watched? entry))
            {
                entry.Watcher.Dispose();
                entry.Debounce.Dispose();
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (Watched entry in _watchers.Values)
            {
                entry.Watcher.Dispose();
                entry.Debounce.Dispose();
            }
            _watchers.Clear();
        }
    }
}
