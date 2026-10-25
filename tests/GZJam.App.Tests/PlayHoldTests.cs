namespace GZJam.App.Tests;

/// <summary>
/// Two-tap pausing's press-and-hold, decided by the input's own times (#81): a quick tap during a
/// screen freeze, whose hold timer came due before the release was seen, is still a tap.
/// </summary>
public sealed class PlayHoldTests
{
    private const ulong T = 1_000_000;
    private static readonly TimeSpan Hold = TimeSpan.FromMilliseconds(600);

    [Fact]
    public void A_quick_release_before_the_timer_is_a_tap()
    {
        var hold = new PlayHold(Hold);
        hold.Press(T);
        Assert.Equal(PlayHold.Release.Tap, hold.Released(T + 200));
    }

    [Fact]
    public void A_release_after_the_hold_paused_ends_the_hold()
    {
        var hold = new PlayHold(Hold);
        hold.Press(T);
        hold.Paused();
        Assert.Equal(PlayHold.Release.Held, hold.Released(T + 800));
    }

    [Fact]
    public void A_tap_the_timer_took_for_a_hold_during_a_freeze_is_still_a_tap()
    {
        // Pressed at t, released at t + 0.2 s, but the screen froze: the timer was seen first, at t + 0.8 s.
        var hold = new PlayHold(Hold);
        hold.Press(T);
        hold.Paused();
        Assert.Equal(PlayHold.Release.TapAfterFreeze, hold.Released(T + 200));
    }

    [Fact]
    public void Held_exactly_as_long_as_a_hold_is_a_hold()
    {
        var hold = new PlayHold(Hold);
        hold.Press(T);
        hold.Paused();
        Assert.Equal(PlayHold.Release.Held, hold.Released(T + 600));
    }

    [Fact]
    public void A_release_with_no_press_seen_is_a_tap()
    {
        var hold = new PlayHold(Hold);
        Assert.Equal(PlayHold.Release.Tap, hold.Released(T));
    }

    [Fact]
    public void Each_press_starts_afresh()
    {
        var hold = new PlayHold(Hold);
        hold.Press(T);
        hold.Paused();
        hold.Released(T + 900);
        hold.Press(T + 5000);
        Assert.Equal(PlayHold.Release.Tap, hold.Released(T + 5100));
    }
}
