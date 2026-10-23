using System.Globalization;
using System.Text;
using GZJam.App.Controls;

namespace GZJam.DevTools;

/// <summary>
/// Writes the logo as SVG files into branding/, from the same shapes the app draws
/// (<see cref="LogoShapes"/>): the icon, the mark alone, the name (in bars, and solid for small
/// sizes) and the two together, each for dark and light backgrounds. branding/README.md says
/// which to use where.
/// </summary>
internal static class LogoFiles
{
    private sealed record Theme(string Played, string ToCome, string Ink);

    private static readonly Theme Dark = new(LogoShapes.Played, LogoShapes.ToCome, LogoShapes.Ink);
    private static readonly Theme Light = new(LogoShapes.PlayedOnLight, LogoShapes.ToComeOnLight, LogoShapes.InkOnLight);

    public static void Write(string repo)
    {
        string folder = Path.Combine(repo, "branding");
        Directory.CreateDirectory(folder);
        var files = new Dictionary<string, string>
        {
            ["gzjam-icon.svg"] = Icon(),
            ["gzjam-mark.svg"] = Mark(Dark),
            ["gzjam-mark-light.svg"] = Mark(Light),
            ["gzjam-wordmark.svg"] = Wordmark(Dark, solid: false),
            ["gzjam-wordmark-light.svg"] = Wordmark(Light, solid: false),
            ["gzjam-wordmark-solid.svg"] = Wordmark(Dark, solid: true),
            ["gzjam-wordmark-solid-light.svg"] = Wordmark(Light, solid: true),
            ["gzjam-logo.svg"] = Logo(Dark),
            ["gzjam-logo-light.svg"] = Logo(Light),
        };
        foreach (var (name, text) in files)
            File.WriteAllText(Path.Combine(folder, name), text.ReplaceLineEndings("\n"), new UTF8Encoding(false));
    }

    private static string N(double value) => Math.Round(value, 2).ToString(CultureInfo.InvariantCulture);

    private const string TileDefs =
        $"""<linearGradient id="tile" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="{LogoShapes.TileTop}"/><stop offset="1" stop-color="{LogoShapes.TileBottom}"/></linearGradient>""";

    private const string Tile =
        $"""<rect x="1" y="1" width="62" height="62" rx="12.4" fill="url(#tile)" stroke="{LogoShapes.TileEdge}"/>""";

    private static string WaveG(Theme theme)
    {
        var s = new StringBuilder("<g>");
        foreach (var bar in LogoShapes.Bars)
            s.Append($"""<line x1="{N(bar.X1)}" y1="{N(bar.Y1)}" x2="{N(bar.X2)}" y2="{N(bar.Y2)}" stroke="{(bar.Played ? theme.Played : theme.ToCome)}" stroke-width="{N(LogoShapes.BarWidth)}"/>""");
        s.Append($"""<line x1="{N(LogoShapes.GBarFrom)}" y1="{N(LogoShapes.Centre)}" x2="{N(LogoShapes.GBarTo)}" y2="{N(LogoShapes.Centre)}" stroke="{theme.Played}" stroke-width="{N(LogoShapes.GBarWidth)}"/>""");
        return s.Append("</g>").ToString();
    }

    private static string Letters(Theme theme, bool solid)
    {
        var s = new StringBuilder();
        foreach (var letter in LogoShapes.Letters)
        {
            string colour = solid ? theme.Ink : letter.Played ? theme.Played : theme.ToCome;
            string paint = letter.Filled
                ? $"""fill="{colour}" """
                : $"""fill="none" stroke="{colour}" stroke-width="{N(LogoShapes.LetterStroke)}" """;
            s.Append($"""<path transform="translate({N(letter.X)} 0)" d="{letter.Path}" {paint}/>""");
        }
        return s.ToString();
    }

    private static string BarClip()
    {
        var s = new StringBuilder("""<clipPath id="bars">""");
        for (int i = 0; i < LogoShapes.WordBars; i++)
            s.Append($"""<rect x="{N(i * LogoShapes.WordBarPitch)}" y="-1" width="{N(LogoShapes.WordBarWidth)}" height="{N(LogoShapes.CapHeight + 2)}"/>""");
        return s.Append("</clipPath>").ToString();
    }

    /// <summary>The name, and the definitions it needs.</summary>
    private static (string Body, string Defs) Name(Theme theme, bool solid) => solid
        ? ($"<g>{Letters(theme, solid: true)}</g>", "")
        : ($"""<g clip-path="url(#bars)">{Letters(theme, solid: false)}</g>""", BarClip());

    private static string Svg(double x, double y, double width, double height, string body, string defs = "") =>
        $"""
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="{N(x)} {N(y)} {N(width)} {N(height)}" width="{N(width)}" height="{N(height)}">
          <title>GZJam</title>{(defs == "" ? "" : $"\n  <defs>{defs}</defs>")}
          {body}
        </svg>

        """;

    private static string Icon() => Svg(0, 0, 64, 64, Tile + WaveG(Dark), TileDefs);

    private static string Mark(Theme theme) =>
        Svg(LogoShapes.MarkLeft, LogoShapes.MarkLeft, LogoShapes.MarkSize, LogoShapes.MarkSize, WaveG(theme));

    private static string Wordmark(Theme theme, bool solid)
    {
        var (body, defs) = Name(theme, solid);
        return Svg(-1, -1, LogoShapes.WordWidth + 2, LogoShapes.CapHeight + 2, body, defs);
    }

    /// <summary>The icon beside the name: the name's cap height is 0.41 of the icon, centred on it.</summary>
    private static string Logo(Theme theme)
    {
        const double Cap = 26, Gap = 19;
        double scale = Cap / LogoShapes.CapHeight;
        var (body, defs) = Name(theme, solid: false);
        return Svg(0, 0, 64 + Gap + LogoShapes.WordWidth * scale, 64,
            Tile + WaveG(Dark) + $"""<g transform="translate({N(64 + Gap)} {N((64 - Cap) / 2)}) scale({N(scale)})">{body}</g>""",
            TileDefs + defs);
    }
}
