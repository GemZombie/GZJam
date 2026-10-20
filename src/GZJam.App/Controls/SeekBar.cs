using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace GZJam.App.Controls;

/// <summary>
/// A track position bar: played part filled, a playhead, and press-or-drag to seek (mouse, touch
/// or pen). Stands in for the waveform until waveforms arrive.
/// </summary>
public sealed class SeekBar : Control
{
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<SeekBar, double>(nameof(Progress));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<SeekBar, IBrush?>(nameof(Fill), Brushes.White);

    public static readonly StyledProperty<IBrush?> TrackBackgroundProperty =
        AvaloniaProperty.Register<SeekBar, IBrush?>(nameof(TrackBackground), new SolidColorBrush(Color.Parse("#0E1116")));

    public static readonly StyledProperty<double> CornerRadiusProperty =
        AvaloniaProperty.Register<SeekBar, double>(nameof(CornerRadius), 7);

    static SeekBar()
    {
        AffectsRender<SeekBar>(ProgressProperty, FillProperty, TrackBackgroundProperty, IsEnabledProperty);
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

    public IBrush? TrackBackground
    {
        get => GetValue(TrackBackgroundProperty);
        set => SetValue(TrackBackgroundProperty, value);
    }

    public double CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>Raised with the requested position, 0..1, while pressing or dragging.</summary>
    public event EventHandler<double>? SeekRequested;

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width < 4 || bounds.Height < 1)
            return;   // collapsed or not laid out yet
        context.DrawRectangle(TrackBackground, null, bounds, CornerRadius, CornerRadius);
        if (Fill is null)
            return;

        double x = Math.Clamp(Progress, 0, 1) * bounds.Width;
        double centre = bounds.Height / 2;

        // Unplayed: a thin centre line. Played: a softer filled band. Then the playhead.
        context.DrawRectangle(new SolidColorBrush(Colors.White, 0.08), null, new Rect(10, centre - 0.5, Math.Max(0, bounds.Width - 20), 1));
        if (x > 0)
        {
            using (context.PushOpacity(IsEnabled ? 0.28 : 0.18))
                context.DrawRectangle(Fill, null, new Rect(0, 0, x, bounds.Height), CornerRadius, CornerRadius);
        }
        context.DrawRectangle(IsEnabled ? Brushes.White : Fill, null, new Rect(Math.Clamp(x - 1, 0, bounds.Width - 2), 0, 2, bounds.Height));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        e.Pointer.Capture(this);
        RequestSeek(e);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (IsEnabled && e.Pointer.Captured == this)
            RequestSeek(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Pointer.Captured == this)
            e.Pointer.Capture(null);
    }

    private void RequestSeek(PointerEventArgs e)
    {
        if (Bounds.Width <= 0)
            return;
        double fraction = Math.Clamp(e.GetPosition(this).X / Bounds.Width, 0, 1);
        SeekRequested?.Invoke(this, fraction);
    }
}
