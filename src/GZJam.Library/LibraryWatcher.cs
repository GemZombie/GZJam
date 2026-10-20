namespace GZJam.Library;

/// <summary>
/// Watches library roots and raises <see cref="RootChanged"/> once a root has been quiet for a
/// moment after changes, so copying in an album triggers one rescan, not hundreds.
/// </summary>
public sealed class LibraryWatcher : IDisposable
{
    private readonly Dictionary<long, (FileSystemWatcher Watcher, Timer Debounce)> _watchers = [];
    private readonly TimeSpan _quietPeriod;
    private readonly Lock _lock = new();

    public LibraryWatcher(TimeSpan? quietPeriod = null)
    {
        _quietPeriod = quietPeriod ?? TimeSpan.FromSeconds(3);
    }

    /// <summary>Raised on a thread-pool thread with the root that changed.</summary>
    public event Action<LibraryRoot>? RootChanged;

    public void Watch(LibraryRoot root)
    {
        lock (_lock)
        {
            if (_watchers.ContainsKey(root.Id) || !Directory.Exists(root.Path))
                return;

            var timer = new Timer(_ => RootChanged?.Invoke(root));
            var watcher = new FileSystemWatcher(root.Path)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
            FileSystemEventHandler onChange = (_, _) => timer.Change(_quietPeriod, Timeout.InfiniteTimeSpan);
            watcher.Created += onChange;
            watcher.Deleted += onChange;
            watcher.Changed += onChange;
            watcher.Renamed += (_, _) => timer.Change(_quietPeriod, Timeout.InfiniteTimeSpan);
            // Buffer overflow means we lost events; a rescan catches everything up.
            watcher.Error += (_, _) => timer.Change(_quietPeriod, Timeout.InfiniteTimeSpan);
            watcher.EnableRaisingEvents = true;
            _watchers[root.Id] = (watcher, timer);
        }
    }

    public void Unwatch(long rootId)
    {
        lock (_lock)
        {
            if (_watchers.Remove(rootId, out var entry))
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
            foreach (var (watcher, timer) in _watchers.Values)
            {
                watcher.Dispose();
                timer.Dispose();
            }
            _watchers.Clear();
        }
    }
}
