using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>Played recently: tonight's songs dimmed, the Played column, naming a session.</summary>
public sealed class PlayedAppTests
{
    /// <summary>Plays a library track to the room long enough to count as played.</summary>
    private static Track PlayToTheRoom(AppHarness app)
    {
        Track track = app.Library.Results[0];
        app.ViewModel.LoadToDeckAsync(app.ViewModel.Deck1!, track).GetAwaiter().GetResult();
        app.ViewModel.Deck1!.TogglePlayCommand.Execute(null);
        for (int i = 0; i < 64; i++)
            app.Play(0.5);
        Assert.Single(app.ViewModel.History.Plays);
        return track;
    }

    [AvaloniaFact]
    public void A_song_played_tonight_is_dimmed_in_the_library_and_the_queue()
    {
        using var app = new AppHarness();
        Track played = app.Library.Results[0];
        app.ViewModel.Queue.Add([played]);
        Assert.False(played.IsPlayedTonight);

        PlayToTheRoom(app);

        Assert.True(played.IsPlayedTonight);   // the library's copy (also the queue's)
        Assert.Equal(1, played.PlayCount);
        Assert.Equal("Today", Format.Ago(played.LastPlayedUtc));

        // A fresh search still knows: it reads History.
        app.Library.SearchText = played.Title!;
        AppHarness.Settle();
        Track again = app.Library.Results.Single(t => t.Path == played.Path);
        Assert.NotSame(played, again);
        Assert.True(again.IsPlayedTonight);
        Assert.DoesNotContain(app.Library.Results, t => t.Path != played.Path && t.IsPlayedTonight);
    }

    [AvaloniaFact]
    public void A_session_can_be_named()
    {
        using var app = new AppHarness();
        PlayToTheRoom(app);
        HistoryViewModel history = app.ViewModel.History;
        Assert.True(history.CanRename);

        history.RenameSelected("Friday Night, Oct 14");

        Assert.StartsWith("Friday Night, Oct 14 · ", history.SelectedSession!.Label);
        Assert.EndsWith(" · 1 song", history.SelectedSession.Label);
        Assert.Equal("Friday Night, Oct 14", history.SelectedName);

        history.RenameSelected("");
        Assert.StartsWith("This session · ", history.SelectedSession!.Label);
    }

    [Theory]
    [InlineData(0, "Today")]
    [InlineData(1, "1 day")]
    [InlineData(5, "5 days")]
    [InlineData(21, "3 wk")]
    [InlineData(150, "5 mo")]
    [InlineData(800, "2 yr")]
    public void Played_shows_how_long_ago(int daysAgo, string expected)
    {
        DateTime now = new(2026, 10, 21, 18, 0, 0, DateTimeKind.Local);
        Assert.Equal(expected, Format.Ago(now.AddDays(-daysAgo).ToUniversalTime(), now.ToUniversalTime()));
    }

    [Fact]
    public void Never_played_is_blank() => Assert.Equal("", Format.Ago(null));
}
