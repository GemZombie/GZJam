using CommunityToolkit.Mvvm.ComponentModel;
using GZJam.Library;

namespace GZJam.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    /// <summary>
    /// When the music queued runs out (#279), beside the clock: the time while a song plays to the
    /// room, or when auto-play's Stop at or Stop after this song comes first (the tip says which);
    /// "—" before a song plays (a time would only drift) and with Repeat (it doesn't run out).
    /// </summary>
    [ObservableProperty]
    public partial string QueueEndText { get; private set; } = NoEnd;

    /// <summary>Something is queued or loaded: <see cref="QueueEndText"/> shows.</summary>
    [ObservableProperty]
    public partial bool ShowsQueueEnd { get; private set; }

    private const string NoEnd = "—";

    [ObservableProperty]
    public partial string? QueueEndTip { get; private set; }

    /// <summary>How each queued song plays (its start, end, fade and jumps), read from the library once; null: nothing known, its length.</summary>
    private readonly Dictionary<string, SongPlay?> _songPlays = new(StringComparer.Ordinal);
    private bool _readingSongPlays;

    /// <summary>A song's start, end, fade or jumps changed, or its analysis came in: read it again.</summary>
    internal void ForgetSongPlay(string path)
    {
        if (_songPlays.Remove(path))
            ReadSongPlays();
    }

    /// <summary>Reads how the queued songs play, for those not read yet, off the UI thread.</summary>
    private async void ReadSongPlays()
    {
        if (_readingSongPlays)
            return;
        var paths = Queue.Items.Select(i => i.Track.Path).Where(p => !_songPlays.ContainsKey(p)).Distinct().ToList();
        if (paths.Count == 0)
            return;
        _readingSongPlays = true;
        try
        {
            var plays = await Task.Run(() => _database.GetSongPlays(paths));
            foreach (string path in paths)
                _songPlays[path] = plays.TryGetValue(path, out SongPlay play) ? play : null;
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't read how the queued songs play", ex);
        }
        finally
        {
            _readingSongPlays = false;
        }
        UpdateQueueEnd();
        ReadSongPlays();   // the queue may have changed meanwhile
    }

    /// <summary>How long a queued song plays: from its start to its end, less what its jumps skip, and its fade.</summary>
    private TimeSpan PlayLength(Track track)
    {
        if (!_songPlays.TryGetValue(track.Path, out SongPlay? known) || known is not { } play)
            return track.Duration;
        TimeSpan end = play.End ?? track.Duration;
        TimeSpan length = end - play.Start - play.Skipped;
        if (play.FadeOut && play.End is not null)
            length += TimeSpan.FromSeconds(_settings.FadeOutSeconds);
        return length > TimeSpan.Zero ? length : track.Duration;
    }

    /// <summary>The time of day; tests set it.</summary>
    internal Func<DateTime> Now { get; set; } = () => DateTime.Now;

    /// <summary>
    /// Works out <see cref="QueueEndText"/> again: every second, and as the queue changes. And, from the same
    /// sums, when each song to come will start (its <c>StartsText</c>, the "Plays at" column). While nothing
    /// plays to the room, a planned start (<see cref="Plan"/>) is where the count begins, and the queue's end
    /// shows from it; without one, the times are those if the music started now, and the end waits for a song.
    /// </summary>
    internal void UpdateQueueEnd()
    {
        var decks = ActiveDecks;
        DateTime now = Now();
        Plan?.Refresh(now);
        ShowsQueueEnd = !Queue.IsEmpty || decks.Any(d => d.HasTrack);
        DeckViewModel? live = decks.FirstOrDefault(d => d.IsOnAir);
        AutoPlayViewModel? autoPlay = AutoPlay;
        bool auto = autoPlay?.IsOn == true;
        var rules = new QueueEnd.Rules(
            auto,
            TimeSpan.FromSeconds(_settings.GapSeconds),
            _settings.AutoFadeAfterSeconds > 0 ? TimeSpan.FromSeconds(_settings.AutoFadeAfterSeconds + _settings.FadeOutSeconds) : null,
            autoPlay?.StopAt,
            autoPlay?.StopAfterThisSong == true,
            autoPlay?.Repeat ?? RepeatMode.Off);

        // The songs to come, in the order auto-play takes them: the decks ready (the one after the live deck first), then the queue.
        int at = live is null ? 0 : decks.ToList().IndexOf(live);
        var coming = decks.Select((deck, i) => (deck, order: (i - at + decks.Count) % decks.Count))
            .Where(d => d.deck != live && d.deck.IsReady)
            .OrderBy(d => d.order)
            .Select(d => (Deck: (DeckViewModel?)d.deck, Item: (QueueItem?)null, Length: d.deck.Deck.Remaining))
            .Concat(Queue.Items.Select(i => (Deck: (DeckViewModel?)null, Item: (QueueItem?)i, Length: PlayLength(i.Track))))
            .ToList();

        DateTime? plan = Plan?.PlannedStart is { } planned && planned > now ? planned : null;
        DateTime first = live is not null ? now + live.Deck.Remaining + (auto ? rules.Gap : TimeSpan.Zero) : plan ?? now;
        ShowStarts(live, coming, QueueEnd.Starts(first, [.. coming.Select(c => c.Length)], rules, afterLive: live is not null), now,
            guess: autoPlay is { WaitAfterEach: true }, firstIsExact: live is null);

        if (live is null)
        {
            if (plan is not { } start || coming.Count == 0)
            {
                QueueEndText = NoEnd;
                QueueEndTip = "When the music queued runs out: shown once a song plays to the room, or from the start time you set";
                return;
            }
            // Not started: the first song is the one the plan starts, and the rest follow it.
            var rest = coming.Skip(1).Select(c => c.Length).ToList();
            QueueEnd.Estimate fromPlan = QueueEnd.Work(start, coming[0].Length, coming[0].Length, rest, rules);
            SetQueueEnd(fromPlan, rules, autoPlay, auto, $"if the music starts at {PlannedStartViewModel.Clock(start, now)}", now);
            return;
        }

        TimeSpan liveLength = live.Deck.End - live.Deck.Start + (live.Deck.EndFade ?? TimeSpan.Zero);
        QueueEnd.Estimate estimate = QueueEnd.Work(now, live.Deck.Remaining, liveLength, [.. coming.Select(c => c.Length)], rules);
        SetQueueEnd(estimate, rules, autoPlay, auto, null, now);
    }

    /// <summary>Puts each song's start on the song: the live deck says "now", a deck or queued song the clock time (with "~" where auto-play waits for the DJ, so it's a guess).</summary>
    private void ShowStarts(DeckViewModel? live, List<(DeckViewModel? Deck, QueueItem? Item, TimeSpan Length)> coming,
        IReadOnlyList<DateTime?> starts, DateTime now, bool guess, bool firstIsExact)
    {
        // Each row's text is made only when what it shows changes (ShownStart): not every second.
        foreach (DeckViewModel deck in Decks)
        {
            if (deck == live)
                deck.ShowStart(null, false, now, playing: true);
            else if (!Coming(deck))
                deck.ShowStart(null, false, now);   // not to play: no time
        }
        for (int i = 0; i < coming.Count; i++)
        {
            bool isGuess = guess && !(i == 0 && firstIsExact);
            if (coming[i].Deck is { } deck)
                deck.ShowStart(starts[i], isGuess, now);
            else
                coming[i].Item!.ShowStart(starts[i], isGuess, now);
        }

        bool Coming(DeckViewModel deck)
        {
            foreach (var c in coming)
            {
                if (c.Deck == deck)
                    return true;
            }
            return false;
        }
    }

    private void SetQueueEnd(QueueEnd.Estimate estimate, QueueEnd.Rules rules, AutoPlayViewModel? autoPlay, bool auto, string? ifStarted, DateTime now)
    {
        string time = estimate.At is { } end ? end.ToString("t") : "";
        (QueueEndText, QueueEndTip) = estimate.Kind switch
        {
            QueueEnd.Kind.Repeats => (NoEnd, "Repeat is on: the queue goes round, so it doesn't run out"),
            QueueEnd.Kind.Stops => (time,
                autoPlay?.StopAfterThisSong == true ? "Auto-play stops after this song, as asked"
                : $"Auto-play stops at {autoPlay?.StopAt:t}, as set: the song playing then is the last"),
            _ => (time, ifStarted is not null
                ? $"When the music queued runs out, {ifStarted}: the songs ready on the decks and in the queue" + (auto ? ", and the gaps between them" : "")
                : auto
                ? "When the music queued runs out: what's left of this song, the songs ready on the decks and in the queue, and the gaps between them"
                : "When the music queued runs out, played one after another: what's left of this song, the songs ready on the decks and in the queue"),
        };
        // Songs still on a server (#290) count, but may not be here in time: said, with a mark.
        if (estimate.Kind != QueueEnd.Kind.Repeats && Queue.Items.Count(i => i.IsWaiting) is var waiting and > 0)
        {
            QueueEndText += " ↓";
            QueueEndTip += waiting == 1
                ? ". It counts 1 song still downloading."
                : $". It counts {waiting} songs still downloading.";
        }
    }
}