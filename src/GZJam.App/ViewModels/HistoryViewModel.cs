using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>A song in the history list.</summary>
public sealed class PlayItem(PlayRecord record, Track track, int number)
{
    public PlayRecord Record { get; } = record;

    /// <summary>The library's track if it's still there (so it can be shown in the folder tree), else one rebuilt from the record.</summary>
    public Track Track { get; } = track;

    public int Number { get; } = number;
    public string TimeText => Record.PlayedUtc.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);
    public string Title => Record.DisplayTitle;
    public string Artist => Record.Artist ?? "";
    public string BpmText => Format.Bpm(Record.Bpm is { } bpm ? bpm * (1 + Record.TempoPercent / 100) : null);
    public string DurationText => Format.Duration(Record.Duration);
}

/// <summary>An entry in the session drop-down.</summary>
public sealed record SessionChoice(PlaySession? Session, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// The History tab: every song the room heard, grouped into sessions (gigs) and saved, so any
/// past night can be looked up by date.
/// </summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    /// <summary>Restarting GZJam within this long of the last song continues the same session.</summary>
    public static readonly TimeSpan ContinueWithin = TimeSpan.FromHours(3);

    private readonly LibraryDatabase _database;
    private PlaySession? _current;

    public HistoryViewModel(LibraryDatabase database)
    {
        _database = database;
        Reload();
    }

    public ObservableCollection<SessionChoice> Sessions { get; } = [];
    public ObservableCollection<PlayItem> Plays { get; } = [];

    [ObservableProperty]
    public partial SessionChoice? SelectedSession { get; set; }

    public bool IsEmpty => Plays.Count == 0;

    public string Summary
    {
        get
        {
            if (Plays.Count == 0)
                return "";
            var total = TimeSpan.FromTicks(Plays.Sum(p => p.Record.Duration.Ticks));
            return $"{Plays.Count} {(Plays.Count == 1 ? "song" : "songs")} · {Format.Duration(total)}";
        }
    }

    /// <summary>When tonight's session began (null before its first song): songs played since are "played tonight".</summary>
    public DateTime? CurrentSessionStartUtc => _current?.StartedUtc;

    /// <summary>The selected session's name, for renaming.</summary>
    public string? SelectedName => SelectedSession?.Session?.Name;

    public bool CanRename => SelectedSession?.Session is not null;

    /// <summary>Names the selected session ("Hot Jam, Oct 14"); empty goes back to its date.</summary>
    public void RenameSelected(string? name)
    {
        if (SelectedSession?.Session is not { } session)
            return;
        _database.RenameSession(session.Id, name);
        Log.Info($"History: session {session.Id} named \"{name}\"");
        Reload(keepCurrentSelected: false);
    }

    /// <summary>Whether the selected session is the one being recorded into now.</summary>
    private bool ShowingCurrent => SelectedSession is { } s && (s.Session is null || s.Session.Id == _current?.Id);

    /// <summary>Records a song the room heard. Starts (or continues) the session on the first one.</summary>
    public void Record(Track track, DateTime playedUtc, string deck, double tempoPercent, TimeSpan duration)
    {
        bool wasShowingCurrent = ShowingCurrent;
        _current ??= _database.StartOrContinueSession(playedUtc, ContinueWithin);
        PlayRecord record = _database.AddPlay(_current.Id, track, playedUtc, deck, tempoPercent, duration);
        Log.Info($"History: \"{record.DisplayTitle}\" ({track.Path})");

        Reload(keepCurrentSelected: wasShowingCurrent);
    }

    /// <summary>Re-reads the session list; keeps the selection, or picks "This session".</summary>
    public void Reload(bool keepCurrentSelected = true)
    {
        long? selectedId = SelectedSession?.Session?.Id ?? (keepCurrentSelected ? null : _current?.Id);
        bool selectCurrent = SelectedSession is null || (keepCurrentSelected && ShowingCurrent);

        var sessions = _database.GetSessions();
        // On startup, the latest session counts as "this session" if it's recent enough to continue.
        if (_current is null && sessions.FirstOrDefault() is { } latest &&
            DateTime.UtcNow - (latest.LastPlayUtc ?? latest.StartedUtc) <= ContinueWithin && latest.PlayCount > 0)
            _current = latest;

        Sessions.Clear();
        if (_current is null)
            Sessions.Add(new SessionChoice(null, "This session"));
        foreach (PlaySession session in sessions.Where(s => s.PlayCount > 0))
            Sessions.Add(new SessionChoice(session, Label(session)));

        SelectedSession = selectCurrent
            ? Sessions.FirstOrDefault(s => s.Session is null || s.Session.Id == _current?.Id) ?? Sessions.FirstOrDefault()
            : Sessions.FirstOrDefault(s => s.Session?.Id == selectedId) ?? Sessions.FirstOrDefault();
        LoadPlays();
    }

    private string Label(PlaySession session)
    {
        DateTime local = session.StartedUtc.ToLocalTime();
        string songs = $"{session.PlayCount} {(session.PlayCount == 1 ? "song" : "songs")}";
        if (session.Name is { } name)
            return $"{name} · {local.ToString("MMM d", CultureInfo.CurrentCulture)} · {songs}";
        string when = session.Id == _current?.Id ? "This session" : $"{local.ToString("ddd MMM d, yyyy", CultureInfo.CurrentCulture)} · {local.ToString("t", CultureInfo.CurrentCulture)}";
        return $"{when} · {songs}";
    }

    partial void OnSelectedSessionChanged(SessionChoice? value)
    {
        LoadPlays();
        OnPropertyChanged(nameof(SelectedName));
        OnPropertyChanged(nameof(CanRename));
    }

    private void LoadPlays()
    {
        Plays.Clear();
        if (SelectedSession?.Session is { } session)
        {
            int n = 0;
            foreach (PlayRecord record in _database.GetPlays(session.Id))
                Plays.Add(new PlayItem(record, _database.FindTrack(record.Path) ?? record.ToTrack(), ++n));
        }
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Summary));
    }
}
