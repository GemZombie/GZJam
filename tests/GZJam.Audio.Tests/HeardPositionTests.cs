using GZJam.Testing;

namespace GZJam.Audio.Tests;

/// <summary>
/// Where a deck is heard, not where it's been read to (#417): the speakers hold what's mixed for
/// a moment before playing it (about 430 ms at Normal protection on a laptop), and the playhead,
/// the times and the meters went that far ahead of the music. On this computer's real speakers,
/// at volume 0; skipped where there's no sound device, as on CI machines.
/// </summary>
[Collection("BASS")]
public sealed class HeardPositionTests : IDisposable
{
    private readonly AudioEngine _engine = new();
    private readonly MixEngine _mix;
    private readonly string _dir = Directory.CreateTempSubdirectory("gzjam-heard-").FullName;

    public HeardPositionTests() => _mix = new MixEngine(_engine);

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

    private void Run(int ms)
    {
        for (int waited = 0; waited < ms; waited += 25)
        {
            Thread.Sleep(25);
            _mix.Update();
        }
    }

    [Fact]
    public void A_playing_deck_is_heard_behind_where_its_read_to_by_the_speakers_latency()
    {
        AudioDevice? device = _engine.GetOutputDevices().FirstOrDefault(d => !d.FollowsSystemDefault);
        Assert.SkipWhen(device is null || TestMachine.IsCi, TestMachine.NeedsRealSpeakers);
        _mix.SetOutputs(device, null);
        _mix.Main.Volume = 0;
        Deck deck = _mix.Decks[0];
        deck.Load(TestWav.WriteTone(Path.Combine(_dir, "tone.wav"), TimeSpan.FromSeconds(30)));
        deck.Play();
        Run(1500);

        TimeSpan latency = _mix.SpeakerLatency, read = deck.Position, heard = deck.HeardPosition;
        Assert.InRange(latency, TimeSpan.FromMilliseconds(20), TimeSpan.FromSeconds(2));
        // Read between the two, so allow a tick or two either way.
        Assert.InRange(read - heard, latency - TimeSpan.FromMilliseconds(80), latency + TimeSpan.FromMilliseconds(80));
        Assert.InRange(deck.HeardRemaining(heard) - deck.Remaining, latency - TimeSpan.FromMilliseconds(80), latency + TimeSpan.FromMilliseconds(80));

        // Paused: what was on its way is played, then it's heard just where the deck stopped.
        deck.Pause();
        TimeSpan stopped = deck.Position;
        Assert.True(deck.HeardPosition < stopped);
        Run((int)latency.TotalMilliseconds + 300);
        Assert.Equal(stopped.TotalMilliseconds, deck.HeardPosition.TotalMilliseconds, tolerance: 5.0);

        // A seek while paused shows at once: nothing is on its way.
        deck.Seek(TimeSpan.FromSeconds(10));
        Run(50);
        Assert.Equal(10_000, deck.HeardPosition.TotalMilliseconds, tolerance: 5.0);
    }

    /// <summary>With no output (as in the app's tests), there's nothing to wait for: heard is where it's read to.</summary>
    [Fact]
    public void Offline_the_heard_position_is_where_its_read_to()
    {
        using var offline = new MixEngine(_engine, offline: true);
        Deck deck = offline.Decks[0];
        deck.Load(TestWav.WriteTone(Path.Combine(_dir, "offline.wav"), TimeSpan.FromSeconds(10)));
        deck.Play();
        offline.Render(TimeSpan.FromSeconds(1));
        offline.Update();

        Assert.Equal(TimeSpan.Zero, offline.SpeakerLatency);
        Assert.Equal(deck.Position.TotalMilliseconds, deck.HeardPosition.TotalMilliseconds, tolerance: 30.0);
        Assert.Equal(deck.Remaining.TotalMilliseconds, deck.HeardRemaining(deck.HeardPosition).TotalMilliseconds, tolerance: 30.0);
    }
}
