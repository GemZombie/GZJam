using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GZJam.App.ViewModels;

/// <summary>
/// When the DJ plans to start the queue, for planning a set ahead: until the music starts, the queue
/// counts from this time, so each song's "Plays at" and the time the queue <i>ends</i> (#279) say how
/// the night would go. Once a song plays to the room the real time takes over. A plan from long ago
/// (last night's) is dropped. The sums themselves are <see cref="QueueEnd"/>'s.
/// </summary>
public sealed partial class PlannedStartViewModel(MainWindowViewModel owner, AppSettings settings) : ObservableObject
{
    /// <summary>The planned start, if there is one; kept with the settings.</summary>
    public DateTime? PlannedStart
    {
        get => settings.PlannedStart;
        private set
        {
            if (settings.PlannedStart == value)
                return;
            settings.PlannedStart = value;
            Changed();
            owner.UpdateQueueEnd();
        }
    }

    public bool HasPlannedStart => PlannedStart is not null;

    /// <summary>The planned time alone ("7:30 PM"), shown beside the clock in the queue's header while there is room.</summary>
    public string PlannedTimeText => PlannedStart is { } start ? Clock(start, owner.Now()) : "";

    /// <summary>For the button in the queue's header: "Start 7:30 PM", or what it is for.</summary>
    public string PlannedStartText => PlannedStart is { } start ? "Start " + Clock(start, owner.Now()) : "Set start time";

    public string PlannedStartTip => PlannedStart is { } start
        ? $"The queue is counted from {Clock(start, owner.Now())} until the music starts. Click to change it."
        : "Say when you mean to start, to see when each song will play and when the queue will end";

    private void Changed()
    {
        OnPropertyChanged(nameof(PlannedStart));
        OnPropertyChanged(nameof(HasPlannedStart));
        OnPropertyChanged(nameof(PlannedStartText));
        OnPropertyChanged(nameof(PlannedTimeText));
        OnPropertyChanged(nameof(PlannedStartTip));
    }

    /// <summary>The day the button's text was last made for.</summary>
    private DateTime _shownDay;

    /// <summary>
    /// Drops a plan from long ago, and keeps "today" or a day's name in the button true as the clock
    /// moves on. Run with the queue's times, every second, so it raises its texts only when the day
    /// changes or the plan is dropped (#365).
    /// </summary>
    internal void Refresh(DateTime now)
    {
        bool dropped = PlannedStart is { } plan && plan < now.AddHours(-12);
        if (dropped)
            settings.PlannedStart = null;
        if (dropped || now.Date != _shownDay)
        {
            _shownDay = now.Date;
            Changed();
        }
    }

    /// <summary>What the date and time pickers hold before Set is pressed.</summary>
    [ObservableProperty]
    public partial DateTime? DraftDate { get; set; }

    [ObservableProperty]
    public partial TimeSpan? DraftTime { get; set; }

    /// <summary>The pickers' clock: 24 hours where that's how the computer writes the time.</summary>
    public string ClockIdentifier => CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('H') ? "24HourClock" : "12HourClock";

    /// <summary>The pickers start at the plan, or at the next half hour: the usual time for a dance to start.</summary>
    public void PrepareDraft()
    {
        if (PlannedStart is { } plan)
        {
            (DraftDate, DraftTime) = (plan.Date, plan.TimeOfDay);
            return;
        }
        DateTime now = owner.Now().AddMinutes(1);
        DateTime next = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute < 30 ? 30 : 0, 0).AddHours(now.Minute < 30 ? 0 : 1);
        (DraftDate, DraftTime) = (next.Date, next.TimeOfDay);
    }

    [RelayCommand]
    private void SetStart()
    {
        if (DraftDate is not { } date || DraftTime is not { } time)
            return;
        PlannedStart = date.Date + time;
        Log.Info($"Planned start: {PlannedStart:g}");
    }

    [RelayCommand]
    private void ClearStart()
    {
        PlannedStart = null;
        Log.Info("Planned start cleared");
    }

    /// <summary>A time of day as the computer writes it ("9:42 PM"); another day's has its name ("Thu 12:15 AM").</summary>
    internal static string Clock(DateTime time, DateTime now)
    {
        // Two formats: in "ddd t" a lone t is taken to mean "A" or "P", not the short time.
        string clock = time.ToString("t", CultureInfo.CurrentCulture);
        return time.Date == now.Date ? clock : time.ToString("ddd", CultureInfo.CurrentCulture) + " " + clock;
    }
}
