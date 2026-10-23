using Avalonia;
using Avalonia.Controls;

namespace GZJam.App.Controls;

/// <summary>
/// Lays its children over what's beneath it, at the size it's given, but asks for no room of its
/// own: a hint, a label or a key appearing over the waveform never makes the deck taller (nothing
/// moves as a deck's state changes). A child taller than the room it gets draws past its edge.
/// </summary>
public sealed class OverlayPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (Control child in Children)
            child.Measure(availableSize);
        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (Control child in Children)
            child.Arrange(new Rect(finalSize));
        return finalSize;
    }
}
