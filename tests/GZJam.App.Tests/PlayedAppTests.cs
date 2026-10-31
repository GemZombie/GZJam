using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>Played recently: tonight's songs checked, the Played column, naming a session.</summary>
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

    /// <summary>
    /// A song played tonight gets a check by its title, at full strength: dimmed, it was taken for
    /// a missing file (#238). Gray is for a missing file.
    /// </summary>
    [AvaloniaFact]
    public void A_song_played_tonight_gets_a_check_not_gray_and_a_missing_file_is_gray()
    {
        using var app = new AppHarness();
        Track played = app.Library.Results[0];
        app.ViewModel.Queue.Add([played]);
        string gone = System.IO.Path.Combine(app.WorkDirectory, "Old set", "gone song.mp3");
        var playlist = app.ViewModel.CreatePlaylist("Saturday", [played])!;
        app.Database.AddToPlaylist(playlist.Id, [gone]);
        PlayToTheRoom(app);
        app.Library.OpenPlaylist(playlist.Id);
        AppHarness.Settle(300);

        var grid = app.Window.FindControl<Avalonia.Controls.DataGrid>("TrackGrid")!;
        var rows = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(grid).OfType<Avalonia.Controls.DataGridRow>().ToList();
        var playedRow = rows.Single(r => r.DataContext is Track { PlaylistNumber: 1 });
        var missingRow = rows.Single(r => r.DataContext is Track { PlaylistNumber: 2 });
        Assert.Equal(1, playedRow.Opacity);
        Assert.True(Check(playedRow, "played-check").IsEffectivelyVisible);
        Assert.False(Check(playedRow, "missing-mark").IsEffectivelyVisible);
        Assert.True(((Track)missingRow.DataContext!).IsMissing);
        Assert.Equal(0.4, missingRow.Opacity, 2);
        Assert.True(Check(missingRow, "missing-mark").IsEffectivelyVisible);
        Assert.False(Check(missingRow, "played-check").IsEffectivelyVisible);
        Assert.Contains("1 missing", app.Library.PlaylistSummary);

        // The queue's row: full strength, with the check.
        var queueItem = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(app.Window.FindControl<Avalonia.Controls.ListBox>("QueueList")!)
            .OfType<Avalonia.Controls.ListBoxItem>().Single();
        Assert.True(Check(queueItem, "played-check").IsEffectivelyVisible);
        Assert.All(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(queueItem).OfType<Avalonia.Controls.Panel>(), p => Assert.Equal(1, p.Opacity));
    }

    private static Avalonia.Controls.Shapes.Path Check(Avalonia.Visual row, string kind) =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(row).OfType<Avalonia.Controls.Shapes.Path>().Single(p => p.Classes.Contains(kind));

    [AvaloniaFact]
    public void A_song_played_tonight_is_marked_in_the_library_and_the_queue()
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
