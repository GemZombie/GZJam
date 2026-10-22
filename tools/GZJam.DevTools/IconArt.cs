using SkiaSharp;

namespace GZJam.DevTools;

/// <summary>
/// Draws GZJam's app icon at each size it ships in, so small sizes are drawn for their pixels
/// rather than scaled down: a platter on a graphite tile, with a steel-blue position ring like the
/// light ring round a jog wheel.
/// </summary>
internal static class IconArt
{
    private static readonly SKColor TileTop = SKColor.Parse("#1F242D");
    private static readonly SKColor TileBottom = SKColor.Parse("#0E1116");
    private static readonly SKColor TileEdge = SKColor.Parse("#363D4B");
    private static readonly SKColor RingTrack = SKColor.Parse("#262C37");
    private static readonly SKColor Accent = SKColor.Parse("#7FA3CF");
    private static readonly SKColor VinylCentre = SKColor.Parse("#171A21");
    private static readonly SKColor VinylEdge = SKColor.Parse("#090B0E");
    private static readonly SKColor Label = SKColor.Parse("#E7EAF0");
    private static readonly SKColor Spindle = SKColor.Parse("#0C0E12");

    /// <summary>Writes the icon files into the repository at <paramref name="repo"/>.</summary>
    public static int Generate(string repo)
    {
        string assets = Path.Combine(repo, "src", "GZJam.App", "Assets");
        Directory.CreateDirectory(assets);

        WriteIco(Path.Combine(assets, "gzjam.ico"), [16, 20, 24, 32, 40, 48, 64, 256]);
        File.WriteAllBytes(Path.Combine(assets, "gzjam.png"), Png(256, mac: false));
        WriteIcns(Path.Combine(repo, "packaging", "macos", "GZJam.icns"));

        string preview = Path.Combine(Path.GetTempPath(), "gzjam-icon-preview.png");
        WritePreview(preview);
        Console.WriteLine($"Icons written; preview: {preview}");
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
        var c = new SKPoint(s / 2, s / 2);

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
            paint.Style = SKPaintStyle.Fill;
        }

        // The position ring: a dim track, three quarters lit in the accent, starting at the top.
        float ringRadius = tile * 0.385f;
        float ringWidth = Math.Max(tile * 0.06f, 1.75f);
        var ringRect = new SKRect(c.X - ringRadius, c.Y - ringRadius, c.X + ringRadius, c.Y + ringRadius);
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = ringWidth;
        paint.Color = RingTrack;
        canvas.DrawOval(ringRect, paint);
        paint.StrokeCap = SKStrokeCap.Round;
        if (s >= 64)
        {
            paint.Color = Accent.WithAlpha(110);
            paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, tile * 0.018f);
            canvas.DrawArc(ringRect, -90, 270, false, paint);
            paint.MaskFilter = null;
        }
        paint.Color = Accent;
        canvas.DrawArc(ringRect, -90, 270, false, paint);
        paint.StrokeCap = SKStrokeCap.Butt;
        paint.Style = SKPaintStyle.Fill;

        // The record: near-black, with grooves and a sheen when there's room for them.
        float vinyl = tile * 0.31f;
        paint.Shader = SKShader.CreateRadialGradient(c, vinyl, [VinylCentre, VinylEdge], SKShaderTileMode.Clamp);
        canvas.DrawCircle(c, vinyl, paint);
        paint.Shader = null;
        float labelRadius = tile * 0.125f;
        if (s >= 96)
        {
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = px * 0.75f;
            paint.Color = SKColors.White.WithAlpha(13);
            for (float r = labelRadius + tile * 0.03f; r < vinyl - tile * 0.012f; r += tile * 0.016f)
                canvas.DrawCircle(c, r, paint);
            paint.Style = SKPaintStyle.Fill;
        }
        if (s >= 48)
        {
            var clear = SKColors.White.WithAlpha(0);
            var shine = SKColors.White.WithAlpha(22);
            paint.Shader = SKShader.CreateSweepGradient(c,
                [clear, shine, clear, clear, shine, clear, clear],
                [0f, 0.09f, 0.18f, 0.5f, 0.59f, 0.68f, 1f]);
            canvas.Save();
            canvas.RotateDegrees(-20, c.X, c.Y);
            canvas.DrawCircle(c, vinyl, paint);
            canvas.Restore();
            paint.Shader = null;
        }

        // The index mark at the top of the platter, as on a jog wheel: it turns with the record.
        if (s >= 32)
        {
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = Math.Max(tile * 0.022f, 1.25f);
            paint.StrokeCap = SKStrokeCap.Round;
            paint.Color = Label.WithAlpha(220);
            canvas.DrawLine(c.X, c.Y - vinyl * 0.93f, c.X, c.Y - vinyl * 0.64f, paint);
            paint.StrokeCap = SKStrokeCap.Butt;
            paint.Style = SKPaintStyle.Fill;
        }

        // The label, light like the badge in the app's top bar, and the spindle.
        paint.Color = Label;
        canvas.DrawCircle(c, labelRadius, paint);
        float spindle = tile * 0.024f;
        if (spindle >= 0.8f)
        {
            paint.Color = Spindle;
            canvas.DrawCircle(c, spindle, paint);
        }
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
