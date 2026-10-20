namespace GZJam.Audio;

/// <summary>
/// How the crossfader splits each deck between the speakers and the headphones.
/// "Audibility" is how far the fader leans towards a deck: 1 = all the way to it, 0 = away.
/// The room hears a deck in proportion to its audibility (equal-power curve, so a blend in the
/// middle doesn't dip in volume), and the headphones hear the complement: whatever the room
/// can't hear is what you cue.
/// </summary>
public static class Crossfade
{
    /// <summary>Audibility of a deck for a crossfader position (0 = full A, 1 = full B).</summary>
    public static double Audibility(int deckIndex, double crossfader) => deckIndex switch
    {
        0 => 1 - crossfader,
        1 => crossfader,
        _ => 0, // decks C and D (future) are cue-only until assigned to a side
    };

    public static double MainGain(double audibility) => Math.Sin(Math.Clamp(audibility, 0, 1) * Math.PI / 2);

    public static double CueGain(double audibility) => Math.Cos(Math.Clamp(audibility, 0, 1) * Math.PI / 2);
}
