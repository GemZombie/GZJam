namespace GZJam.App.ViewModels;

/// <summary>
/// What a row's "Plays at" (#297) last showed: the minute, whether a guess, whether another day's.
/// The queue's times are worked out every second, but a row's text is made again, and raised, only
/// when one of these changes: once a minute at most, not every row every second (#365).
/// </summary>
internal struct ShownStart
{
    private (long Minute, bool Guess, bool OtherDay)? _key;
    private bool _set;

    /// <summary>The live deck's: "now".</summary>
    private static readonly (long, bool, bool) Now = (long.MinValue, false, false);

    /// <summary>
    /// The text for a song starting <paramref name="at"/> ("~" first if <paramref name="guess"/>;
    /// empty for one that never plays; "now" if <paramref name="playing"/>), or null if that's
    /// what it shows already.
    /// </summary>
    public string? Update(DateTime? at, bool guess, DateTime now, bool playing = false)
    {
        (long, bool, bool)? key = playing ? Now
            : at is { } time ? (time.Ticks / TimeSpan.TicksPerMinute, guess, time.Date != now.Date)
            : null;
        if (_set && key == _key)
            return null;
        (_key, _set) = (key, true);
        return playing ? "now" : at is { } start ? (guess ? "~" : "") + PlannedStartViewModel.Clock(start, now) : "";
    }
}
