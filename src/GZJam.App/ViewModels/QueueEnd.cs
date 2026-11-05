namespace GZJam.App.ViewModels;

/// <summary>
/// When the music queued runs out (#279): from the end of the song the room hears now, through
/// the songs ready on the decks and in the queue, with auto-play's gaps; or where auto-play stops
/// first (<see cref="Rules.StopAt"/>, <see cref="Rules.StopAfterThisSong"/>); or never, with Repeat.
/// </summary>
public static class QueueEnd
{
    /// <summary>What auto-play does between songs; all off when it isn't on (the DJ plays by hand: no gaps, no stops).</summary>
    public sealed record Rules(
        bool AutoPlay,
        TimeSpan Gap,
        TimeSpan? FadeAfter,
        DateTime? StopAt,
        bool StopAfterThisSong,
        RepeatMode Repeat);

    public enum Kind { Ends, Stops, Repeats }

    public readonly record struct Estimate(Kind Kind, DateTime? At);

    /// <summary>The most songs counted with Repeat and a stop time: a queue of songs a second long would take a long while.</summary>
    private const int MostSongs = 5000;

    /// <param name="liveLeft">What's left of the song the room hears.</param>
    /// <param name="liveLength">That song's whole length (Repeat this song plays it again).</param>
    /// <param name="upcoming">How long each song to come plays, in order: the decks ready, then the queue.</param>
    public static Estimate Work(DateTime now, TimeSpan liveLeft, TimeSpan liveLength, IReadOnlyList<TimeSpan> upcoming, Rules rules)
    {
        DateTime at = now + liveLeft;
        if (!rules.AutoPlay)
            return new Estimate(Kind.Ends, at + Sum(upcoming));
        if (rules.StopAfterThisSong || rules.StopAt is { } early && at >= early)
            return new Estimate(Kind.Stops, at);

        IReadOnlyList<TimeSpan> round = rules.Repeat switch
        {
            RepeatMode.Song => [liveLength],
            RepeatMode.Queue => [.. upcoming, liveLength],
            _ => upcoming,
        };
        bool repeats = rules.Repeat != RepeatMode.Off && round.Any(l => l > TimeSpan.Zero);
        if (repeats && rules.StopAt is null)
            return new Estimate(Kind.Repeats, null);

        for (int i = 0; repeats ? i < MostSongs : i < round.Count; i++)
        {
            TimeSpan length = round[i % round.Count];
            if (rules.FadeAfter is { } limit && length > limit)
                length = limit;
            at += rules.Gap + length;
            if (rules.StopAt is { } stopAt && at >= stopAt)
                return new Estimate(Kind.Stops, at);   // the song playing then is the last
        }
        return repeats ? new Estimate(Kind.Repeats, null) : new Estimate(Kind.Ends, at);
    }

    private static TimeSpan Sum(IEnumerable<TimeSpan> lengths) => TimeSpan.FromTicks(lengths.Sum(l => l.Ticks));
}
