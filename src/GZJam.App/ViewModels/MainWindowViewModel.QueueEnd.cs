using CommunityToolkit.Mvvm.ComponentModel;
using GZJam.Library;

namespace GZJam.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    /// <summary>
    /// When the music queued runs out (#279), under auto-play: "Queue ends 11:42 PM", or "Stops
    /// 11:00 PM" when auto-play's Stop at, or Stop after this song, comes first. Only while a song
    /// plays to the room (before then a time would only drift), and not with Repeat (it doesn't
    /// run out; the options line says so).
    /// </summary>
    [ObservableProperty]
    public partial string QueueEndText { get; private set; } = "";

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

    /// <summary>Works out <see cref="QueueEndText"/> again: every second, and as the queue changes.</summary>
    internal void UpdateQueueEnd()
    {
        var decks = ActiveDecks;
        if (decks.FirstOrDefault(d => d.IsOnAir) is not { } live)
        {
            QueueEndText = "";
            QueueEndTip = null;
            return;
        }

        // The decks ready to play, in the order auto-play takes them (the one after the live deck first), then the queue.
        int at = decks.ToList().IndexOf(live);
        var upcoming = decks.Select((deck, i) => (deck, order: (i - at + decks.Count) % decks.Count))
            .Where(d => d.deck != live && d.deck.IsReady)
            .OrderBy(d => d.order)
            .Select(d => d.deck.Deck.Remaining)
            .Concat(Queue.Items.Select(i => PlayLength(i.Track)))
            .ToList();
        AutoPlayViewModel? autoPlay = AutoPlay;
        bool auto = autoPlay?.IsOn == true;
        var rules = new QueueEnd.Rules(
            auto,
            TimeSpan.FromSeconds(_settings.GapSeconds),
            _settings.AutoFadeAfterSeconds > 0 ? TimeSpan.FromSeconds(_settings.AutoFadeAfterSeconds + _settings.FadeOutSeconds) : null,
            autoPlay?.StopAt,
            autoPlay?.StopAfterThisSong == true,
            autoPlay?.Repeat ?? RepeatMode.Off);
        TimeSpan liveLength = live.Deck.End - live.Deck.Start + (live.Deck.EndFade ?? TimeSpan.Zero);
        QueueEnd.Estimate estimate = QueueEnd.Work(DateTime.Now, live.Deck.Remaining, liveLength, upcoming, rules);

        string time = estimate.At is { } end ? end.ToString("t") : "";
        (QueueEndText, QueueEndTip) = estimate.Kind switch
        {
            QueueEnd.Kind.Repeats => ("", null),
            QueueEnd.Kind.Stops => ($"Stops {time}",
                autoPlay?.StopAfterThisSong == true ? "Auto-play stops after this song, as asked"
                : $"Auto-play stops at {autoPlay?.StopAt:t}, as set: the song playing then is the last"),
            _ => ($"Queue ends {time}", auto
                ? "When the music queued runs out: what's left of this song, the songs ready on the decks and in the queue, and the gaps between them"
                : "When the music queued runs out, played one after another: what's left of this song, the songs ready on the decks and in the queue"),
        };
    }
}
