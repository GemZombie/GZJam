using System.Diagnostics;
using ManagedBass;

namespace GZJam.Audio.Tests;

/// <summary>
/// Dropouts on this computer's real speakers, shared with other programs (#345): the audio thread
/// is held up on purpose, as a long .NET garbage collection did at Hot Jam (#343), and the gap is
/// found and measured. Silence plays; nothing is heard. Skipped where there's no sound device, as
/// on CI machines.
/// </summary>
[Collection("BASS")]
public sealed class SpeakerDropoutTests : IDisposable
{
    private readonly AudioEngine _engine = new();
    private readonly MixEngine _mix;

    public SpeakerDropoutTests() => _mix = new MixEngine(_engine);

    public void Dispose()
    {
        _mix.Dispose();
        _engine.Dispose();
    }

    [Fact]
    public void Held_up_audio_is_logged_as_a_dropout_of_about_the_right_length_and_steady_play_isnt()
    {
        AudioDevice? device = _engine.GetOutputDevices().FirstOrDefault(d => !d.FollowsSystemDefault);
        Assert.SkipWhen(device is null, "Needs a sound device.");
        _engine.Buffer = SoundBuffer.Low;   // the shortest: 200 ms of mix ready
        _mix.SetOutputs(device, null);
        Assert.False(_mix.Main.IsExclusive);
        var gaps = new List<TimeSpan>();
        _mix.MainDropout += (_, gap) => gaps.Add(gap);

        // Held up once, for 400 ms, from inside the speakers' mix: the buffer (200 ms) runs dry.
        var clock = Stopwatch.StartNew();
        long holdAt = long.MaxValue;
        int held = 0;
        DSPProcedure holdUp = (_, _, _, _, _) =>
        {
            if (clock.ElapsedMilliseconds >= Interlocked.Read(ref holdAt) && Interlocked.Exchange(ref held, 1) == 0)
                Thread.Sleep(400);
        };
        Bass.ChannelSetDSP(_mix.Main.Handle, holdUp, IntPtr.Zero, 100);

        void Run(int ms)
        {
            long until = clock.ElapsedMilliseconds + ms;
            while (clock.ElapsedMilliseconds < until)
            {
                Thread.Sleep(33);
                _mix.Update();
            }
        }

        Run(1500);
        Assert.Empty(gaps);   // steady play: nothing

        Interlocked.Exchange(ref holdAt, clock.ElapsedMilliseconds);
        Run(2000);

        TimeSpan gap = Assert.Single(gaps);
        Assert.InRange(gap.TotalMilliseconds, 120, 400);
        GC.KeepAlive(holdUp);
    }
}
