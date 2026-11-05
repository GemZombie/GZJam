using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace GZJam.App.Views;

/// <summary>
/// The pictures on the taskbar's buttons (#262), drawn from GZJam's own shapes as PNGs: play and
/// pause as on the decks, fade out as the slope a fading end draws on the waveform, and auto-play
/// as the queue going on (with a dot when it's on).
/// </summary>
internal static class RoomButtonIcons
{
    public enum Icon { Play, Pause, FadeOut, AutoPlayOff, AutoPlayOn }

    private static readonly IBrush Ink = new SolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xEE));
    private static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7));

    // On a 24-unit square, as App.axaml's icons.
    private static readonly Geometry Play = Geometry.Parse("M8 5.5v13a1 1 0 0 0 1.5.9l10-6.5a1 1 0 0 0 0-1.7l-10-6.5A1 1 0 0 0 8 5.5z");
    private static readonly Geometry Pause = Geometry.Parse("M6 5h4v14H6z M14 5h4v14h-4z");
    private static readonly Geometry Fade = Geometry.Parse("M3 5h2l16 14H3z");
    private static readonly Geometry Next = Geometry.Parse("M3 6v12l8-6z M12 6v12l8-6z");
    private static readonly Geometry Dot = Geometry.Parse("M21 2.5a2.5 2.5 0 1 1 0 5a2.5 2.5 0 1 1 0-5z");

    /// <summary>The icon as a PNG, <paramref name="size"/> pixels square.</summary>
    public static byte[] Png(Icon icon, int size)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(size, size));
        using (DrawingContext context = bitmap.CreateDrawingContext())
        using (context.PushTransform(Matrix.CreateScale(size / 24.0, size / 24.0)))
        {
            Geometry shape = icon switch
            {
                Icon.Play => Play,
                Icon.Pause => Pause,
                Icon.FadeOut => Fade,
                _ => Next,
            };
            context.DrawGeometry(Ink, null, shape);
            if (icon == Icon.AutoPlayOn)
                context.DrawGeometry(Accent, null, Dot);
        }
        using var png = new MemoryStream();
        bitmap.Save(png, new PngBitmapEncoderOptions());
        return png.ToArray();
    }
}
