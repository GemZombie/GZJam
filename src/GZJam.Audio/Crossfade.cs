namespace GZJam.Audio;

/// <summary>
/// How the crossfader fades the room between the decks. It only affects the main (speaker)
/// output; what goes to the headphones is chosen per deck with its cue button.
/// "Audibility" is how far the fader leans towards a deck: 1 = all the way to it, 0 = away.
/// Equal-power curve, so a blend in the middle doesn't dip in volume.
/// </summary>
public static class Crossfade
{
    /// <summary>Audibility of a deck for a crossfader position (0 = full A, 1 = full B).</summary>
    public static double Audibility(int deckIndex, double crossfader) => deckIndex switch
    {
        0 => 1 - crossfader,
        1 => crossfader,
        _ => 0, // decks C and D (future) aren't on the crossfader until assigned to a side
    };

    public static double MainGain(double audibility) => Math.Sin(Math.Clamp(audibility, 0, 1) * Math.PI / 2);
}
