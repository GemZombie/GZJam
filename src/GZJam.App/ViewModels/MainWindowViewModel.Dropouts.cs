namespace GZJam.App.ViewModels;

/// <summary>
/// Dropouts in the log (#345): each one with how long the sound stopped, and whether GZJam had
/// just stopped to tidy its memory (a .NET garbage collection), which was the cause at Hot Jam
/// on 2026-11-09 (#343). Long tidying pauses are logged on their own too, heard or not, and
/// closing says how many dropouts there were, so a DJ's report can be checked against the log.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>A tidying pause this long is logged: half of the shortest buffer GZJam uses.</summary>
    private static readonly TimeSpan LongMemoryPause = TimeSpan.FromMilliseconds(100);

    /// <summary>How far back before a dropout is reported its tidying pauses are counted: the gap, the half second before it's reported, and the buffer.</summary>
    private const long PauseLookBackMs = 3000;

    private TimeSpan _memoryPausedSoFar = GC.GetTotalPauseDuration();
    private readonly Queue<(long At, TimeSpan Paused)> _memoryPauses = new();

    /// <summary>Dropouts since GZJam started.</summary>
    internal int DropoutCount { get; private set; }

    /// <summary>Each tick: how long GZJam stopped to tidy its memory since the last.</summary>
    private void WatchMemoryPauses()
    {
        TimeSpan total = GC.GetTotalPauseDuration();
        TimeSpan paused = total - _memoryPausedSoFar;
        _memoryPausedSoFar = total;
        long now = Environment.TickCount64;
        if (paused > TimeSpan.Zero)
            _memoryPauses.Enqueue((now, paused));
        while (_memoryPauses.Count > 0 && now - _memoryPauses.Peek().At > PauseLookBackMs)
            _memoryPauses.Dequeue();
        if (paused >= LongMemoryPause)
            Log.Info($"GZJam stopped for {paused.TotalMilliseconds:0} ms to tidy its memory ({GC.GetGCMemoryInfo().HeapSizeBytes / (1024 * 1024):N0} MB in use)");
    }

    private void OnMainDropout(TimeSpan gap)
    {
        DropoutCount++;
        // The tick that reports it counted its pauses first (Tick), so the queue is up to date.
        TimeSpan tidying = TimeSpan.Zero;
        foreach (var (_, paused) in _memoryPauses)
            tidying += paused;
        string cause = tidying.TotalMilliseconds >= 50
            ? $"; GZJam had stopped for {tidying.TotalMilliseconds:0} ms to tidy its memory just before"
            : "";
        string hint = _settings.ExtraBuffering ? "" : "; Settings › Sound › Extra protection against crackles may help";
        Log.Info($"Speakers: the sound stopped for {gap.TotalMilliseconds:0} ms (a dropout){cause}{hint}");
    }

    /// <summary>For the log as GZJam closes.</summary>
    public void LogDropouts() => Log.Info(DropoutCount switch
    {
        0 => "Speakers: no dropouts while GZJam ran",
        1 => "Speakers: 1 dropout while GZJam ran",
        _ => $"Speakers: {DropoutCount} dropouts while GZJam ran",
    });
}
