using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using GZJam.Audio;
using GZJam.Library;

namespace GZJam.App;

/// <summary>
/// Analyzes tracks (waveform, where the music starts and ends, BPM) on low-priority background
/// threads and saves the results, so each file is only ever analyzed once. A track loaded onto a
/// deck jumps the line, then queued tracks, then the rest of the library in folder order.
/// </summary>
public sealed partial class TrackAnalysisService : ObservableObject, IDisposable
{
    private enum Priority { Deck, Queue, Library }

    private sealed class Job(string path, FileVersion version, Priority priority, bool redo = false)
    {
        public string Path { get; } = path;

        /// <summary>Analyze again even if there's a saved result. Never merged with other requests.</summary>
        public bool Redo { get; } = redo;
        public string Key { get; } = redo ? $"{path}|redo {Guid.NewGuid()}" : path;
        public FileVersion Version { get; } = version;
        public Priority Priority { get; set; } = priority;
        public TaskCompletionSource<TrackAnalysis?> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly LibraryDatabase _database;
    private readonly TrackAnalyzer _analyzer;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Job> _jobs = new(StringComparer.Ordinal);   // waiting or running
    private readonly LinkedList<Job>[] _waiting = [new(), new(), new()];
    private readonly SemaphoreSlim _available = new(0);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Thread> _workers = [];
    private int _libraryTotal;
    private int _libraryDone;
    private bool _libraryUpdate;   // the library's being analyzed again, for a new analyzer
    private long _libraryStarted;  // when this run through the library began, for the time left

    public TrackAnalysisService(LibraryDatabase database, AudioEngine audio, int? threads = null)
    {
        _database = database;
        _analyzer = new TrackAnalyzer(audio);
        int count = threads ?? Math.Clamp(Environment.ProcessorCount / 2, 1, 3);
        for (int i = 0; i < count; i++)
        {
            // Below normal: the audio thread and the UI always come first.
            var worker = new Thread(Work) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = $"Track analysis {i + 1}" };
            _workers.Add(worker);
            worker.Start();
        }
    }

    /// <summary>Raised on the UI thread when a track has been analyzed (path, result).</summary>
    public event Action<string, TrackAnalysis>? Analyzed;

    /// <summary>
    /// Progress through the library, e.g. "First-time analysis… 1,234 / 12,686", then "First-time
    /// analysis… 1 h 40 min left"; null when idle.
    /// </summary>
    [ObservableProperty]
    public partial string? Status { get; private set; }

    /// <summary>Why the analysis is happening and that it's done once, for the status's tooltip.</summary>
    [ObservableProperty]
    public partial string? StatusTip { get; private set; }

    [ObservableProperty]
    public partial double Percent { get; private set; }

    /// <summary>
    /// The analysis of a file: from the database if it's been done, otherwise analyzed ahead of
    /// everything else. Null if the file can't be read.
    /// </summary>
    public Task<TrackAnalysis?> GetAsync(string path) => Request(path, Priority.Deck);

    /// <summary>
    /// Analyzes a file again, ahead of everything else, ignoring the saved result: e.g. the DJ
    /// has just chosen a BPM range for it. Null if it can't be read, or if the range changed
    /// again before this finished (a later request has the answer).
    /// </summary>
    public Task<TrackAnalysis?> RedoAsync(string path) => Task.Run(() =>
    {
        if (FileVersion.Of(path) is not { } version)
            return Task.FromResult<TrackAnalysis?>(null);
        var job = new Job(path, version, Priority.Deck, redo: true);
        lock (_lock)
            Enqueue(job);
        return job.Done.Task;
    });

    /// <summary>
    /// A close-up waveform of a file, for a deck's close-up (worked out now, not stored). Null if
    /// it can't be read or <paramref name="cancellationToken"/> fires first (another song loaded).
    /// </summary>
    public Task<Waveform?> DetailAsync(string path, CancellationToken cancellationToken) => Task.Run(() =>
    {
        try
        {
            return _analyzer.Detail(path, cancellationToken);
        }
        catch (Exception ex) when (ex is AudioException or OperationCanceledException)
        {
            return null;
        }
    }, CancellationToken.None);

    /// <summary>Moves tracks ahead of the library (e.g. they've just been queued).</summary>
    public void Prioritize(IEnumerable<Track> tracks)
    {
        foreach (Track track in tracks)
            _ = Request(track.Path, Priority.Queue);
    }

    /// <summary>Queues every library track that hasn't been analyzed yet (call after a scan).</summary>
    public void QueueLibrary()
    {
        Task.Run(() =>
        {
            var pending = _database.GetPendingAnalysis(TrackAnalyzer.Version);
            int added = 0;
            lock (_lock)
            {
                if (_libraryDone >= _libraryTotal)
                {
                    // Idle: a fresh run through the library, counted from zero.
                    (_libraryDone, _libraryTotal, _libraryUpdate) = (0, 0, false);
                    _libraryStarted = TimeProvider.System.GetTimestamp();
                }
                foreach (PendingAnalysis p in pending)
                {
                    if (_jobs.ContainsKey(p.Path))
                        continue;
                    Enqueue(new Job(p.Path, p.Version, Priority.Library));
                    _libraryUpdate |= p.Update;
                    added++;
                }
                if (added > 0)
                    _libraryTotal = _libraryDone + _jobs.Values.Count(j => j.Priority == Priority.Library);
            }
            if (added > 0)
                Log.Info($"Track analysis: {added:N0} library tracks to analyze");
            UpdateStatus();
        });
    }

    private Task<TrackAnalysis?> Request(string path, Priority priority)
    {
        lock (_lock)
        {
            if (_jobs.TryGetValue(path, out Job? existing))
            {
                // Already waiting: move it up if this request is more urgent.
                if (priority < existing.Priority && _waiting[(int)existing.Priority].Remove(existing))
                {
                    if (existing.Priority == Priority.Library)
                        _libraryTotal--;
                    existing.Priority = priority;
                    _waiting[(int)priority].AddLast(existing);
                }
                return existing.Done.Task;
            }
        }

        // Saved already? Most requests end here, so don't wait behind the workers.
        return Task.Run(() =>
        {
            if (FileVersion.Of(path) is not { } version)
                return Task.FromResult<TrackAnalysis?>(null);
            if (Load(path, version) is { } saved)
                return Task.FromResult<TrackAnalysis?>(saved);
            lock (_lock)
            {
                if (_jobs.TryGetValue(path, out Job? raced))
                    return raced.Done.Task;
                var job = new Job(path, version, priority);
                Enqueue(job);
                return job.Done.Task;
            }
        });
    }

    /// <summary>Adds a job (caller holds the lock).</summary>
    private void Enqueue(Job job)
    {
        _jobs[job.Key] = job;
        _waiting[(int)job.Priority].AddLast(job);
        _available.Release();
    }

    private TrackAnalysis? Load(string path, FileVersion version)
    {
        if (_database.GetAnalysis(path, version) is not { } stored || stored.Version < TrackAnalyzer.Version ||
            Waveform.FromBytes(stored.Waveform) is not { } waveform)
            return null;
        return new TrackAnalysis(stored.Length, stored.AudibleStart, stored.AudibleEnd, stored.Bpm, waveform)
        {
            Lufs = stored.Lufs,
            PeakDb = stored.PeakDb,
        };
    }

    private void Work()
    {
        while (true)
        {
            try
            {
                _available.Wait(_stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            Job? job;
            lock (_lock)
            {
                job = _waiting.Select(list => list.First?.Value).FirstOrDefault(j => j is not null);
                if (job is null)
                    continue;   // its slot was taken by a promotion; the count still matches
                _waiting[(int)job.Priority].RemoveFirst();
            }

            TrackAnalysis? result = Analyze(job);
            lock (_lock)
            {
                _jobs.Remove(job.Key);
                if (job.Priority == Priority.Library)
                    _libraryDone++;
            }
            job.Done.TrySetResult(result);
            if (result is not null)
                Dispatcher.UIThread.Post(() => Analyzed?.Invoke(job.Path, result));
            UpdateStatus();
            if (_stop.IsCancellationRequested)
                return;
        }
    }

    private TrackAnalysis? Analyze(Job job)
    {
        try
        {
            // A queued library track may have been analyzed meanwhile (e.g. loaded onto a deck).
            if (!job.Redo && Load(job.Path, job.Version) is { } saved)
                return saved;
            BpmRange? range = _database.GetBpmRange(job.Path);
            TrackAnalysis result = _analyzer.Analyze(job.Path, bpmRange: range is { } r ? (r.Min, r.Max) : null, cancellationToken: _stop.Token);
            if (_database.GetBpmRange(job.Path) != range)
                return null;   // the DJ changed the range meanwhile; that request will redo it
            _database.SaveAnalysis(job.Path, job.Version, new StoredAnalysis(
                TrackAnalyzer.Version, result.Length, result.AudibleStart, result.AudibleEnd, result.Bpm, result.Waveform.ToBytes())
            {
                Lufs = result.Lufs,
                PeakDb = result.PeakDb,
            });
            return result;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex) when (ex is AudioException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            Log.Error($"Couldn't analyze {job.Path}", ex);
            return null;
        }
    }

    private void UpdateStatus()
    {
        int done, total;
        bool update;
        TimeSpan elapsed;
        lock (_lock)
            (done, total, update, elapsed) = (_libraryDone, _libraryTotal, _libraryUpdate, TimeProvider.System.GetElapsedTime(_libraryStarted));
        Dispatcher.UIThread.Post(() =>
        {
            bool busy = done < total;
            Status = busy ? StatusText(done, total, update, elapsed) : null;
            StatusTip = busy ? $"{Format.Count(done)} of {Format.Count(total)} songs done. {(update ? UpdateTip : NewSongsTip)}" : null;
            Percent = busy ? 100.0 * done / total : 0;
        });
    }

    private const string NewSongsTip =
        "GZJam listens to each song once, start to finish, for its waveform, where the music starts and ends, " +
        "its BPM and its loudness, and keeps what it finds. After this, only songs new to your music folders " +
        "(or changed) are analyzed. Use GZJam as normal meanwhile: a song you load or queue goes first.";

    private const string UpdateTip =
        "This version of GZJam analyzes songs better, so it listens to each one again, once: its waveform, " +
        "where the music starts and ends, its BPM and its loudness. Use GZJam as normal meanwhile: a song you " +
        "load or queue goes first.";

    /// <summary>
    /// The top bar's progress: says the work is done once (or once again, for a new analyzer), and
    /// once enough songs are done to tell, roughly how long the rest will take instead of the count
    /// (the tooltip keeps it). Short, as the old "Analyzing tracks… n / total" was: the top bar has
    /// no room to spare on a 1024-wide window.
    /// </summary>
    internal static string StatusText(int done, int total, bool update, TimeSpan elapsed)
    {
        string text = update ? "Updating analysis… " : "First-time analysis… ";
        // Too early, the rate is mostly the first songs' luck (or a deck's song jumping the line).
        return done >= 25 && elapsed >= TimeSpan.FromMinutes(1)
            ? text + TimeLeft(elapsed * ((double)(total - done) / done))
            : text + $"{Format.Count(done)} / {Format.Count(total)}";
    }

    /// <summary>A rough time left: to the minute under an hour, then to ten minutes.</summary>
    internal static string TimeLeft(TimeSpan left)
    {
        if (left < TimeSpan.FromMinutes(1))
            return "under a minute left";
        if (left < TimeSpan.FromMinutes(59.5))
            return $"{Math.Round(left.TotalMinutes):0} min left";
        int tens = (int)Math.Round(left.TotalMinutes / 10);   // whole ten minutes
        (int hours, int minutes) = (tens / 6, tens % 6 * 10);
        return minutes == 0 ? $"{hours} h left" : $"{hours} h {minutes} min left";
    }

    public void Dispose()
    {
        _stop.Cancel();
        foreach (Thread worker in _workers)
            worker.Join(TimeSpan.FromSeconds(5));   // they must stop decoding before BASS is freed
        lock (_lock)
        {
            foreach (Job job in _jobs.Values)
                job.Done.TrySetResult(null);
        }
    }
}
