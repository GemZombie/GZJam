using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>
/// When each queued song will play, by the clock (the "Plays at" column), and the planned start the
/// queue is counted from until the music starts. The sums are <see cref="QueueEnd"/>'s; the clock is
/// fixed here so they are exact.
/// </summary>
public sealed class PlannedStartTests
{
    /// <summary>A Wednesday evening, ten seconds into the minute, so a few seconds either way never changes a time shown.</summary>
    private static readonly DateTime Evening = new(2026, 11, 4, 19, 0, 10);

    private static Track Song(string name, int seconds) => new()
    {
        Path = "C:/nowhere/" + name + ".mp3", Folder = "", FileName = name + ".mp3", Title = name, Artist = "Band",
        Duration = TimeSpan.FromSeconds(seconds),
    };

    /// <summary>An app whose clock stands at <paramref name="now"/>; auto-play on with a minute between songs, so gaps show in the sums.</summary>
    private static AppHarness Night(DateTime? now = null, bool auto = true)
    {
        var app = new AppHarness();
        DateTime clock = now ?? Evening;
        app.ViewModel.Now = () => clock;
        app.Settings.GapSeconds = 60;
        if (auto)
            app.ViewModel.AutoPlay!.IsOn = true;
        return app;
    }

    private static List<string> Starts(AppHarness app) => app.ViewModel.Queue.Items.Select(i => i.StartsText).ToList();

    private static void Queue(AppHarness app, params Track[] songs)
    {
        app.ViewModel.Queue.Add(songs);
        app.ViewModel.UpdateQueueEnd();
    }

    private static void Plan(AppHarness app, DateTime when)
    {
        var plan = app.ViewModel.Plan;
        plan.DraftDate = when.Date;
        plan.DraftTime = when.TimeOfDay;
        plan.SetStartCommand.Execute(null);
    }

    [AvaloniaFact]
    public void Songs_follow_one_another_with_auto_plays_gap_between_them_counted_from_now()
    {
        using var app = Night();

        Queue(app, Song("a", 180), Song("b", 120), Song("c", 240));

        // 7:00:10, then 3:00 + 1:00 later (7:04:10), then 2:00 + 1:00 after that (7:07:10); over at 7:11:10.
        Assert.Equal(["7:00 PM", "7:04 PM", "7:07 PM"], Starts(app));
        Assert.Equal("—", app.ViewModel.QueueEndText);   // nothing plays yet: no end to give (a time would only drift)
    }

    [AvaloniaFact]
    public void By_hand_there_are_no_gaps()
    {
        using var app = Night(auto: false);

        Queue(app, Song("a", 180), Song("b", 120), Song("c", 240));

        Assert.Equal(["7:00 PM", "7:03 PM", "7:05 PM"], Starts(app));
    }

    [AvaloniaFact]
    public void Auto_plays_fade_after_cuts_a_long_song_short_in_the_sums()
    {
        using var app = Night();
        app.Settings.AutoFadeAfterSeconds = 120;
        app.Settings.FadeOutSeconds = 10;

        Queue(app, Song("a", 600), Song("b", 120));

        // The first plays 2:10 (2:00 and its fade), then the minute: 7:03:20.
        Assert.Equal(["7:00 PM", "7:03 PM"], Starts(app));
    }

    [AvaloniaFact]
    public void An_empty_queue_has_no_times_and_no_end()
    {
        using var app = Night();
        app.ViewModel.UpdateQueueEnd();

        Assert.Equal("—", app.ViewModel.QueueEndText);
        Assert.Empty(Starts(app));
    }

    [AvaloniaFact]
    public void A_planned_start_moves_every_time_and_gives_the_queues_end()
    {
        using var app = Night();
        Queue(app, Song("a", 180), Song("b", 120), Song("c", 240));

        Plan(app, new DateTime(2026, 11, 4, 21, 30, 0));

        var plan = app.ViewModel.Plan;
        Assert.Equal(new DateTime(2026, 11, 4, 21, 30, 0), plan.PlannedStart);
        Assert.Equal("Start 9:30 PM", plan.PlannedStartText);
        Assert.Equal(new DateTime(2026, 11, 4, 21, 30, 0), app.Settings.PlannedStart);   // kept with the settings
        Assert.Equal(["9:30 PM", "9:34 PM", "9:37 PM"], Starts(app));
        Assert.Equal("9:41 PM", app.ViewModel.QueueEndText);   // 9:37 + 4:00
        Assert.Contains("if the music starts at 9:30 PM", app.ViewModel.QueueEndTip);
    }

    [AvaloniaFact]
    public void A_planned_start_that_has_passed_with_nothing_playing_counts_from_now()
    {
        using var app = Night();
        Queue(app, Song("a", 180), Song("b", 120));
        app.Settings.PlannedStart = Evening.AddHours(-1);
        app.ViewModel.UpdateQueueEnd();

        Assert.Equal(["7:00 PM", "7:04 PM"], Starts(app));
        Assert.Equal("—", app.ViewModel.QueueEndText);
    }

    [AvaloniaFact]
    public void A_plan_from_days_ago_is_dropped()
    {
        using var app = Night();
        app.Settings.PlannedStart = Evening.AddDays(-2);

        app.ViewModel.UpdateQueueEnd();

        Assert.False(app.ViewModel.Plan.HasPlannedStart);
        Assert.Equal("Set start time", app.ViewModel.Plan.PlannedStartText);
    }

    [AvaloniaFact]
    public void Clearing_the_start_goes_back_to_now()
    {
        using var app = Night();
        Queue(app, Song("a", 180));
        Plan(app, new DateTime(2026, 11, 4, 22, 0, 0));
        Assert.Equal(["10:00 PM"], Starts(app));

        app.ViewModel.Plan.ClearStartCommand.Execute(null);

        Assert.False(app.ViewModel.Plan.HasPlannedStart);
        Assert.Equal(["7:00 PM"], Starts(app));
        Assert.Null(app.Settings.PlannedStart);
    }

    [AvaloniaFact]
    public void The_pickers_start_at_the_next_half_hour_or_at_the_plan()
    {
        using var app = Night(new DateTime(2026, 11, 4, 19, 7, 0));
        var plan = app.ViewModel.Plan;

        plan.PrepareDraft();
        Assert.Equal((new DateTime(2026, 11, 4), new TimeSpan(19, 30, 0)), (plan.DraftDate, plan.DraftTime));

        app.ViewModel.Now = () => new DateTime(2026, 11, 4, 19, 40, 0);
        plan.PrepareDraft();
        Assert.Equal(new TimeSpan(20, 0, 0), plan.DraftTime);

        app.Settings.PlannedStart = new DateTime(2026, 11, 5, 9, 15, 0);
        plan.PrepareDraft();
        Assert.Equal((new DateTime(2026, 11, 5), new TimeSpan(9, 15, 0)), (plan.DraftDate, plan.DraftTime));
    }

    [AvaloniaFact]
    public async Task While_a_song_plays_the_next_one_comes_after_what_is_left_of_it_and_the_gap()
    {
        using var app = Night();
        var deck = app.ViewModel.Deck1!;
        Assert.True(await app.ViewModel.LoadToDeckAsync(deck, app.ToneTrack("Playing", seconds: 30)));
        deck.TogglePlayCommand.Execute(null);
        app.Play(1.0);
        Assert.True(deck.IsOnAir);
        Queue(app, Song("next", 120), Song("after", 60));
        TimeSpan left = deck.Deck.Remaining;

        // The playing song is "now"; the next starts when it is over and the gap has passed.
        Assert.Equal("now", deck.StartsText);
        Assert.Equal(Clock(Evening + left + TimeSpan.FromSeconds(60)), Starts(app)[0]);
        Assert.Equal(Clock(Evening + left + TimeSpan.FromSeconds(60 + 120 + 60)), Starts(app)[1]);
        Assert.NotEqual("—", app.ViewModel.QueueEndText);
        // A planned start doesn't matter once the music is going.
        Plan(app, Evening.AddHours(3));
        Assert.Equal(Clock(Evening + left + TimeSpan.FromSeconds(60)), Starts(app)[0]);
    }

    [AvaloniaFact]
    public async Task A_song_loaded_and_waiting_on_a_deck_counts_before_the_queue()
    {
        using var app = Night();
        var deck = app.ViewModel.Deck1!;
        Assert.True(await app.ViewModel.LoadToDeckAsync(deck, app.ToneTrack("Waiting", seconds: 60)));
        Queue(app, Song("queued", 120));
        TimeSpan length = deck.Deck.Remaining;

        // Nothing plays yet: the deck's song is first, and the queue's follows it.
        Assert.Equal(Clock(Evening), deck.StartsText);
        Assert.Equal(Clock(Evening + length + TimeSpan.FromSeconds(60)), Starts(app)[0]);
    }

    [AvaloniaFact]
    public void A_wait_between_songs_makes_the_later_times_a_guess()
    {
        using var app = Night();
        app.ViewModel.AutoPlay!.WaitAfterEach = true;

        Queue(app, Song("a", 180), Song("b", 120));

        Assert.Equal(["7:00 PM", "~7:04 PM"], Starts(app));   // the first is when it starts, the rest wait for a press of play
    }

    [AvaloniaFact]
    public void Past_midnight_a_time_says_which_day()
    {
        using var app = Night(new DateTime(2026, 11, 4, 23, 50, 10), auto: false);

        Queue(app, Song("a", 1200), Song("b", 1200), Song("c", 1200));

        // 11:50 PM, 12:10 AM (Thursday), 12:30 AM.
        Assert.Equal(["11:50 PM", "Thu 12:10 AM", "Thu 12:30 AM"], Starts(app));
    }

    [AvaloniaFact]
    public void Songs_after_auto_plays_stop_time_never_play()
    {
        using var app = Night();
        app.ViewModel.AutoPlay!.StopAt = Evening.AddMinutes(5);

        Queue(app, Song("a", 180), Song("b", 120), Song("c", 240));

        Assert.Equal(["7:00 PM", "7:04 PM", ""], Starts(app));   // c would start at 7:07, after the stop at 7:05
    }

    [AvaloniaFact]
    public void The_column_shows_the_times_and_can_be_hidden_like_the_others()
    {
        using var app = Night(auto: false);   // with auto-play on, the first song would go straight to a deck
        Queue(app, Song("a", 180), Song("b", 120));
        AppHarness.Settle(200);
        List<TextBlock> Shown() => app.Window.FindControl<ListBox>("QueueList")!.GetVisualDescendants().OfType<TextBlock>()
            .Where(c => c.Classes.Contains("q-starts") && c.IsEffectivelyVisible).ToList();

        Assert.Contains("Starts", GZJam.App.Views.MainWindow.QueueColumns.Select(c => c.Name));
        Assert.Contains("Starts", app.Settings.HiddenQueueColumns);   // off to start with, like the album and the year
        app.Window.SetQueueColumnShown("Starts", true);
        AppHarness.Settle(200);
        Assert.Equal(["7:00 PM", "7:03 PM"], Shown().Select(t => t.Text));

        app.Window.SetQueueColumnShown("Starts", false);
        AppHarness.Settle(200);
        Assert.Empty(Shown());
        Assert.Contains("Starts", app.Settings.HiddenQueueColumns);
    }

    [AvaloniaFact]
    public void The_button_in_the_queues_header_says_what_is_planned()
    {
        using var app = Night();
        var button = app.Window.FindControl<Button>("QueueStartButton")!;
        var time = app.Window.FindControl<TextBlock>("QueuePlanText")!;
        Assert.False(time.IsVisible);   // nothing planned: only the clock
        Assert.DoesNotContain("planned", button.Classes);
        Assert.Contains("Say when you mean to start", ToolTip.GetTip(button)?.ToString());

        app.ViewModel.Plan.PrepareDraft();
        app.ViewModel.Plan.SetStartCommand.Execute(null);
        AppHarness.Settle(100);

        Assert.True(time.IsVisible);
        Assert.Equal("7:30 PM", time.Text);
        Assert.Contains("planned", button.Classes);   // lit
    }

    [AvaloniaFact]
    public void The_clock_is_there_at_the_default_width_and_plays_at_turned_on_shows_there_too()
    {
        using var app = Night(auto: false);
        Queue(app, Song("a", 180), Song("b", 120));
        app.Window.SetQueueColumnShown("Starts", true);
        AppHarness.Settle(300);

        var row = app.Window.FindControl<GZJam.App.Controls.FitRow>("QueueTabsRow")!;
        Assert.False(row.IsDropped(app.Window.FindControl<Button>("QueueStartButton")!));   // the DJ can always set a start time
        var song = app.Window.FindControl<ListBox>("QueueList")!.GetVisualDescendants().OfType<GZJam.App.Controls.FitRow>().First();
        bool Dropped(string part) => song.IsDropped(song.Children.First(c => c.Classes.Contains(part)));
        // Turned on, it's what the DJ wants to see: at the default width the art, rating and energy
        // give way for it (turned on and showing nothing, it would look broken). The BPM stays.
        Assert.False(Dropped("q-starts"));
        Assert.False(Dropped("q-bpm"));
        Assert.True(Dropped("q-art"));
        Assert.True(Dropped("q-rating"));
    }

    /// <summary>
    /// A queued song marked to fade out early (*Start fade-out here*) plays to its mark and the fade,
    /// not to its end: the songs after it move up by what it leaves out.
    /// </summary>
    [AvaloniaFact]
    public void A_queued_song_marked_to_fade_out_early_moves_the_next_ones_up()
    {
        using var app = Night(auto: false);   // auto-play would take the queue onto a deck as the test waits
        app.Settings.FadeOutSeconds = 5;
        Track early = Song("early", 240);
        Queue(app, early, Song("next", 120));
        AppHarness.WaitFor(() => Starts(app)[1] == "7:04 PM");   // 7:00:10 + 4:00

        app.ViewModel.SaveTrim(early.Path, new ManualTrim(null, TimeSpan.FromSeconds(100), FadeOut: true));

        // Fading from 1:40 over 5 seconds: 7:00:10 + 1:45 = 7:01:55.
        AppHarness.WaitFor(() => Starts(app)[1] == "7:01 PM");
        Assert.Equal(["7:00 PM", "7:01 PM"], Starts(app));
    }

    /// <summary>A song waiting on a deck, marked to fade out early: the queue after it counts to its fade's end.</summary>
    [AvaloniaFact]
    public async Task A_deck_song_marked_to_fade_out_early_counts_to_the_end_of_its_fade()
    {
        using var app = Night();
        app.Settings.FadeOutSeconds = 5;
        Track song = app.ToneTrack("Fades early", seconds: 120);
        app.ViewModel.SaveTrim(song.Path, new ManualTrim(null, TimeSpan.FromSeconds(30), FadeOut: true));
        var deck = app.ViewModel.Deck1!;
        Assert.True(await app.ViewModel.LoadToDeckAsync(deck, song));
        Queue(app, Song("queued", 120));

        Assert.Equal(35, deck.Deck.Remaining.TotalSeconds, 0.5);   // to the mark, and the fade
        Assert.Equal(Clock(Evening + deck.Deck.Remaining + TimeSpan.FromSeconds(60)), Starts(app)[0]);
    }

    /// <summary>
    /// The times are worked out every second, but a row is told only when the minute it shows
    /// changes: nothing made or raised for every row every second (#365).
    /// </summary>
    [AvaloniaFact]
    public void A_row_changes_only_when_its_minute_does()
    {
        using var app = new AppHarness();
        DateTime clock = Evening;   // 7:00:10
        app.ViewModel.Now = () => clock;
        app.ViewModel.AutoPlay!.IsOn = true;
        app.Settings.GapSeconds = 60;
        Queue(app, Song("a", 180), Song("b", 120), Song("c", 240));
        int raised = 0;
        foreach (var item in app.ViewModel.Queue.Items)
            item.PropertyChanged += (_, e) => raised += e.PropertyName == nameof(QueueItem.StartsText) ? 1 : 0;
        int planRaised = 0;
        app.ViewModel.Plan.PropertyChanged += (_, _) => planRaised++;

        for (int second = 1; second <= 45; second++)   // to 7:00:55: still the same minute for every row
        {
            clock = Evening.AddSeconds(second);
            app.ViewModel.UpdateQueueEnd();
        }
        Assert.Equal(0, raised);
        Assert.Equal(0, planRaised);

        clock = Evening.AddSeconds(55);   // 7:01:05: every row a minute on
        app.ViewModel.UpdateQueueEnd();
        Assert.Equal(3, raised);
        Assert.Equal(["7:01 PM", "7:05 PM", "7:08 PM"], Starts(app));
    }

    private static string Clock(DateTime time) => time.ToString("t", System.Globalization.CultureInfo.CurrentCulture);
}
