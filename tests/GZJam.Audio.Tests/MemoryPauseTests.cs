using System.Diagnostics;
using GZJam.Testing;

namespace GZJam.Audio.Tests;

/// <summary>
/// .NET stopping GZJam to tidy its memory (a garbage collection) doesn't stop the music (#344).
/// At Hot Jam on 2026-11-09 it did (#343): BASS's audio thread called .NET code, the output's
/// limiter and the deck's meter, and had to wait for each long collection. They're native code
/// now. Here a deck plays on this computer's real speakers, shared, while collections far longer
/// than the speakers' buffer run, and no dropout may be found. A tone plays at volume 0: nothing
/// is heard. Skipped where there's no sound device, as on CI machines.
/// </summary>
[Collection("BASS")]
public sealed class MemoryPauseTests : IDisposable
{
    private readonly AudioEngine _engine = new();
    private readonly MixEngine _mix;
    private readonly string _dir = Directory.CreateTempSubdirectory("gzjam-pauses-").FullName;

    public MemoryPauseTests() => _mix = new MixEngine(_engine);

    public void Dispose()
    {
        _mix.Dispose();
        _engine.Dispose();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>A heap with millions of objects pointing at each other: slow to tidy.</summary>
    private sealed class Node(Node? next)
    {
        public Node? Next = next;
    }

    [Fact]
    public void Long_memory_pauses_dont_stop_the_speakers()
    {
        AudioDevice? device = _engine.GetOutputDevices().FirstOrDefault(d => !d.FollowsSystemDefault);
        Assert.SkipWhen(device is null, "Needs a sound device.");
        _mix.SetOutputs(device, null);
        _mix.Main.Volume = 0;   // the audio still runs through every stage
        var gaps = new List<TimeSpan>();
        _mix.MainDropout += (_, gap) => gaps.Add(gap);

        Deck deck = _mix.Decks[0];
        deck.Load(TestWav.WriteTone(Path.Combine(_dir, "tone.wav"), TimeSpan.FromSeconds(30)));
        deck.SetJumps([(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(1), true)]);
        deck.Play();

        var clock = Stopwatch.StartNew();
        void Run(int ms)
        {
            long until = clock.ElapsedMilliseconds + ms;
            while (clock.ElapsedMilliseconds < until)
            {
                Thread.Sleep(33);
                _mix.Update();
                deck.TakePeak();
                _mix.Main.TakeLevels();
            }
        }

        Run(1000);

        // Full, compacting collections of a big heap, one after another, with the window's
        // thread (this one) checking for dropouts in between, as GZJam's does.
        var heap = new Node?[12_000_000];
        for (int i = 0; i < heap.Length; i++)
            heap[i] = new Node(i > 0 ? heap[i - 1] : null);
        TimeSpan longest = TimeSpan.Zero;
        for (int i = 0; i < 6; i++)
        {
            TimeSpan before = GC.GetTotalPauseDuration();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            TimeSpan paused = GC.GetTotalPauseDuration() - before;
            longest = paused > longest ? paused : longest;
            Run(100);
        }
        GC.KeepAlive(heap);
        heap = null;
        Run(1500);   // a gap is reported once it's been over for half a second

        // Fast computers tidy even this heap quickly: then this proves nothing either way.
        Assert.SkipWhen(longest < TimeSpan.FromMilliseconds(250),
            $"The longest memory pause was {longest.TotalMilliseconds:0} ms, within the speakers' buffer.");
        Assert.True(gaps.Count == 0,
            $"The sound stopped {gaps.Count} time(s) ({string.Join(", ", gaps.Select(g => $"{g.TotalMilliseconds:0} ms"))}) " +
            $"while .NET paused for up to {longest.TotalMilliseconds:0} ms");
        Assert.True(deck.IsPlaying);
    }
}
