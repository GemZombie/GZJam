using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;

namespace GZJam.App.Controls;

/// <summary>
/// A song's album art, small and square, to see at a glance that the right record is loaded.
/// A song without art (or no song) shows a dim record in <see cref="Accent"/>, never a made-up
/// picture. The pictures come from the <see cref="CacheProperty"/> set on the window, inherited.
/// </summary>
public sealed class AlbumArt : Control
{
    /// <summary>Where pictures come from; set once on the window and inherited by every AlbumArt in it.</summary>
    public static readonly AttachedProperty<AlbumArtCache?> CacheProperty =
        AvaloniaProperty.RegisterAttached<AlbumArt, Control, AlbumArtCache?>("Cache", inherits: true);

    /// <summary>The song's file; null: no song.</summary>
    public static readonly StyledProperty<string?> SongPathProperty =
        AvaloniaProperty.Register<AlbumArt, string?>(nameof(SongPath));

    /// <summary>The colour of the record drawn when there's no art (the deck's colour).</summary>
    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<AlbumArt, IBrush?>(nameof(Accent));

    public static readonly StyledProperty<double> CornerRadiusProperty =
        AvaloniaProperty.Register<AlbumArt, double>(nameof(CornerRadius), 3);

    private static readonly IBrush Well = new ImmutableSolidColorBrush(Color.Parse("#0E1116"));
    private static readonly IBrush Neutral = new ImmutableSolidColorBrush(Color.Parse("#5D6574"));
    private static readonly IPen Edge = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#252A34")), 1);

    private AlbumArtCache? _cache;
    private Bitmap? _bitmap;
    private bool _attached;

    static AlbumArt()
    {
        AffectsRender<AlbumArt>(AccentProperty, CornerRadiusProperty);
        SongPathProperty.Changed.AddClassHandler<AlbumArt>((art, _) => art.Refresh());
        CacheProperty.Changed.AddClassHandler<AlbumArt>((art, _) => art.Attach());
    }

    public static AlbumArtCache? GetCache(Control control) => control.GetValue(CacheProperty);

    public static void SetCache(Control control, AlbumArtCache? value) => control.SetValue(CacheProperty, value);

    public string? SongPath
    {
        get => GetValue(SongPathProperty);
        set => SetValue(SongPathProperty, value);
    }

    public IBrush? Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public double CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>Whether a picture is showing (false: the record).</summary>
    internal bool HasPicture => _bitmap is not null;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Attach();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        Detach();
    }

    private void Attach()
    {
        Detach();
        if (_attached)
        {
            _cache = GetCache(this);
            if (_cache is not null)
                _cache.Found += OnFound;
        }
        Refresh();
    }

    private void Detach()
    {
        if (_cache is not null)
            _cache.Found -= OnFound;
        _cache = null;
    }

    private void OnFound(string path)
    {
        if (path == SongPath)
            Refresh();
    }

    private void Refresh()
    {
        Bitmap? bitmap = SongPath is { } path ? _cache?.Get(path) : null;
        if (ReferenceEquals(bitmap, _bitmap))
            return;
        _bitmap = bitmap;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        double size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0)
            return;
        var square = new Rect((Bounds.Width - size) / 2, (Bounds.Height - size) / 2, size, size);
        double radius = Math.Min(CornerRadius, size / 4);

        if (_bitmap is { } bitmap)
        {
            // Filled, cropped to the middle if the picture isn't square.
            Size pixels = bitmap.Size;
            double side = Math.Min(pixels.Width, pixels.Height);
            var crop = new Rect((pixels.Width - side) / 2, (pixels.Height - side) / 2, side, side);
            using (context.PushClip(new RoundedRect(square, radius)))
                context.DrawImage(bitmap, crop, square);
            return;
        }

        // No art: a dim record in the deck's colour.
        context.DrawRectangle(Well, Edge, new RoundedRect(square.Deflate(0.5), radius));
        IBrush ink = Accent ?? Neutral;
        Point centre = square.Center;
        double r = size * 0.38;
        using (context.PushOpacity(0.16))
            context.DrawEllipse(ink, null, centre, r, r);
        if (size >= 32)
        {
            // Grooves, only where there's room to see them.
            var groove = new Pen(ink, 0.8);
            using (context.PushOpacity(0.22))
            {
                context.DrawEllipse(null, groove, centre, r * 0.8, r * 0.8);
                context.DrawEllipse(null, groove, centre, r * 0.6, r * 0.6);
            }
        }
        using (context.PushOpacity(0.45))
            context.DrawEllipse(ink, null, centre, r * 0.36, r * 0.36);
        context.DrawEllipse(Well, null, centre, Math.Max(1, r * 0.08), Math.Max(1, r * 0.08));
    }
}
