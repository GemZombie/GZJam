using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace GZJam.App.Controls;

/// <summary>
/// The on-air bar: a thin red strip that fills left to right as the song the room hears plays
/// (#165). The part played is bright red, the rest a dim red, so the whole strip still says "on
/// air" at a glance, even at the start of a song. Drawn along the top of the deck on air, and in
/// the line that stands in for hidden decks.
/// </summary>
public sealed class AirBar : Control
{
    /// <summary>How far into the song, 0..1 (see <see cref="ViewModels.DeckViewModel.AirProgress"/>).</summary>
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<AirBar, double>(nameof(Progress));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<AirBar, IBrush?>(nameof(Fill), new ImmutableSolidColorBrush(Color.Parse("#F04444")));

    /// <summary>How strong the part still to play is, against the played part's full color.</summary>
    private const double RestOpacity = 0.3;

    static AirBar()
    {
        AffectsRender<AirBar>(ProgressProperty, FillProperty);
    }

    public double Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Fill is not { } fill)
            return;
        Rect bounds = new(Bounds.Size);
        using (context.PushOpacity(RestOpacity))
            context.FillRectangle(fill, bounds);
        double played = Math.Round(bounds.Width * Math.Clamp(Progress, 0, 1));
        if (played > 0)
            context.FillRectangle(fill, bounds.WithWidth(played));
    }
}
