using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using GZJam.Library;

namespace GZJam.App.Controls;

/// <summary>Where a song is from (#318), for the lists that mix sources: the queue, History and playlists.</summary>
public enum SongSource
{
    MyMusic,
    Server,
    Files,
    Stick,
}

/// <summary>
/// A song's source, as a small icon (#318): a record for My music, a cloud for a server, a folder
/// for the computer's files, a stick for a USB stick. The queue, History and playlists mix
/// sources (a set is a set), so each row says where its song is from, always.
/// </summary>
public sealed class SourceIcon : Control
{
    public static readonly StyledProperty<Track?> TrackProperty =
        AvaloniaProperty.Register<SourceIcon, Track?>(nameof(Track));

    /// <summary>Where a song is from, and what to say of it: set by the window's view model, which knows the music folders.</summary>
    public static Func<Track, (SongSource Source, string Tip)> Resolve { get; set; } = _ => (SongSource.MyMusic, "From My music");

    private static readonly IPen Pen = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#8F9BB0")), 2,
        lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    private static readonly IBrush Fill = new ImmutableSolidColorBrush(Color.Parse("#8F9BB0"));

    // In a 24 x 24 box, as the library column's own icons.
    private static readonly Geometry Record = Geometry.Parse("M12 3a9 9 0 1 1 0 18a9 9 0 1 1 0-18z M12 10a2 2 0 1 1 0 4a2 2 0 1 1 0-4z");
    private static readonly Geometry Cloud = Geometry.Parse("M7.2 18.5a4.2 4.2 0 0 1-.7-8.35A5.8 5.8 0 0 1 17.6 8.4a4.6 4.6 0 0 1-.2 10.1z");
    private static readonly Geometry Folder = Geometry.Parse("M3 6.5A1.5 1.5 0 0 1 4.5 5h4.2l2 2h8.8A1.5 1.5 0 0 1 21 8.5v9a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 17.5z");
    private static readonly Geometry Stick = Geometry.Parse("M9 2h6v6H9z M7 8h10v13a1 1 0 0 1-1 1H8a1 1 0 0 1-1-1z");

    static SourceIcon() => AffectsRender<SourceIcon>(TrackProperty);

    public SourceIcon()
    {
        Width = 12;
        Height = 12;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
    }

    public Track? Track
    {
        get => GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    /// <summary>The source shown now (for tests); null without a song.</summary>
    public SongSource? Source { get; private set; }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != TrackProperty)
            return;
        if (Track is { } track)
        {
            (SongSource source, string tip) = Resolve(track);
            Source = source;
            ToolTip.SetTip(this, tip);
        }
        else
        {
            Source = null;
            ToolTip.SetTip(this, null);
        }
    }

    public override void Render(DrawingContext context)
    {
        double size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0 || Source is not { } source)
            return;
        var scale = Matrix.CreateScale(size / 24, size / 24) *
                    Matrix.CreateTranslation((Bounds.Width - size) / 2, (Bounds.Height - size) / 2);
        using var _ = context.PushTransform(scale);
        switch (source)
        {
            case SongSource.MyMusic:
                context.DrawGeometry(null, Pen, Record);
                break;
            case SongSource.Server:
                context.DrawGeometry(null, Pen, Cloud);
                break;
            case SongSource.Files:
                context.DrawGeometry(Fill, null, Folder);
                break;
            case SongSource.Stick:
                context.DrawGeometry(null, Pen, Stick);
                break;
        }
    }
}
