using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>When the music queued runs out (#279): the time after the queue's summary.</summary>
public sealed class QueueEndTests
{
    private static readonly DateTime Now = new(2026, 11, 7, 21, 0, 0);
    private static TimeSpan M(double minutes) => TimeSpan.FromMinutes(minutes);
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static QueueEnd.Rules Auto(double gap = 3, TimeSpan? fadeAfter = null, DateTime? stopAt = null, bool stopAfter = false,
        RepeatMode repeat = RepeatMode.Off) => new(true, S(gap), fadeAfter, stopAt, stopAfter, repeat);

    private static readonly QueueEnd.Rules ByHand = new(false, S(3), null, null, false, RepeatMode.Off);

    [Fact]
    public void By_hand_its_the_song_playing_then_the_rest_one_after_another()
    {
        var end = QueueEnd.Work(Now, M(1), M(3), [M(3), M(4)], ByHand);
        Assert.Equal(new QueueEnd.Estimate(QueueEnd.Kind.Ends, Now + M(8)), end);
    }

    [Fact]
    public void Auto_play_adds_the_gap_before_each_song_and_cuts_songs_at_its_fade_after()
    {
        Assert.Equal(Now + M(8) + S(6), QueueEnd.Work(Now, M(1), M(3), [M(3), M(4)], Auto()).At);
        // Fade after 2:30 (with its fade): both songs to come are cut there; the one playing is already timed.
        Assert.Equal(Now + M(1) + S(3) + M(2.5) + S(3) + M(2.5),
            QueueEnd.Work(Now, M(1), M(3), [M(3), M(4)], Auto(fadeAfter: M(2.5))).At);
    }

    [Fact]
    public void Stop_after_this_song_and_stop_at_end_it_sooner()
    {
        Assert.Equal(new QueueEnd.Estimate(QueueEnd.Kind.Stops, Now + M(1)),
            QueueEnd.Work(Now, M(1), M(3), [M(3), M(4)], Auto(stopAfter: true)));

        // Stop at 21:02: the song playing then (the first from the queue, 21:01 to 21:04) is the last.
        var end = QueueEnd.Work(Now, M(1), M(3), [M(3), M(4)], Auto(gap: 0, stopAt: Now + M(2)));
        Assert.Equal(new QueueEnd.Estimate(QueueEnd.Kind.Stops, Now + M(4)), end);

        // A stop time after the queue runs out doesn't change when it ends.
        Assert.Equal(new QueueEnd.Estimate(QueueEnd.Kind.Ends, Now + M(8)),
            QueueEnd.Work(Now, M(1), M(3), [M(3), M(4)], Auto(gap: 0, stopAt: Now + M(30))));
    }

    [Fact]
    public void With_repeat_the_queue_never_runs_out_unless_auto_play_stops_at_a_time()
    {
        Assert.Equal(QueueEnd.Kind.Repeats, QueueEnd.Work(Now, M(1), M(3), [M(3)], Auto(repeat: RepeatMode.Queue)).Kind);
        Assert.Equal(QueueEnd.Kind.Repeats, QueueEnd.Work(Now, M(1), M(3), [], Auto(repeat: RepeatMode.Song)).Kind);

        // This song over and over, 3:00 each from 21:01, stopping at 21:09: the one playing then ends at 21:10.
        var end = QueueEnd.Work(Now, M(1), M(3), [M(4)], Auto(gap: 0, stopAt: Now + M(9), repeat: RepeatMode.Song));
        Assert.Equal(new QueueEnd.Estimate(QueueEnd.Kind.Stops, Now + M(10)), end);
    }

    [AvaloniaFact]
    public void The_queue_says_when_it_ends_while_a_song_plays_to_the_room()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        Track playing = app.ToneTrack("Playing", seconds: 30);
        Assert.True(AppHarness.Await(vm.LoadToDeckAsync(vm.Deck1!, playing)));
        vm.Queue.Add([app.Library.Results[0], app.Library.Results[1]]);
        vm.UpdateQueueEnd();
        Assert.True(vm.ShowsQueueEnd);
        Assert.Equal("—", vm.QueueEndText);   // nothing playing yet: no time to give

        vm.Deck1!.TogglePlayCommand.Execute(null);
        app.Play(0.1);
        AppHarness.WaitFor(() =>
        {
            vm.UpdateQueueEnd();
            return vm.QueueEndText != "—";
        });
        Assert.True(DateTime.TryParse(vm.QueueEndText, out _), vm.QueueEndText);   // the sums themselves: the tests above
        Assert.Contains("runs out", vm.QueueEndTip);
        app.Screenshot("queue-end");

        vm.AutoPlay!.IsOn = true;
        vm.AutoPlay.StopAfterThisSong = true;
        vm.UpdateQueueEnd();
        Assert.True(DateTime.TryParse(vm.QueueEndText, out _), vm.QueueEndText);
        Assert.StartsWith("Auto-play stops after this song", vm.QueueEndTip);

        vm.AutoPlay.StopAfterThisSong = false;
        vm.AutoPlay.Repeat = RepeatMode.Queue;
        vm.UpdateQueueEnd();
        Assert.Equal("—", vm.QueueEndText);   // it doesn't run out
        Assert.StartsWith("Repeat is on", vm.QueueEndTip);
    }
}
