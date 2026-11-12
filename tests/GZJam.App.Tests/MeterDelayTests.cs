using GZJam.App.ViewModels;

namespace GZJam.App.Tests;

/// <summary>The meters wait for the room to hear what they read (#417), and lose no peak doing so.</summary>
public sealed class MeterDelayTests
{
    [Fact]
    public void With_no_latency_a_reading_passes_straight_through()
    {
        var delay = new MeterDelay();
        Assert.Equal((0.5f, 0.25f, true), delay.Pass(0.5f, 0.25f, true, TimeSpan.Zero));
        Assert.Equal((0.1f, 0.2f, false), delay.Pass(0.1f, 0.2f, false, TimeSpan.Zero));
    }

    [Fact]
    public void A_reading_comes_out_once_the_latency_has_passed_with_the_loudest_of_whats_due()
    {
        var delay = new MeterDelay();
        var latency = TimeSpan.FromMilliseconds(150);

        Assert.Equal((0f, 0f, false), delay.Pass(0.9f, 0.3f, true, latency));   // not heard yet
        Assert.Equal((0f, 0f, false), delay.Pass(0.2f, 0.8f, false, latency));
        Thread.Sleep(200);

        // Both earlier readings are due now: the loudest of each side, and the light if either had it.
        Assert.Equal((0.9f, 0.8f, true), delay.Pass(0.1f, 0.1f, false, latency));
        Assert.Equal((0f, 0f, false), delay.Pass(0.1f, 0.1f, false, latency));   // the newest still waits
    }
}
