using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace GZJam.App.Controls;

/// <summary>
/// A waveform bar's brushes, by its color (<see cref="SeekBar.BandColor"/>): bright for what's to
/// come, dimmed once played, and fainter for the peaks' outline. One set for every waveform on
/// screen, made the first time a color is wanted and kept (#363). The close-up's own cache was
/// full after a few songs, and then made three brushes for every bar of every frame: garbage the
/// window stopped to tidy away all night. Colors are kept to 32 shades a channel (32,768 at the
/// most, far fewer in practice), too fine to tell apart on a bar 2 pixels wide.
/// </summary>
internal sealed class BandPaint
{
    private const int Bits = 5;
    private static readonly BandPaint?[] Made = new BandPaint?[1 << (3 * Bits)];

    private BandPaint(Color color)
    {
        Bright = new ImmutableSolidColorBrush(color);
        Dim = new ImmutableSolidColorBrush(color, 0.3);
        Outline = new ImmutableSolidColorBrush(color, 0.22);
    }

    public IBrush Bright { get; }
    public IBrush Dim { get; }
    public IBrush Outline { get; }

    /// <summary>The brushes for a bar of these band levels.</summary>
    public static BandPaint For(byte low, byte mid, byte high) => For(SeekBar.BandColor(low, mid, high));

    public static BandPaint For(Color color)
    {
        int key = (Level(color.R) << (2 * Bits)) | (Level(color.G) << Bits) | Level(color.B);
        return Made[key] ??= new BandPaint(Color.FromRgb(Full(key >> (2 * Bits)), Full(key >> Bits), Full(key)));
    }

    private const int Top = (1 << Bits) - 1;

    /// <summary>The nearest of the kept shades to a channel's value.</summary>
    private static int Level(byte value) => (value * Top + 127) / 255;

    /// <summary>A kept shade, spread back over 0-255: within 4 of what was wanted.</summary>
    private static byte Full(int level) => (byte)(((level & Top) * 255 + Top / 2) / Top);

    /// <summary>How many colors have been wanted so far (tests).</summary>
    internal static int Count => Made.Count(p => p is not null);
}
