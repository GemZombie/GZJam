namespace GZJam.App.Controls;

/// <summary>
/// The shapes of GZJam's logo: a waveform wrapped into a G, and the name, GZJAM, in drawn letters
/// cut into bars like a waveform (GZ coloured as the part of a song already played, JAM as the part
/// still to come). The app draws them (<see cref="LogoMark"/>, <see cref="Wordmark"/>), and so does
/// the developer tool that writes the app icons and the SVG files in branding/
/// (tools/GZJam.DevTools/LogoFiles.cs links this file), so every copy of the logo is the same.
/// Plain numbers only: nothing here depends on Avalonia.
/// </summary>
public static class LogoShapes
{
    // ---- Colours ----

    /// <summary>The played part: Deck 1's cyan.</summary>
    public const string Played = "#36C2DC";
    public const string ToCome = "#C3CBD6";
    public const string Ink = "#E8ECF2";

    /// <summary>The same three for light backgrounds (the website, documents).</summary>
    public const string PlayedOnLight = "#1597B0", ToComeOnLight = "#2A303B", InkOnLight = "#151920";

    /// <summary>The app icon's tile, top to bottom, and its edge.</summary>
    public const string TileTop = "#1E232C", TileBottom = "#0C0F14", TileEdge = "#2B313C";

    // ---- The waveform G, in a 64-unit square ----

    public const double Centre = 32, Ring = 18;
    public const double BarWidth = 1.9;

    /// <summary>Where the bars reach, with a little room: the mark without its tile.</summary>
    public const double MarkLeft = 6.2, MarkSize = 51.6;

    /// <summary>The G's own bar, from the middle out to the ring's opening.</summary>
    public const double GBarFrom = 33, GBarTo = Centre + Ring + 5, GBarWidth = 3.4;

    /// <summary>The ring runs clockwise from <see cref="RingStart"/>° (0° is 3 o'clock) to <see cref="RingEnd"/>°, opening at the top right.</summary>
    public const double RingStart = 18, RingEnd = 306, BarStep = 9, PlayedTo = 160;

    /// <summary>One bar of the waveform, mirrored around the ring.</summary>
    public readonly record struct Bar(double X1, double Y1, double X2, double Y2, bool Played);

    /// <summary>Each bar's length, as a share of the loudest: a beat's rise and fall, over a slower swell.</summary>
    private static readonly double[] Accents = [1, 0.72, 0.9, 0.62];

    public static IReadOnlyList<Bar> Bars { get; } = MakeBars();

    private static Bar[] MakeBars()
    {
        var bars = new List<Bar>();
        for (double deg = RingStart; deg <= RingEnd; deg += BarStep)
        {
            int i = (int)(deg / BarStep);
            double envelope = 0.55 + 0.45 * Math.Pow(Math.Sin(i * 0.42 + 0.6), 2);
            double h = 1.6 + 5.2 * envelope * Accents[i % 4];
            double a = deg * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
            bars.Add(new Bar(Centre + (Ring - h) * cos, Centre + (Ring - h) * sin,
                             Centre + (Ring + h) * cos, Centre + (Ring + h) * sin, deg <= PlayedTo));
        }
        return [.. bars];
    }

    /// <summary>
    /// For small sizes, where bars would blur: the ring drawn as one thick arc this wide, split at
    /// the same place, so the G stays a G down to 16 pixels.
    /// </summary>
    public const double SolidRingWidth = 9, SolidGBarWidth = 6;

    // ---- The name, on a 20-unit cap height ----

    public const double CapHeight = 20, LetterStroke = 4.4;
    public const double WordWidth = 103.4;

    /// <summary>The name's bars: 43 across, spaced so the word starts and ends on a whole bar.</summary>
    public const double WordBarWidth = 1.65;
    public const int WordBars = 43;
    public static double WordBarPitch => (WordWidth - WordBarWidth) / (WordBars - 1);

    /// <summary>
    /// A letter: SVG path data, drawn at <paramref name="X"/>, stroked <see cref="LetterStroke"/>
    /// wide with flat ends, or <paramref name="Filled"/> (Z and M, whose sharp corners a stroke
    /// would blunt).
    /// </summary>
    public readonly record struct Letter(char Char, double X, string Path, bool Filled, bool Played);

    /// <summary>
    /// GZJAM. The letters' edges are at 0 and 20 (G, A, M) or 2.2 and 17.8 (Z, J), and they're
    /// spaced by those edges; J tucks in under Z, since its top is open.
    /// </summary>
    public static IReadOnlyList<Letter> Letters { get; } =
    [
        new('G', 0, "M15.01,4.02 A7.8,7.8 0 1 0 17.8,10 M20,10 H11", Filled: false, Played: true),
        new('Z', 21.2, "M2.5,0 H17.8 V4 L7.6,15.6 H17.8 V20 H2.2 V16 L12.4,4.4 H2.5 Z", Filled: true, Played: true),
        new('J', 38.6, "M15.6,0 V12 A5.6,5.6 0 0 1 4.4,12", Filled: false, Played: false),
        new('A', 59.8, "M2.2,20 V10 A7.8,7.8 0 0 1 17.8,10 V20 M2.2,13.6 H17.8", Filled: false, Played: false),
        new('M', 83.4, "M0,20 V0 H4.6 L10,8.6 L15.4,0 H20 V20 H15.6 V7.6 L10,16.6 L4.4,7.6 V20 Z", Filled: true, Played: false),
    ];

    /// <summary>Below this many pixels tall, the name's bars blur: draw its letters solid.</summary>
    public const double WordBarsFromPixels = 24;

    /// <summary>Below this many pixels across, the G's bars blur: draw the ring solid.</summary>
    public const double MarkBarsFromPixels = 44;
}
