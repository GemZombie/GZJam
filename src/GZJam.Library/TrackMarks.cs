namespace GZJam.Library;

/// <summary>
/// Whether the DJ would play a track for a dance. Three-way on purpose: five stars drift into an
/// inconsistent mess, and the useful question is yes or no.
/// </summary>
public enum Rating
{
    Unrated = 0,
    /// <summary>♥: I'd play this for a dance.</summary>
    Loved = 1,
    /// <summary>Broken heart: I wouldn't.</summary>
    Rejected = -1,
}

/// <summary>How much energy a track brings to the floor, as the DJ judges it.</summary>
public enum Energy
{
    Unset = 0,
    Low = 1,
    Medium = 2,
    High = 3,
}
