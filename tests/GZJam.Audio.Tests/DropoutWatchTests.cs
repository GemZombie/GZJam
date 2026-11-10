namespace GZJam.Audio.Tests;

/// <summary>
/// The dropout watch (#345): a gap is the speakers' play position falling behind the clock and
/// staying behind. Fed here with the clock and the position as BASS gave them on Windows.
/// </summary>
public sealed class DropoutWatchTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(33);

    private readonly DropoutWatch _watch = new();
    private TimeSpan _real = TimeSpan.FromSeconds(100);
    private double _played = 5;
    private readonly List<TimeSpan> _found = [];

    /// <summary>One tick: <paramref name="realMs"/> of time, <paramref name="playedMs"/> of sound.</summary>
    private void Step(double realMs, double playedMs)
    {
        _real += TimeSpan.FromMilliseconds(realMs);
        _played += playedMs / 1000;
        if (_watch.Observe(_real, _played) is { } gap)
            _found.Add(gap);
    }

    private void Steady(int ticks, double jitterMs = 0.3)
    {
        for (int i = 0; i < ticks; i++)
            Step(Tick.TotalMilliseconds, Tick.TotalMilliseconds + (i % 2 == 0 ? jitterMs : -jitterMs));
    }

    [Fact]
    public void Steady_play_with_jitter_and_slow_drift_is_no_dropout()
    {
        Steady(30);
        // A sound card a little slower than the computer's clock: 0.5 ms a second, for ten minutes.
        for (int i = 0; i < 18_000; i++)
            Step(33, 33 * (1 - 0.0005));
        Assert.Empty(_found);
    }

    [Fact]
    public void Sound_gone_for_good_is_seen_growing_though_never_reported_as_a_dropout()
    {
        Steady(30);
        Assert.Equal(TimeSpan.Zero, _watch.Silent);
        // The card dropped out (#353): the position stands still from now on.
        for (int i = 0; i < 90; i++)
            Step(33, 0);
        Assert.InRange(_watch.Silent.TotalSeconds, 2.9, 3.0);
        Assert.Empty(_found);   // a dropout is reported once it's over, and this one isn't

        Steady(30);   // back (the output opened again): no longer silent
        Assert.Equal(TimeSpan.Zero, _watch.Silent);
    }

    [Fact]
    public void A_gap_is_reported_once_whole_after_it_ends()
    {
        Steady(30);
        // As measured: the position stood still for 222 ms over seven ticks, then went on.
        for (int i = 0; i < 6; i++)
            Step(33, 0);
        Step(33, 9);
        Assert.Empty(_found);   // not while it's happening
        Steady(20);

        TimeSpan gap = Assert.Single(_found);
        Assert.InRange(gap.TotalMilliseconds, 215, 230);
    }

    [Fact]
    public void A_gap_counted_late_because_the_whole_app_was_held_up_is_still_found()
    {
        Steady(30);
        // No tick for 600 ms (GZJam stopped), and the sound stopped for 250 ms of it.
        Step(600, 350);
        Steady(20);

        TimeSpan gap = Assert.Single(_found);
        Assert.InRange(gap.TotalMilliseconds, 245, 255);
    }

    [Fact]
    public void Late_ticks_while_the_sound_played_on_are_no_dropout()
    {
        Steady(30);
        Step(600, 600);   // the window was busy, the audio wasn't
        Step(250, 250);
        Steady(20);
        Assert.Empty(_found);
    }

    [Fact]
    public void After_the_computer_slept_or_new_outputs_it_starts_again()
    {
        Steady(30);
        Step(60_000, 0);   // asleep a minute
        Steady(30);
        _played = 0;       // new outputs: their position starts from nothing
        Step(33, 0);
        Steady(30);
        _watch.Reset();
        Steady(30);
        Assert.Empty(_found);
    }

    [Fact]
    public void Two_dropouts_a_while_apart_are_two()
    {
        Steady(30);
        Step(100, 0);    // 100 ms of silence
        Steady(90);      // three seconds
        Step(150, 0);    // 150 ms
        Steady(30);

        Assert.Equal(2, _found.Count);
        Assert.InRange(_found[0].TotalMilliseconds, 95, 105);
        Assert.InRange(_found[1].TotalMilliseconds, 145, 155);
    }
}
