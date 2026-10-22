using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>The History session menu: name, export the set list, delete.</summary>
public sealed class SessionMenuTests
{
    private static readonly DateTime LastSaturday = new DateTime(2026, 10, 17, 19, 42, 0, DateTimeKind.Local).ToUniversalTime();

    /// <summary>A past gig with two songs from the library, recorded directly in History.</summary>
    private static (Track First, Track Second) PastGig(AppHarness app)
    {
        Track first = app.Library.Results[0], second = app.Library.Results[1];
        PlaySession gig = app.Database.StartOrContinueSession(LastSaturday, HistoryViewModel.ContinueWithin);
        app.Database.AddPlay(gig.Id, first, LastSaturday, "1", 0);
        app.Database.AddPlay(gig.Id, second, LastSaturday.AddMinutes(3), "2", -2);
        app.ViewModel.History.Reload();
        app.Library.RefreshResults();
        AppHarness.Settle();
        return (first, second);
    }

    [AvaloniaFact]
    public void The_menu_offers_naming_exporting_and_deleting()
    {
        using var app = new AppHarness();
        PastGig(app);
        app.ViewModel.ShowHistory = true;
        app.ViewModel.History.SelectedSession = app.ViewModel.History.Sessions.First(s => s.Session is not null);
        AppHarness.Settle(100);
        var button = app.Window.FindControl<Button>("SessionMenuButton")!;
        Assert.True(button.IsEffectivelyEnabled);

        button.Flyout!.ShowAt(button);
        AppHarness.Settle(150);
        var headers = ((MenuFlyout)button.Flyout).Items.OfType<MenuItem>().Select(i => (string)i.Header!).ToList();
        button.Flyout.Hide();
        Assert.Equal(["Name this session…", "Export set list…", "Delete this session…"], headers);
    }

    [AvaloniaFact]
    public void The_set_list_export_has_the_sessions_songs_in_order()
    {
        using var app = new AppHarness();
        var (first, second) = PastGig(app);
        HistoryViewModel history = app.ViewModel.History;
        history.SelectedSession = history.Sessions.First(s => s.Session is not null);
        history.RenameSelected("Hot Jam");

        var setList = history.SetListForSelected()!.Value;

        Assert.Equal("Set list - Hot Jam - 2026-10-17", setList.FileName);
        Assert.StartsWith("Hot Jam" + Environment.NewLine, setList.Text);
        Assert.True(setList.Text.IndexOf(first.DisplayTitle, StringComparison.Ordinal) < setList.Text.IndexOf(second.DisplayTitle, StringComparison.Ordinal));
        Assert.Equal(3, setList.Csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);   // heading + 2 songs
    }

    [AvaloniaFact]
    public void Deleting_a_session_asks_first_then_removes_it_and_its_played_dates()
    {
        using var app = new AppHarness();
        var (first, _) = PastGig(app);
        Assert.NotNull(app.Library.ResultFor(first.Path)!.LastPlayedUtc);
        HistoryViewModel history = app.ViewModel.History;
        app.ViewModel.ShowHistory = true;
        history.SelectedSession = history.Sessions.First(s => s.Session is not null);
        AppHarness.Settle(100);

        string? asked = null;
        bool answer = false;
        app.ViewModel.ConfirmAsync = (heading, _, confirm) =>
        {
            asked = $"{heading} [{confirm}]";
            return Task.FromResult(answer);
        };
        var delete = app.Window.FindControl<MenuItem>("DeleteSessionItem")!;

        // Cancelled: nothing changes.
        delete.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        AppHarness.Settle(100);
        Assert.StartsWith("Delete \"Sat Oct 17, 2026", asked);
        Assert.EndsWith("[Delete]", asked);
        Assert.Contains(history.Sessions, s => s.Session is not null);

        answer = true;
        delete.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        AppHarness.Settle();
        Assert.DoesNotContain(history.Sessions, s => s.Session is not null);
        Assert.Empty(history.Plays);
        Assert.Empty(app.Database.GetSessions().Where(s => s.PlayCount > 0));
        Assert.Null(app.Library.ResultFor(first.Path)!.LastPlayedUtc);   // the Played column forgets it
    }

    [AvaloniaFact]
    public void Deleting_tonights_session_starts_a_fresh_one_with_the_next_song()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        Track song = app.Library.Results[0];
        vm.Queue.Add([song]);
        vm.LoadToDeckAsync(vm.Deck1!, song).GetAwaiter().GetResult();
        vm.Deck1!.TogglePlayCommand.Execute(null);
        for (int i = 0; i < 64; i++)
            app.Play(0.5);
        Assert.True(song.IsPlayedTonight);

        vm.History.DeleteSelected();

        Assert.Null(vm.History.CurrentSessionStartUtc);
        Assert.False(vm.Queue.Items[0].Track.IsPlayedTonight);
        Assert.Empty(vm.History.Plays);

        // The next song the room hears goes into a new session.
        if (vm.Deck1.IsPlaying)
            vm.Deck1.TogglePlayCommand.Execute(null);   // pause
        Track next = app.Library.Results[1];
        vm.LoadToDeckAsync(vm.Deck1, next).GetAwaiter().GetResult();
        vm.Deck1.TogglePlayCommand.Execute(null);
        Assert.True(vm.Deck1.IsPlaying);
        for (int i = 0; i < 64; i++)
            app.Play(0.5);
        Assert.Equal(next.Path, Assert.Single(vm.History.Plays).Record.Path);   // only the new song: a fresh session
        Assert.Equal(1, vm.History.SelectedSession!.Session!.PlayCount);
    }
}
