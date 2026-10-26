namespace GZJam.App.Tests;

/// <summary>The top bar's analysis progress says it's done once, and roughly how long is left (#139).</summary>
public sealed class AnalysisStatusTests
{
    [Fact]
    public void New_songs_say_first_time()
    {
        Assert.Equal("First-time analysis… 10 / 100", TrackAnalysisService.StatusText(10, 100, update: false, TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public void A_new_analyzer_says_updating()
    {
        Assert.Equal("Updating analysis… 10 / 100", TrackAnalysisService.StatusText(10, 100, update: true, TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public void Time_left_replaces_the_count_once_enough_songs_are_done()
    {
        Assert.EndsWith("24 / 12,000", TrackAnalysisService.StatusText(24, 12_000, false, TimeSpan.FromMinutes(5)));
        Assert.EndsWith("500 / 12,000", TrackAnalysisService.StatusText(500, 12_000, false, TimeSpan.FromSeconds(50)));
        // 100 songs a minute: 11,900 to go is a little under 2 hours.
        Assert.Equal("First-time analysis… 2 h left", TrackAnalysisService.StatusText(100, 12_000, false, TimeSpan.FromMinutes(1)));
    }

    [Theory]
    [InlineData(30, "under a minute left")]
    [InlineData(90, "2 min left")]
    [InlineData(45 * 60, "45 min left")]
    [InlineData(59 * 60 + 50, "1 h left")]
    [InlineData(100 * 60, "1 h 40 min left")]
    [InlineData(124 * 60, "2 h left")]
    public void Time_left_is_rounded(int seconds, string expected)
    {
        Assert.Equal(expected, TrackAnalysisService.TimeLeft(TimeSpan.FromSeconds(seconds)));
    }
}
