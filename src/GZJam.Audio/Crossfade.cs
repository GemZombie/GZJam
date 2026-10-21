namespace GZJam.Audio;

/// <summary>
/// How the crossfader fades the room between the decks. It only affects the main (speaker)
/// output; what goes to the headphones is chosen per deck with its cue button.
/// "Audibility" is how far the fader leans towards a deck: 1 = all the way to it, 0 = away.
/// Equal-power curve, so a blend in the middle doesn't dip in volume.
/// </summary>
public static class Crossfade
{
    /// <summary>
    /// Which end of the crossfader a deck is on: 0 = left (Decks 1 and 3), 1 = right (2 and 4).
    /// Playing the decks in number order switches sides every time, so every change crosses it.
    /// </summary>
    public static int Side(int deckIndex) => deckIndex % 2;

    /// <summary>Audibility of a deck for a crossfader position (0 = all the left side, 1 = all the right).</summary>
    public static double Audibility(int deckIndex, double crossfader) =>
        Side(deckIndex) == 0 ? 1 - crossfader : crossfader;

    public static double MainGain(double audibility) => Math.Sin(Math.Clamp(audibility, 0, 1) * Math.PI / 2);
}
