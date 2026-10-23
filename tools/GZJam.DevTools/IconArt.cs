using GZJam.App.Controls;
using SkiaSharp;

namespace GZJam.DevTools;

/// <summary>
/// Draws GZJam's app icon at each size it ships in, so small sizes are drawn for their pixels
/// rather than scaled down: the logo's waveform G (<see cref="LogoShapes"/>) on a graphite tile.
/// Small, the G's ring is drawn solid, as the app's <see cref="LogoMark"/> does.
/// </summary>
internal static class IconArt
{
    private static readonly SKColor TileTop = SKColor.Parse(LogoShapes.TileTop);
    private static readonly SKColor TileBottom = SKColor.Parse(LogoShapes.TileBottom);
    private static readonly SKColor TileEdge = SKColor.Parse(LogoShapes.TileEdge);
    private static readonly SKColor Played = SKColor.Parse(LogoShapes.Played);
    private static readonly SKColor ToCome = SKColor.Parse(LogoShapes.ToCome);

    /// <summary>Writes the icon files, and the logo's SVG files, into the repository at <paramref name="repo"/>.</summary>
    public static int Generate(string repo)
    {
        string assets = Path.Combine(repo, "src", "GZJam.App", "Assets");
        Directory.CreateDirectory(assets);

        WriteIco(Path.Combine(assets, "gzjam.ico"), [16, 20, 24, 32, 40, 48, 64, 256]);
        File.WriteAllBytes(Path.Combine(assets, "gzjam.png"), Png(256, mac: false));
        WriteIcns(Path.Combine(repo, "packaging", "macos", "GZJam.icns"));
        LogoFiles.Write(repo);

        string preview = Path.Combine(Path.GetTempPath(), "gzjam-icon-preview.png");
        WritePreview(preview);
        Console.WriteLine($"Icons and branding/*.svg written; preview: {preview}");
        return 0;
    }

    /// <summary>
    /// Draws the icon. <paramref name="mac"/>: the macOS layout, a smaller tile with room around it
    /// (Apple's grid: 824 of 1024); otherwise the tile fills the square, which reads better small.
    /// </summary>
    public static void Draw(SKCanvas canvas, int size, bool mac)
    {
        float s = size;
        float inset = mac ? s * 100 / 1024 : s >= 64 ? s * 0.03f : 0;
        float tile = s - 2 * inset;
        float radius = mac ? s * 185 / 1024 : tile * 0.2f;
        float px = Math.Max(1, s / 256);   // a hairline that stays one pixel when small

        using var paint = new SKPaint { IsAntialias = true };

        // The tile: graphite, lit from above, with a fine edge.
        var tileRect = new SKRect(inset, inset, s - inset, s - inset);
        if (mac)
        {
            paint.Color = new SKColor(0, 0, 0, 90);
            paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, s * 0.012f);
            canvas.DrawRoundRect(new SKRect(tileRect.Left, tileRect.Top + s * 0.012f, tileRect.Right, tileRect.Bottom + s * 0.012f), radius, radius, paint);
            paint.MaskFilter = null;
            paint.Color = SKColors.Black;   // opaque again: the paint's alpha also applies to a shader
        }
        paint.Shader = SKShader.CreateLinearGradient(new SKPoint(0, tileRect.Top), new SKPoint(0, tileRect.Bottom), [TileTop, TileBottom], SKShaderTileMode.Clamp);
        canvas.DrawRoundRect(tileRect, radius, radius, paint);
        paint.Shader = null;
        if (s >= 32)
        {
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = px;
            paint.Color = TileEdge;
            var edge = tileRect;
            edge.Inflate(-px / 2, -px / 2);
            canvas.DrawRoundRect(edge, radius - px / 2, radius - px / 2, paint);
        }

        // The G, laid out on the logo's 64-unit square, whose tile spans 1 to 63.
        float unit = tile / 62;
        canvas.Save();
        canvas.Translate(inset - unit, inset - unit);
        canvas.Scale(unit);
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeCap = SKStrokeCap.Butt;
        if (tile >= LogoShapes.MarkBarsFromPixels)
        {
            paint.StrokeWidth = (float)LogoShapes.BarWidth;
            foreach (var bar in LogoShapes.Bars)
            {
                paint.Color = bar.Played ? Played : ToCome;
                canvas.DrawLine((float)bar.X1, (float)bar.Y1, (float)bar.X2, (float)bar.Y2, paint);
            }
            paint.StrokeWidth = (float)LogoShapes.GBarWidth;
        }
        else
        {
            float r = (float)LogoShapes.Ring, c = (float)LogoShapes.Centre, half = (float)LogoShapes.BarStep / 2;
            var ring = new SKRect(c - r, c - r, c + r, c + r);
            float start = (float)LogoShapes.RingStart - half, split = (float)LogoShapes.PlayedTo + half, end = (float)LogoShapes.RingEnd + half;
            paint.StrokeWidth = (float)LogoShapes.SolidRingWidth;
            paint.Color = Played;
            canvas.DrawArc(ring, start, split - start, false, paint);
            paint.Color = ToCome;
            canvas.DrawArc(ring, split, end - split, false, paint);
            paint.StrokeWidth = (float)LogoShapes.SolidGBarWidth;
        }
        paint.Color = Played;
        canvas.DrawLine((float)LogoShapes.GBarFrom, (float)LogoShapes.Centre, (float)LogoShapes.GBarTo, (float)LogoShapes.Centre, paint);
        canvas.Restore();
    }

    private static byte[] Png(int size, bool mac)
    {
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);
        Draw(surface.Canvas, size, mac);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>A Windows .ico holding a PNG per size (supported since Windows Vista).</summary>
    private static void WriteIco(string path, int[] sizes)
    {
        var images = sizes.Select(size => Png(size, mac: false)).ToList();
        using var w = new BinaryWriter(File.Create(path));
        w.Write((ushort)0);
        w.Write((ushort)1);
        w.Write((ushort)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0);   // no palette
            w.Write((byte)0);
            w.Write((ushort)1);   // planes
            w.Write((ushort)32);   // bits per pixel
            w.Write(images[i].Length);
            w.Write(offset);
            offset += images[i].Length;
        }
        foreach (byte[] image in images)
            w.Write(image);
    }

    /// <summary>A macOS .icns holding PNGs, from 16 px to 512 px @2x.</summary>
    private static void WriteIcns(string path)
    {
        (string Type, int Size)[] entries =
        [
            ("icp4", 16), ("icp5", 32), ("ic11", 32), ("ic12", 64), ("ic07", 128),
            ("ic13", 256), ("ic08", 256), ("ic14", 512), ("ic09", 512), ("ic10", 1024),
        ];
        var chunks = entries.Select(e => (e.Type, Data: Png(e.Size, mac: true))).ToList();
        using var w = new BinaryWriter(File.Create(path));
        w.Write("icns"u8);
        WriteBigEndian(w, 8 + chunks.Sum(c => 8 + c.Data.Length));
        foreach (var (type, data) in chunks)
        {
            w.Write(System.Text.Encoding.ASCII.GetBytes(type));
            WriteBigEndian(w, 8 + data.Length);
            w.Write(data);
        }
    }

    private static void WriteBigEndian(BinaryWriter w, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        w.Write(bytes);
    }

    /// <summary>Every size on a dark and a light strip, the small ones also blown up to see their pixels.</summary>
    private static void WritePreview(string path)
    {
        int[] sizes = [16, 24, 32, 48, 64, 128, 256];
        const int Strip = 300;
        int width = sizes.Sum() + 20 * (sizes.Length + 1) + 3 * (16 + 24 + 32) * 4 + 80 + 512 + 40;
        using var surface = SKSurface.Create(new SKImageInfo(width, Strip * 2));
        var canvas = surface.Canvas;
        canvas.Clear(SKColor.Parse("#202124"));
        using (var light = new SKPaint { Color = SKColor.Parse("#ECEDEF") })
            canvas.DrawRect(0, Strip, width, Strip, light);

        using var sampling = new SKPaint();
        for (int row = 0; row < 2; row++)
        {
            float x = 20;
            float y = row * Strip + 20;
            foreach (int size in sizes)
            {
                using var bitmap = SKBitmap.Decode(Png(size, mac: false));
                canvas.DrawBitmap(bitmap, x, y + (256 - size) / 2f);
                x += size + 20;
            }
            foreach (int size in (int[])[16, 24, 32])
            {
                using var image = SKImage.FromEncodedData(Png(size, mac: false));
                canvas.DrawImage(image, new SKRect(x, y, x + size * 4, y + size * 4), new SKSamplingOptions(SKFilterMode.Nearest), sampling);
                x += size * 4 + 20;
            }
            using var mac = SKBitmap.Decode(Png(256, mac: true));
            canvas.DrawBitmap(mac, x + 40, y);
        }
        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }
}
