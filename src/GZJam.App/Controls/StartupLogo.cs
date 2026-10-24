using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;

namespace GZJam.App.Controls;

/// <summary>
/// The startup screen's logo (issue #49): the waveform G's bars rise into place, then the played
/// colour sweeps round the ring and on along the name as GZJam gets ready (<see cref="BeginStep"/>,
/// <see cref="Finish"/>). It's drawn on Avalonia's render thread, which keeps going while the UI
/// thread is busy building the main window, so it moves when nothing else can.
/// </summary>
public sealed class StartupLogo : Control
{
    private CompositionCustomVisual? _visual;
    private readonly List<object> _pending = [];

    /// <summary>A step of getting ready begins, covering <paramref name="from"/> to <paramref name="to"/> of the sweep.</summary>
    public void BeginStep(double from, double to) => Send(new StartupLogoHandler.Step(from, to));

    /// <summary>Everything is ready: the sweep runs to the end.</summary>
    public void Finish() => Send(new StartupLogoHandler.Finish());

    private void Send(object message)
    {
        if (_visual is null)
            _pending.Add(message);
        else
            _visual.SendHandlerMessage(message);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (ElementComposition.GetElementVisual(this)?.Compositor is not { } compositor)
            return;

        _visual = compositor.CreateCustomVisual(new StartupLogoHandler());
        ElementComposition.SetElementChildVisual(this, _visual);
        _visual.Size = new Vector(Bounds.Width, Bounds.Height);
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        _visual.SendHandlerMessage(new StartupLogoHandler.Name(
            NameImage(StartupLogoHandler.ToComeBrush, scaling), NameImage(StartupLogoHandler.PlayedBrush, scaling)));
        foreach (object message in _pending)
            _visual.SendHandlerMessage(message);
        _pending.Clear();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_visual is not null)
            _visual.Size = new Vector(e.NewSize.Width, e.NewSize.Height);
    }

    /// <summary>
    /// The name in bars, all in one colour, as an image for the render thread (which can draw
    /// images, but not the letters' shapes). Copied out of the render target, so nothing draws on
    /// it again.
    /// </summary>
    private static Bitmap NameImage(IBrush brush, double scaling)
    {
        // At 96 dpi, one unit a pixel: the render thread reads a bitmap's source area in pixels.
        double cap = StartupLogoHandler.NameCap * scaling;
        var size = new Size(cap * LogoShapes.WordWidth / LogoShapes.CapHeight, cap);
        using var target = new RenderTargetBitmap(PixelSize.FromSize(size, 1), new Vector(96, 96));
        using (var context = target.CreateDrawingContext())
        using (context.PushTransform(Matrix.CreateScale(cap / LogoShapes.CapHeight, cap / LogoShapes.CapHeight)))
            Wordmark.DrawBars(context, _ => brush);

        var image = new WriteableBitmap(target.PixelSize, target.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var pixels = image.Lock())
            target.CopyPixels(pixels);
        return image;
    }
}

/// <summary>Draws <see cref="StartupLogo"/>, on the render thread.</summary>
internal sealed class StartupLogoHandler : CompositionCustomVisualHandler
{
    public sealed record Step(double From, double To);
    public sealed record Finish;
    public sealed record Name(Bitmap ToCome, Bitmap Played);

    public static readonly ImmutableSolidColorBrush PlayedBrush = new(Color.Parse(LogoShapes.Played));
    public static readonly ImmutableSolidColorBrush ToComeBrush = new(Color.Parse(LogoShapes.ToCome));

    /// <summary>Sizes, in device-independent pixels: the G, the name's cap height, and the space between.</summary>
    public const double MarkSize = 84, NameCap = 30, Gap = 22;

    /// <summary>The sweep's first part goes round the ring; the rest along the name.</summary>
    private const double RingShare = 0.6;

    /// <summary>The bars rise one after another, starting this long apart in all, each taking this long.</summary>
    private const double RiseSpread = 0.25, RiseSeconds = 0.3;

    private static readonly double RiseEnd = RiseSpread + RiseSeconds + 0.25;

    private readonly StartupProgress _progress = new();
    private TimeSpan? _start;
    private Bitmap? _toCome, _played;

    public override void OnMessage(object message)
    {
        switch (message)
        {
            case Step step:
                _progress.BeginStep(step.From, step.To, CompositionNow);
                break;
            case Finish:
                _progress.Finish();
                break;
            case Name name:
                (_toCome, _played) = (name.ToCome, name.Played);
                break;
        }
        RegisterForNextAnimationFrameUpdate();
    }

    public override void OnAnimationFrameUpdate()
    {
        Invalidate();
        bool risen = _start is { } start && (CompositionNow - start).TotalSeconds > RiseEnd;
        if (!(risen && _progress.IsComplete))
            RegisterForNextAnimationFrameUpdate();
    }

    public override void OnRender(ImmediateDrawingContext context)
    {
        _start ??= CompositionNow;
        double t = (CompositionNow - _start.Value).TotalSeconds;
        double progress = _progress.Update(CompositionNow);
        double ring = Math.Clamp(progress / RingShare, 0, 1);
        double name = Math.Clamp((progress - RingShare) / (1 - RingShare), 0, 1);

        double nameWidth = NameCap * LogoShapes.WordWidth / LogoShapes.CapHeight;
        double left = Math.Round((EffectiveSize.X - (MarkSize + Gap + nameWidth)) / 2);
        double top = Math.Round((EffectiveSize.Y - MarkSize) / 2);
        DrawMark(context, new Point(left, top), t, ring);

        if (_toCome is null || _played is null)
            return;
        var nameRect = new Rect(left + MarkSize + Gap, top + (MarkSize - NameCap) / 2, nameWidth, NameCap);
        using (context.PushOpacity(Ease(Math.Clamp((t - 0.15) / 0.3, 0, 1)), nameRect))
        {
            context.DrawBitmap(_toCome, nameRect);
            if (name > 0)
            {
                using (context.PushClip(nameRect.WithWidth(nameRect.Width * name)))
                    context.DrawBitmap(_played, nameRect);
            }
        }
    }

    /// <summary>The G, its bars rising one by one, lit as far round as <paramref name="ring"/> (0 to 1).</summary>
    private static void DrawMark(ImmediateDrawingContext context, Point at, double t, double ring)
    {
        double k = MarkSize / LogoShapes.MarkSize;
        Point Map(double x, double y) => new(at.X + (x - LogoShapes.MarkLeft) * k, at.Y + (y - LogoShapes.MarkLeft) * k);

        var played = new ImmutablePen(PlayedBrush, LogoShapes.BarWidth * k);
        var toCome = new ImmutablePen(ToComeBrush, LogoShapes.BarWidth * k);
        var bars = LogoShapes.Bars;
        for (int i = 0; i < bars.Count; i++)
        {
            double rise = Ease(Math.Clamp((t - RiseSpread * i / bars.Count) / RiseSeconds, 0, 1));
            if (rise <= 0)
                continue;
            var bar = bars[i];
            double midX = (bar.X1 + bar.X2) / 2, midY = (bar.Y1 + bar.Y2) / 2;
            double halfX = (bar.X2 - bar.X1) / 2 * rise, halfY = (bar.Y2 - bar.Y1) / 2 * rise;
            bool lit = ring >= (i + 1.0) / bars.Count;
            context.DrawLine(lit ? played : toCome, Map(midX - halfX, midY - halfY), Map(midX + halfX, midY + halfY));
        }

        // The G's bar slides in from the ring's opening once the ring is up; it's where the sweep starts.
        double slide = Ease(Math.Clamp((t - RiseSpread) / RiseSeconds, 0, 1));
        if (slide > 0)
        {
            var pen = new ImmutablePen(ring > 0 ? PlayedBrush : ToComeBrush, LogoShapes.GBarWidth * k);
            double from = LogoShapes.GBarTo - (LogoShapes.GBarTo - LogoShapes.GBarFrom) * slide;
            context.DrawLine(pen, Map(from, LogoShapes.Centre), Map(LogoShapes.GBarTo, LogoShapes.Centre));
        }
    }

    /// <summary>Ease out: quick, then settling.</summary>
    private static double Ease(double x) => 1 - Math.Pow(1 - x, 3);
}
