using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using GZJam.Audio;

namespace GZJam.App.Controls;

/// <summary>
/// What the close-up shows, apart from where the playhead is: made on the window's thread when
/// something changes (a song, the zoom, a marker), read by the render thread at every frame.
/// Nothing in it changes once made.
/// </summary>
internal sealed record CloseUpScene(
    Waveform? Waveform, double Length, double WindowSeconds,
    double TrimStart, double TrimEnd, bool StartManual, bool EndManual, double EndFade,
    IReadOnlyList<JumpMarker> Jumps, double? PendingJumpFrom, IReadOnlyList<(double At, Bitmap Flag)> Cues,
    bool CanSeek, IImmutableBrush Fill);

/// <summary>
/// Draws the close-up (#364): on the render thread at every frame while a deck plays, so the
/// window's thread, busy or not, never holds the waveform up; and on the window's thread only for
/// a picture of it on its own (tests). Like <see cref="SeekBar"/>: bars colored by frequency, what's
/// played dimmed, trimmed silence shaded, jumps and cues marked. The render thread draws only
/// rectangles, lines and pictures, so the fade's slope is a skewed rectangle and a cue's number a
/// picture made beforehand (<see cref="CueFlag"/>).
/// </summary>
internal static class CloseUpPicture
{
    private const double BarStep = 2;
    private static readonly IImmutableBrush Background = new ImmutableSolidColorBrush(Color.Parse("#0E1116"));
    private static readonly IImmutableBrush CenterLine = new ImmutableSolidColorBrush(Colors.White, 0.08);
    private static readonly IImmutableBrush TrimShade = new ImmutableSolidColorBrush(Color.FromArgb(150, 12, 14, 18));
    private static readonly IImmutableBrush TrimMarker = new ImmutableSolidColorBrush(Color.FromRgb(0x8B, 0x94, 0xA5));
    private static readonly IImmutableBrush White = new ImmutableSolidColorBrush(Colors.White);
    private static readonly IImmutableBrush Jump = (IImmutableBrush)SeekBar.JumpBrush;
    private static readonly IImmutableBrush JumpOff = (IImmutableBrush)SeekBar.JumpOffBrush;
    private static readonly IImmutableBrush Skipped = (IImmutableBrush)SeekBar.SkippedShade;
    private static readonly IImmutableBrush Repeat = (IImmutableBrush)SeekBar.RepeatShade;
    private static readonly ImmutablePen FadeLine = new(Jump, 1.5);

    /// <summary>A cue's flag: its number on a square, this size at the most.</summary>
    public const double FlagSize = 13;

    /// <summary>Draws the close-up, <paramref name="size"/> big, with the playhead <paramref name="playhead"/> seconds into the song.</summary>
    public static void Draw(ICloseUpCanvas canvas, CloseUpScene scene, double playhead, Size size)
    {
        var bounds = new Rect(size);
        if (bounds.Width < 4 || bounds.Height < 1)
            return;
        canvas.FillRounded(Background, bounds, 3);
        double middle = Math.Round(bounds.Width / 2);
        if (scene.Length > 0 && scene.WindowSeconds > 0)
        {
            var view = new View(scene, playhead, bounds);
            canvas.PushClip(bounds);
            if (scene.Waveform is { Count: > 0, Peak: > 0, LoudnessPeak: > 0 } waveform)
                DrawBars(canvas, waveform, view, middle);
            else
                canvas.Fill(CenterLine, new Rect(0, bounds.Height / 2 - 0.5, bounds.Width, 1));
            DrawTrim(canvas, scene, view);
            DrawJumps(canvas, scene, view);
            DrawCues(canvas, scene, view);
            canvas.Pop();
        }
        canvas.Fill(scene.CanSeek ? White : scene.Fill, new Rect(middle - 1, 0, 2, bounds.Height));
    }

    /// <summary>The stretch of the song in view: where it starts (s), seconds a pixel, and the close-up's size.</summary>
    private readonly record struct View(double Start, double SecondsPerPixel, double Length, Rect Bounds)
    {
        public View(CloseUpScene scene, double playhead, Rect bounds)
            : this(playhead - scene.WindowSeconds / 2, scene.WindowSeconds / bounds.Width, scene.Length, bounds)
        {
        }

        /// <summary>Where a fraction of the song is, in pixels (may be off either side).</summary>
        public double X(double fraction) => (Math.Clamp(fraction, 0, 1) * Length - Start) / SecondsPerPixel;
    }

    private static void DrawBars(ICloseUpCanvas canvas, Waveform waveform, View view, double playhead)
    {
        Rect bounds = view.Bounds;
        double center = bounds.Height / 2, reach = center - 3;
        double maxPeak = Waveform.Decode(waveform.Peak), maxLoudness = Waveform.Decode(waveform.LoudnessPeak);
        double slicesPerSecond = waveform.Count / view.Length, start = view.Start, spp = view.SecondsPerPixel;
        // Each bar is a fixed stretch of the song (not of the screen), so it keeps its shape as it
        // slides past; the first one may start just off the left edge.
        double barSeconds = BarStep * spp;
        for (long bar = (long)Math.Floor(start / barSeconds); ; bar++)
        {
            double from = bar * barSeconds, x = (from - start) / spp;
            if (x >= bounds.Width)
                break;
            if (CloseUpWaveform.BarLevels(waveform, from * slicesPerSecond, (from + barSeconds) * slicesPerSecond) is not var (low, mid, high, loud))
                continue;
            double body = Math.Max(0.5, Waveform.Decode(loud) / maxLoudness * reach);
            double peak = Math.Max(body, Waveform.Decode(Math.Max(low, Math.Max(mid, high))) / maxPeak * reach);
            BandPaint paint = BandPaint.For(low, mid, high);   // shared and kept: nothing made per frame (#363)
            bool played = x + BarStep <= playhead;
            canvas.Fill((IImmutableBrush)(played ? paint.Dim : paint.Outline), new Rect(x, center - peak, BarStep, peak * 2));
            canvas.Fill((IImmutableBrush)(played ? paint.Dim : paint.Bright), new Rect(x, center - body, BarStep, body * 2));
        }
    }

    private static void DrawTrim(ICloseUpCanvas canvas, CloseUpScene scene, View view)
    {
        Rect bounds = view.Bounds;
        double start = view.X(scene.TrimStart), end = view.X(scene.TrimEnd);
        // Before the song's own start and after its end (the file's edges) is plain background.
        double fileStart = view.X(0), fileEnd = view.X(1);
        if (start > 0 && (scene.TrimStart > 0 || scene.StartManual))
        {
            canvas.Fill(TrimShade, new Rect(Math.Max(0, fileStart), 0, Math.Max(0, start - Math.Max(0, fileStart)), bounds.Height));
            canvas.Fill(scene.StartManual ? Jump : TrimMarker, new Rect(start - (scene.StartManual ? 1 : 0.5), 0, scene.StartManual ? 2 : 1, bounds.Height));
        }
        if (end < bounds.Width && (scene.TrimEnd < 1 || scene.EndManual))
        {
            double fade = view.X(scene.TrimEnd + scene.EndFade) - end;
            DrawEndShade(canvas, end, fade, Math.Min(bounds.Width, fileEnd), bounds.Height);
            canvas.Fill(scene.EndManual ? Jump : TrimMarker, new Rect(end - (scene.EndManual ? 1 : 0.5), 0, scene.EndManual ? 2 : 1, bounds.Height));
        }
    }

    /// <summary>
    /// After the song's end: shaded, the shade coming in along a slope over its fade (#267), as on
    /// <see cref="SeekBar"/>. The slope's triangle is a rectangle skewed along it, clipped to the fade.
    /// </summary>
    private static void DrawEndShade(ICloseUpCanvas canvas, double end, double fade, double right, double height)
    {
        if (fade < 1)
        {
            canvas.Fill(TrimShade, new Rect(end, 0, Math.Max(0, right - end), height));
            return;
        }
        canvas.PushClip(new Rect(end, 0, Math.Max(0, Math.Min(fade, right - end)), height));
        // x' = x + (y - 0) * fade / height, about (end, 0): the rectangle's left edge on the slope.
        canvas.PushTransform(new Matrix(1, 0, fade / height, 1, 0, 0));
        canvas.Fill(TrimShade, new Rect(end, 0, fade, height));
        canvas.Pop();
        canvas.Line(FadeLine, new Point(end, 0), new Point(end + fade, height));
        canvas.Pop();
        if (right > end + fade)
            canvas.Fill(TrimShade, new Rect(end + fade, 0, right - end - fade, height));
    }

    private static void DrawJumps(ICloseUpCanvas canvas, CloseUpScene scene, View view)
    {
        Rect bounds = view.Bounds;
        foreach (JumpMarker jump in scene.Jumps)
        {
            double from = view.X(jump.From), to = view.X(jump.To);
            IImmutableBrush brush = jump.Armed ? Jump : JumpOff;
            if (jump.Armed)
                canvas.Fill(to > from ? Skipped : Repeat, new Rect(Math.Min(from, to), 0, Math.Abs(to - from), bounds.Height));
            canvas.Fill(brush, new Rect(from - 1, 0, 2, bounds.Height));
            canvas.Fill(brush, new Rect(to - 1, 0, 2, bounds.Height));
            // Its arrow, pointing the way it jumps: a triangle in thin strips.
            const double arrow = 6;
            double direction = to > from ? 1 : -1, y = bounds.Height - arrow - 1;
            for (double i = 0; i < arrow; i += 0.5)
            {
                double half = arrow / 2 * (1 - i / arrow);
                canvas.Fill(brush, new Rect(direction > 0 ? from + i : from - i - 0.5, y - half, 0.5, half * 2));
            }
        }
        if (scene.PendingJumpFrom is { } pending)
            canvas.Fill(JumpOff, new Rect(view.X(pending) - 1, 0, 2, bounds.Height));
    }

    private static void DrawCues(ICloseUpCanvas canvas, CloseUpScene scene, View view)
    {
        Rect bounds = view.Bounds;
        double flag = Math.Min(FlagSize, bounds.Height);
        foreach (var (atFraction, picture) in scene.Cues)
        {
            double at = view.X(atFraction);
            if (at < -1 || at > bounds.Width + 1)
                continue;   // off the close-up: not pinned to its edge
            canvas.Fill(Jump, new Rect(at - 1, 0, 2, bounds.Height));
            double left = Math.Clamp(at - 1, 0, bounds.Width - flag);
            canvas.Picture(picture, new Rect(left, 0, flag, flag));
        }
    }

    // ───────────── A cue's flag, as a picture ─────────────

    private static readonly Dictionary<(int Number, double Scaling), Bitmap> Flags = [];

    /// <summary>
    /// A cue's flag with its number, as a picture the render thread can draw (it can't draw text):
    /// made on the window's thread the first time it's wanted, and kept. Copied out of the render
    /// target, as the startup logo's name is, so nothing draws on it again.
    /// </summary>
    public static Bitmap CueFlag(int number, double scaling)
    {
        if (Flags.TryGetValue((number, scaling), out Bitmap? made))
            return made;
        int pixels = (int)Math.Ceiling(FlagSize * scaling);
        using var target = new RenderTargetBitmap(new PixelSize(pixels, pixels), new Vector(96, 96));
        using (var context = target.CreateDrawingContext())
        using (context.PushTransform(Matrix.CreateScale(scaling, scaling)))
            SeekBar.DrawCue(context, new Rect(0, 0, FlagSize, FlagSize), 1, number);
        var image = new WriteableBitmap(target.PixelSize, target.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var locked = image.Lock())
            target.CopyPixels(locked);
        return Flags[(number, scaling)] = image;
    }
}

/// <summary>What the close-up is drawn on: the render thread's drawing, or the window's (<see cref="CloseUpPicture"/>).</summary>
internal interface ICloseUpCanvas
{
    void Fill(IImmutableBrush brush, Rect rect);
    void FillRounded(IImmutableBrush brush, Rect rect, double radius);
    void Line(ImmutablePen pen, Point from, Point to);
    void Picture(Bitmap bitmap, Rect rect);
    void PushClip(Rect rect);
    void PushTransform(Matrix matrix);

    /// <summary>Undoes the last <see cref="PushClip"/> or <see cref="PushTransform"/>.</summary>
    void Pop();
}

/// <summary>The render thread's drawing (each frame), as an <see cref="ICloseUpCanvas"/>. Nothing made per frame.</summary>
internal sealed class ImmediateCanvas : ICloseUpCanvas
{
    private readonly Stack<ImmediateDrawingContext.PushedState> _pushed = new();
    private ImmediateDrawingContext? _context;

    public ImmediateCanvas On(ImmediateDrawingContext context)
    {
        _context = context;
        _pushed.Clear();
        return this;
    }

    private ImmediateDrawingContext Context => _context!;

    public void Fill(IImmutableBrush brush, Rect rect) => Context.FillRectangle(brush, rect);
    public void FillRounded(IImmutableBrush brush, Rect rect, double radius) => Context.DrawRectangle(brush, null, rect, radius, radius);
    public void Line(ImmutablePen pen, Point from, Point to) => Context.DrawLine(pen, from, to);
    public void Picture(Bitmap bitmap, Rect rect) => Context.DrawBitmap(bitmap, rect);
    public void PushClip(Rect rect) => _pushed.Push(Context.PushClip(rect));
    public void PushTransform(Matrix matrix) => _pushed.Push(Context.PushPreTransform(matrix));
    public void Pop() => _pushed.Pop().Dispose();
}

/// <summary>The window's drawing, as an <see cref="ICloseUpCanvas"/>: for a picture of the close-up on its own.</summary>
internal sealed class ControlCanvas(DrawingContext context) : ICloseUpCanvas
{
    private readonly Stack<DrawingContext.PushedState> _pushed = new();

    public void Fill(IImmutableBrush brush, Rect rect) => context.DrawRectangle(brush, null, rect);
    public void FillRounded(IImmutableBrush brush, Rect rect, double radius) => context.DrawRectangle(brush, null, rect, radius, radius);
    public void Line(ImmutablePen pen, Point from, Point to) => context.DrawLine(pen, from, to);
    public void Picture(Bitmap bitmap, Rect rect) => context.DrawImage(bitmap, rect);
    public void PushClip(Rect rect) => _pushed.Push(context.PushClip(rect));
    public void PushTransform(Matrix matrix) => _pushed.Push(context.PushTransform(matrix));
    public void Pop() => _pushed.Pop().Dispose();
}
